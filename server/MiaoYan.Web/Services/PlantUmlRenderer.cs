using System.Diagnostics;
using System.Text;
using MiaoYan.Web.Options;

namespace MiaoYan.Web.Services;

/// <summary>
/// Renders PlantUML with the local jar. The public PlantUML server is never
/// contacted: diagram source is note content and must not leave the host.
/// </summary>
public sealed class PlantUmlRenderer
{
    public const int MaxSourceBytes = 64 * 1024;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);
    private static readonly string[] DangerousDirectives =
    [
        "!include",
        "!includesub",
        "!includeurl",
        "!import",
        "!includedef",
        "!define",
        "!definelong",
        "!theme",
        "!procedure",
        "!function",
        "!unquoted",
    ];

    private readonly CapabilitiesService _capabilities;
    private readonly ILogger<PlantUmlRenderer> _logger;

    public PlantUmlRenderer(CapabilitiesService capabilities, ILogger<PlantUmlRenderer> logger)
    {
        _capabilities = capabilities;
        _logger = logger;
    }

    public bool Available => _capabilities.PlantUmlAvailable;

    /// <summary>
    /// Rejects directives that can read local files, fetch URLs, or reach the
    /// environment. Remote rendering was never an option; this also stops a
    /// multi-user host from reading another user's vault through !include.
    /// </summary>
    public static string? Validate(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return "Diagram source is empty.";
        }

        if (Encoding.UTF8.GetByteCount(source) > MaxSourceBytes)
        {
            return "Diagram source is too large.";
        }

        foreach (var rawLine in source.Split('\n'))
        {
            var line = rawLine.TrimStart();
            if (line.Length == 0)
            {
                continue;
            }

            if (line[0] == '!')
            {
                var directive = DangerousDirectives.FirstOrDefault(
                    item => line.StartsWith(item, StringComparison.OrdinalIgnoreCase));
                if (directive is not null)
                {
                    return $"PlantUML directive {directive} is disabled on this server.";
                }
            }
        }

        if (source.Contains("http://", StringComparison.OrdinalIgnoreCase)
            || source.Contains("https://", StringComparison.OrdinalIgnoreCase))
        {
            return "PlantUML diagrams cannot reference remote URLs on this server.";
        }

        return null;
    }

    public async Task<PlantUmlResult> RenderAsync(string source, CancellationToken cancellationToken)
    {
        var jar = _capabilities.PlantUmlJarPath;
        if (jar is null)
        {
            return PlantUmlResult.Unavailable();
        }

        var validationError = Validate(source);
        if (validationError is not null)
        {
            return PlantUmlResult.Invalid(validationError);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "java",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        startInfo.ArgumentList.Add("-jar");
        startInfo.ArgumentList.Add(jar);
        startInfo.ArgumentList.Add("-tsvg");
        startInfo.ArgumentList.Add("-pipe");
        startInfo.ArgumentList.Add("-charset");
        startInfo.ArgumentList.Add("UTF-8");
        // LEGACY == the long-standing -secure behaviour. Source validation above
        // additionally removes directives this flag might otherwise allow.
        startInfo.ArgumentList.Add("-secure");

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to start PlantUML");
            return PlantUmlResult.Unavailable();
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);

        try
        {
            await process.StandardInput.WriteAsync(source);
            await process.StandardInput.FlushAsync(timeout.Token);
            process.StandardInput.Close();

            var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);

            await process.WaitForExitAsync(timeout.Token);
            var svg = await outputTask;
            var error = await errorTask;

            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(svg))
            {
                _logger.LogWarning(
                    "PlantUML exited with {ExitCode}: {Error}",
                    process.ExitCode,
                    error.Length > 500 ? error[..500] : error);
                return PlantUmlResult.Invalid("PlantUML could not render this diagram.");
            }

            return PlantUmlResult.Success(svg);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return PlantUmlResult.Invalid("PlantUML timed out.");
        }
        finally
        {
            TryKill(process);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
    }
}

public sealed record PlantUmlResult(bool Ok, bool Available, string? Svg, string? Error)
{
    public static PlantUmlResult Success(string svg) => new(true, true, svg, null);

    public static PlantUmlResult Unavailable() =>
        new(false, false, null, "PlantUML is not installed on this server.");

    public static PlantUmlResult Invalid(string error) => new(false, true, null, error);
}
