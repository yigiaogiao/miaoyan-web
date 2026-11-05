using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using MiaoYan.Web.Options;

namespace MiaoYan.Web.Services;

/// <summary>
/// Optional protection for standalone deployments.
///
/// LazyCat deployments are already authenticated by the gateway (it injects
/// <c>X-HC-User-ID</c>) and normally run without a password. Everywhere else a
/// password protects the browser UI and an API key protects machine clients.
/// Credentials live only in configuration and are compared in constant time.
/// Sessions are stateless HMAC cookies, so a container restart does not log
/// everyone out as long as the state directory survives.
/// </summary>
public sealed class AuthService
{
    public const string SessionCookie = "miaoyan_session";
    private const string ApiKeyHeader = "X-API-Key";
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(30);
    private static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(5);
    private const int MaxFailures = 8;

    private readonly MiaoYanOptions _options;
    private readonly ILogger<AuthService> _logger;
    private readonly byte[] _secret;
    private readonly byte[] _credentialHash;
    private readonly ConcurrentDictionary<string, Attempts> _failures = new(StringComparer.Ordinal);

    public AuthService(MiaoYanOptions options, VaultRegistry vaults, ILogger<AuthService> logger)
    {
        _options = options;
        _logger = logger;
        _secret = LoadOrCreateSecret(Path.Combine(vaults.StateBase, ".auth-secret"));
        _credentialHash = SHA256.HashData(Encoding.UTF8.GetBytes(
            (options.WebPassword ?? string.Empty) + "\0" + (options.ApiKey ?? string.Empty)));

        if (Enabled)
        {
            _logger.LogInformation(
                "Authentication enabled: password={HasPassword}, apiKey={HasApiKey}",
                HasPassword,
                HasApiKey);
        }
    }

    public bool HasPassword => !string.IsNullOrWhiteSpace(_options.WebPassword);
    public bool HasApiKey => !string.IsNullOrWhiteSpace(_options.ApiKey);
    public bool Enabled => HasPassword || HasApiKey;

    public static bool IsPublicPath(PathString path)
        => path.StartsWithSegments("/healthz")
           || path == "/api/health"
           || path.StartsWithSegments("/api/auth");

    /// <summary>
    /// A request is authenticated by a session cookie, an API key, or both.
    /// When nothing is configured every request is allowed (development and
    /// LazyCat, where the gateway is the boundary).
    /// </summary>
    public bool IsAuthenticated(HttpContext context)
    {
        if (!Enabled)
        {
            return true;
        }

        if (ValidateApiKey(context))
        {
            return true;
        }

        return context.Request.Cookies.TryGetValue(SessionCookie, out var token)
               && ValidateToken(token);
    }

    /// <summary>True when the only credential presented was the session cookie.</summary>
    public bool UsesCookie(HttpContext context)
        => !ValidateApiKey(context)
           && context.Request.Cookies.ContainsKey(SessionCookie);

    public bool CheckOrigin(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.FirstOrDefault();
        if (string.IsNullOrEmpty(origin))
        {
            // Non-browser clients do not send Origin; SameSite already blocks
            // the browser-driven cross-site case.
            return true;
        }

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var parsed))
        {
            return false;
        }

        var forwardedProto = context.Request.Headers["X-Forwarded-Proto"].FirstOrDefault();
        var scheme = !string.IsNullOrWhiteSpace(forwardedProto) && _options.TrustProxy
            ? forwardedProto.Split(',')[0].Trim()
            : context.Request.Scheme;
        var forwardedHost = context.Request.Headers["X-Forwarded-Host"].FirstOrDefault();
        var host = !string.IsNullOrWhiteSpace(forwardedHost) && _options.TrustProxy
            ? forwardedHost.Split(',')[0].Trim()
            : context.Request.Host.Value;

        return string.Equals(parsed.Scheme, scheme, StringComparison.OrdinalIgnoreCase)
               && string.Equals(parsed.Authority, host, StringComparison.OrdinalIgnoreCase);
    }

    public LoginResult Login(HttpContext context, string? secret)
    {
        var remote = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (IsRateLimited(remote))
        {
            return LoginResult.RateLimited;
        }

        var candidate = secret ?? string.Empty;
        var passwordOk = HasPassword && FixedEquals(candidate, _options.WebPassword!);
        var keyOk = HasApiKey && FixedEquals(candidate, _options.ApiKey!);
        if (passwordOk || keyOk)
        {
            _failures.TryRemove(remote, out _);
            return LoginResult.Success;
        }

        RecordFailure(remote);
        return LoginResult.Invalid;
    }

    public void IssueSession(HttpContext context)
    {
        var expiry = DateTimeOffset.UtcNow.Add(SessionLifetime).ToUnixTimeSeconds();
        var token = $"{expiry}.{Base64Url(Sign(expiry))}";
        context.Response.Cookies.Append(SessionCookie, token, CookieOptions(context, SessionLifetime));
    }

    public void ClearSession(HttpContext context)
        => context.Response.Cookies.Delete(SessionCookie, CookieOptions(context, null));

    public bool ValidateToken(string token)
    {
        var dot = token.IndexOf('.');
        if (dot <= 0 || dot == token.Length - 1)
        {
            return false;
        }

        if (!long.TryParse(token.AsSpan(0, dot), out var expiry))
        {
            return false;
        }

        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= expiry)
        {
            return false;
        }

        byte[] supplied;
        try
        {
            supplied = Base64UrlDecode(token[(dot + 1)..]);
        }
        catch (FormatException)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(supplied, Sign(expiry));
    }

    private bool ValidateApiKey(HttpContext context)
    {
        if (!HasApiKey)
        {
            return false;
        }

        var supplied = context.Request.Headers[ApiKeyHeader].FirstOrDefault();
        if (string.IsNullOrEmpty(supplied))
        {
            var authorization = context.Request.Headers.Authorization.FirstOrDefault();
            if (authorization is not null
                && authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                supplied = authorization["Bearer ".Length..].Trim();
            }
        }

        return !string.IsNullOrEmpty(supplied) && FixedEquals(supplied, _options.ApiKey!);
    }

    private byte[] Sign(long expiry)
    {
        var payload = Encoding.UTF8.GetBytes($"{expiry}\n{Convert.ToHexString(_credentialHash)}");
        return HMACSHA256.HashData(_secret, payload);
    }

    private static bool FixedEquals(string supplied, string expected)
    {
        var left = Encoding.UTF8.GetBytes(supplied);
        var right = Encoding.UTF8.GetBytes(expected);
        return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
    }

    private bool IsRateLimited(string remote)
    {
        if (!_failures.TryGetValue(remote, out var attempts))
        {
            return false;
        }

        lock (attempts)
        {
            attempts.Prune();
            return attempts.Timestamps.Count >= MaxFailures;
        }
    }

    private void RecordFailure(string remote)
    {
        var attempts = _failures.GetOrAdd(remote, _ => new Attempts());
        lock (attempts)
        {
            attempts.Timestamps.Add(DateTimeOffset.UtcNow);
            attempts.Prune();
        }
    }

    private static CookieOptions CookieOptions(HttpContext context, TimeSpan? lifetime)
    {
        var forwardedProto = context.Request.Headers["X-Forwarded-Proto"].FirstOrDefault();
        var secure = context.Request.IsHttps
                     || (forwardedProto?.Split(',')[0].Trim()
                         .Equals("https", StringComparison.OrdinalIgnoreCase) ?? false);
        return new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = secure,
            Path = "/",
            IsEssential = true,
            MaxAge = lifetime,
        };
    }

    private static byte[] LoadOrCreateSecret(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var existing = File.ReadAllBytes(path);
                if (existing.Length == 32)
                {
                    return existing;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Fall through to an ephemeral secret; sessions then end on restart.
        }

        var secret = RandomNumberGenerator.GetBytes(32);
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(path, secret);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Read-only state: keep the process-local secret.
        }

        return secret;
    }

    private static string Base64Url(byte[] value)
        => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var text = value.Replace('-', '+').Replace('_', '/');
        text = text.PadRight(text.Length + (4 - text.Length % 4) % 4, '=');
        return Convert.FromBase64String(text);
    }

    private sealed class Attempts
    {
        public List<DateTimeOffset> Timestamps { get; } = [];

        public void Prune()
        {
            var cutoff = DateTimeOffset.UtcNow - FailureWindow;
            Timestamps.RemoveAll(at => at < cutoff);
        }
    }
}

public enum LoginResult
{
    Success,
    Invalid,
    RateLimited,
}
