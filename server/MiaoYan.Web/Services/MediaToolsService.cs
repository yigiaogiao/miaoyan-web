using System.Diagnostics;
using System.Text;
using MiaoYan.Web.Options;

namespace MiaoYan.Web.Services;

/// <summary>
/// Optional local OCR / speech-to-text hooks. Commands come only from
/// configuration, never from requests. The asset path is resolved through the
/// vault sandbox first, and the process gets no shell: placeholders become
/// individual argv entries.
/// </summary>
public sealed class MediaToolsService
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);
    private const int MaxOutputChars = 2_000_000;

    private readonly MiaoYanOptions _options;
    private readonly VaultService _vault;
    private readonly ILogger<MediaToolsService> _logger;

    public MediaToolsService(MiaoYanOptions options, VaultService vault, ILogger<MediaToolsService> logger)
    {
        _options = options;
        _vault = vault;
        _logger = logger;
    }

    public string? CommandFor(string kind) => kind switch
    {
        "ocr" => FirstNonEmpty(_options.OcrCommand, Environment.GetEnvironmentVariable("MIAOYAN_OCR_CMD")),
        "transcribe" => FirstNonEmpty(_options.WhisperCommand, Environment.GetEnvironmentVariable("MIAOYAN_WHISPER_CMD")),
        _ => null,
    };

    public async Task<string> RunAsync(
        VaultRuntime runtime,
        string kind,
        string noteId,
        string? assetPath,
        CancellationToken cancellationToken)
    {
        var command = CommandFor(kind)
                      ?? throw new ApiException(
                          StatusCodes.Status503ServiceUnavailable,
                          "not-configured",
                          $"{kind} is not configured on this server.");

        if (string.IsNullOrWhiteSpace(assetPath))
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "asset-required", "An asset path is required.");
        }

        var asset = _vault.ResolveAsset(runtime, noteId, assetPath);
        var mediaKind = MediaLibrary.KindFor(Path.GetExtension(asset.FullPath));
        if (kind == "ocr" && mediaKind != "image")
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "unsupported-media", "OCR needs an image.");
        }

        if (kind == "transcribe" && mediaKind is not ("audio" or "video"))
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "unsupported-media", "Transcription needs audio or video.");
        }

        var outputDir = Directory.CreateTempSubdirectory("miaoyan-" + kind + "-").FullName;
        var outputFile = Path.Combine(outputDir, "result.txt");
        try
        {
            var arguments = SplitArguments(command)
                .Select(token => token
                    .Replace("{input}", asset.FullPath, StringComparison.Ordinal)
                    .Replace("{outputDir}", outputDir, StringComparison.Ordinal)
                    .Replace("{output}", outputFile, StringComparison.Ordinal))
                .ToList();

            if (arguments.Count == 0)
            {
                throw new ApiException(
                    StatusCodes.Status503ServiceUnavailable,
                    "not-configured",
                    $"{kind} command is empty.");
            }

            var (stdout, stderr, exitCode) = await ExecuteAsync(arguments, outputDir, cancellationToken);
            if (exitCode != 0)
            {
                _logger.LogWarning("{Kind} command exited with {ExitCode}: {Error}", kind, exitCode, Truncate(stderr, 500));
                throw new ApiException(
                    StatusCodes.Status422UnprocessableEntity,
                    "tool-failed",
                    Truncate(stderr, 400) is { Length: > 0 } detail ? detail : $"{kind} command failed.");
            }

            // {output} is the explicit contract; whisper-style tools instead
            // drop a *.txt next to the working directory, handled below.
            var text = string.Empty;
            if (File.Exists(outputFile))
            {
                text = await File.ReadAllTextAsync(outputFile, cancellationToken);
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                text = stdout;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                var produced = Directory.EnumerateFiles(outputDir, "*.txt")
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();
                if (produced is not null)
                {
                    text = await File.ReadAllTextAsync(produced, cancellationToken);
                }
            }

            text = text.Trim();
            if (text.Length > MaxOutputChars)
            {
                text = text[..MaxOutputChars];
            }

            return text;
        }
        finally
        {
            try
            {
                Directory.Delete(outputDir, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static async Task<(string Stdout, string Stderr, int ExitCode)> ExecuteAsync(
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(arguments[0])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        for (var i = 1; i < arguments.Count; i++)
        {
            start.ArgumentList.Add(arguments[i]);
        }

        using var process = new Process { StartInfo = start };
        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new ApiException(
                StatusCodes.Status503ServiceUnavailable,
                "tool-unavailable",
                "The configured command could not be started.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            throw new ApiException(
                StatusCodes.Status504GatewayTimeout,
                "tool-timeout",
                "The command took too long or the request was cancelled.");
        }

        return (await stdout, await stderr, process.ExitCode);
    }

    /// <summary>
    /// Splits a configured command line into argv items. Quotes group words;
    /// both quote styles are accepted so OCR language lists survive.
    /// </summary>
    internal static IReadOnlyList<string> SplitArguments(string command)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var quote = '\0';
        var hasToken = false;

        for (var i = 0; i < command.Length; i++)
        {
            var ch = command[i];
            if (quote != '\0')
            {
                if (ch == quote)
                {
                    quote = '\0';
                }
                else if (ch == '\\' && quote == '"' && i + 1 < command.Length
                         && command[i + 1] is '"' or '\\')
                {
                    current.Append(command[++i]);
                }
                else
                {
                    current.Append(ch);
                }

                hasToken = true;
                continue;
            }

            if (ch is '"' or '\'')
            {
                quote = ch;
                hasToken = true;
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                if (hasToken)
                {
                    result.Add(current.ToString());
                    current.Clear();
                    hasToken = false;
                }

                continue;
            }

            current.Append(ch);
            hasToken = true;
        }

        if (hasToken)
        {
            result.Add(current.ToString());
        }

        return result;
    }

    private static string Truncate(string value, int max)
    {
        value = value.Trim();
        return value.Length <= max ? value : value[..max];
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}
