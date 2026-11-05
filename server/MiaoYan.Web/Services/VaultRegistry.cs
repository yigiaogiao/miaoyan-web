using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using MiaoYan.Web.Options;

namespace MiaoYan.Web.Services;

/// <summary>
/// One user's library. Everything that depends on the filesystem hangs off a
/// runtime, so a single-container app can serve several LazyCat users without
/// mixing their notes or indexes.
/// </summary>
public sealed class VaultRuntime
{
    public required string Key { get; init; }
    public required MiaoYanPaths Paths { get; init; }
    public SemaphoreSlim InitialisationLock { get; } = new(1, 1);
}

/// <summary>
/// Resolves the current request's vault.
///
/// LazyCat signs every authenticated request with <c>X-HC-User-ID</c> and mounts
/// private app documents at <c>/lzcapp/documents/&lt;uid&gt;</c>. A single
/// instance can therefore serve a whole family without sharing folders. A
/// standalone deployment can pin one folder with <c>MIAOYAN_LIBRARY</c>.
/// </summary>
public sealed class VaultRegistry
{
    public const string UserHeader = "X-HC-User-ID";

    private readonly ConcurrentDictionary<string, VaultRuntime> _runtimes = new(StringComparer.Ordinal);
    private readonly MiaoYanOptions _options;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<VaultRegistry> _logger;

    private readonly string? _explicitLibrary;
    private readonly string? _explicitState;
    private readonly string? _documentsRoot;
    private readonly bool _documentsConfigured;
    private readonly string _stateBase;
    private readonly string _developmentRoot;

    public VaultRegistry(MiaoYanOptions options, IConfiguration configuration, IHostEnvironment environment, ILogger<VaultRegistry> logger)
    {
        _options = options;
        _environment = environment;
        _logger = logger;

        _explicitLibrary = NormalisePath(FirstNonEmpty(
            options.Library,
            Environment.GetEnvironmentVariable("MIAOYAN_LIBRARY"),
            configuration["Miaoyan:Library"]));
        _explicitState = NormalisePath(FirstNonEmpty(
            options.StateDir,
            Environment.GetEnvironmentVariable("MIAOYAN_STATE_DIR"),
            configuration["Miaoyan:StateDir"]));

        _developmentRoot = Path.Combine(environment.ContentRootPath, "data");

        // Official mount: document.private exposes /lzcapp/documents/<uid>
        // (LazyCat v1.5+, gateway injects the signed X-HC-User-ID header).
        // MIAOYAN_DOCUMENTS_ROOT allows the same layout off LazyCat and in
        // tests. /lzcapp exists on a LazyCat host too, so use the package
        // content mount as the container marker. If we are in an app container
        // but document.private was not granted, writing to the container
        // rootfs would silently lose notes; fail fast instead.
        var configuredDocuments = NormalisePath(_options.DocumentsRoot);
        var inAppContainer = Directory.Exists("/lzcapp/pkg");
        if (inAppContainer
            && configuredDocuments is null
            && !Directory.Exists("/lzcapp/documents")
            && _explicitLibrary is null)
        {
            throw new InvalidOperationException(
                "LazyCat app container has no /lzcapp/documents mount. " +
                "Declare the document.private permission in package.yml and reinstall the app.");
        }

        _documentsConfigured = configuredDocuments is not null;
        _documentsRoot = configuredDocuments
            ?? (Directory.Exists("/lzcapp/documents") ? "/lzcapp/documents" : null);
        if (_documentsRoot is not null)
        {
            Directory.CreateDirectory(_documentsRoot);
        }

        var lazyCatVar = Directory.Exists("/lzcapp/var") ? "/lzcapp/var/miaoyan" : null;
        _stateBase = _explicitState
            ?? lazyCatVar
            ?? Path.Combine(_developmentRoot, ".miaoyan-state");

        Directory.CreateDirectory(_stateBase);
        ProbeWritable(_stateBase, "state");

        _logger.LogInformation(
            "Vault mode: {Mode}; documents={Documents}; state={State}; explicitLibrary={Library}",
            Mode,
            _documentsRoot ?? "(none)",
            _stateBase,
            _explicitLibrary ?? "(none)");
    }

    public string Mode => _explicitLibrary is not null
        ? "explicit"
        : _documentsRoot is not null
            ? (_documentsConfigured ? "documents" : "lazycat")
            : "development";

    public string StateBase => _stateBase;
    public string? DocumentsRoot => _documentsRoot;
    public string? ExplicitLibrary => _explicitLibrary;
    public string? DefaultUid => FirstNonEmpty(_options.DefaultUid, Environment.GetEnvironmentVariable("MIAOYAN_DEFAULT_UID"));

    /// <summary>All user folders currently visible to the app.</summary>
    public IReadOnlyList<string> UserIds
    {
        get
        {
            if (_documentsRoot is null || !Directory.Exists(_documentsRoot))
            {
                return [];
            }

            try
            {
                return Directory.EnumerateDirectories(_documentsRoot)
                    .Select(Path.GetFileName)
                    .Where(name => !string.IsNullOrWhiteSpace(name) && !name.StartsWith('.'))
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToList()!;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Cannot enumerate {Documents}", _documentsRoot);
                return [];
            }
        }
    }

    /// <summary>Resolve for a request, using the signed LazyCat user header.</summary>
    public VaultRuntime Resolve(HttpContext context)
        => Resolve(HeaderUid(context), requiresUser: true);

    /// <summary>
    /// Resolve for background work or the health check. With no request the
    /// default UID, or the only visible user, is used.
    /// </summary>
    public VaultRuntime Resolve(string? uid = null, bool requiresUser = false)
    {
        if (_explicitLibrary is not null)
        {
            return GetOrCreate("explicit", () => BuildExplicit());
        }

        if (_documentsRoot is null)
        {
            // Development fallback: one library under ./data.
            return GetOrCreate("development", () => BuildRuntime(
                key: "development",
                vaultRoot: Path.Combine(_developmentRoot, "MiaoYan"),
                stateRoot: Path.Combine(_stateBase, "development")));
        }

        var chosen = uid;
        if (string.IsNullOrWhiteSpace(chosen))
        {
            chosen = DefaultUid;
        }

        if (string.IsNullOrWhiteSpace(chosen))
        {
            var users = UserIds;
            if (users.Count == 1)
            {
                chosen = users[0];
            }
        }

        if (string.IsNullOrWhiteSpace(chosen))
        {
            var message = UserIds.Count == 0
                ? "No user documents folder is available. Open the app through the LazyCat client."
                : "Multiple LazyCat users exist; send X-HC-User-ID or configure MIAOYAN_DEFAULT_UID.";
            throw new VaultResolutionException(message, requiresUser ? StatusCodes.Status401Unauthorized : StatusCodes.Status409Conflict);
        }

        var safeUid = ValidateUserSegment(chosen);
        return GetOrCreate(safeUid, () => BuildRuntime(
            key: safeUid,
            vaultRoot: Path.Combine(_documentsRoot, safeUid, "MiaoYan"),
            stateRoot: Path.Combine(_stateBase, StateSegment(safeUid))));
    }

    public VaultRuntime? TryResolveDefault()
    {
        try
        {
            return Resolve();
        }
        catch (VaultResolutionException)
        {
            return null;
        }
    }

    public static string? HeaderUid(HttpContext context)
    {
        var value = context.Request.Headers[UserHeader].FirstOrDefault();
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private VaultRuntime GetOrCreate(string key, Func<VaultRuntime> factory)
        => _runtimes.GetOrAdd(key, _ =>
        {
            var runtime = factory();
            runtime.Paths.EnsureCreated();
            ProbeWritable(runtime.Paths.VaultRoot, "vault");
            ProbeWritable(runtime.Paths.StateRoot, "state");
            _logger.LogInformation("Vault ready for '{Key}': {VaultRoot}", runtime.Key, runtime.Paths.VaultRoot);
            return runtime;
        });

    private VaultRuntime BuildExplicit()
        => BuildRuntime(
            key: "explicit",
            vaultRoot: _explicitLibrary!,
            stateRoot: _explicitState ?? Path.Combine(_stateBase, "explicit"));

    private static VaultRuntime BuildRuntime(string key, string vaultRoot, string stateRoot)
        => new()
        {
            Key = key,
            Paths = new MiaoYanPaths
            {
                VaultRoot = Path.GetFullPath(vaultRoot),
                StateRoot = Path.GetFullPath(stateRoot),
            },
        };

    /// <summary>
    /// The header comes from the trusted ingress. It must still be a single
    /// path segment: no separators, no traversal, no control characters.
    /// </summary>
    private static string ValidateUserSegment(string value)
    {
        if (value.Length is 0 or > 128
            || value is "." or ".."
            || value.Contains('/') || value.Contains('\\')
            || value.Any(char.IsControl))
        {
            throw new VaultResolutionException("Invalid LazyCat user identifier.", StatusCodes.Status400BadRequest);
        }

        return value;
    }

    private static string StateSegment(string uid)
    {
        var safe = uid.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')
            ? uid
            : "u-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uid)))[..16].ToLowerInvariant();
        return safe is "." or ".." ? "u-invalid" : safe;
    }

    private static void ProbeWritable(string directory, string label)
    {
        var probe = Path.Combine(directory, $".miaoyan-write-probe-{Guid.NewGuid():N}");
        File.WriteAllText(probe, "ok");
        File.Delete(probe);
    }

    private static string? NormalisePath(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : Path.GetFullPath(value.Trim());

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}

public sealed class VaultResolutionException(string message, int statusCode)
    : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
