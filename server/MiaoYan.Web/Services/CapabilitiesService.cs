using System.Diagnostics;
using MiaoYan.Web.Options;
using Microsoft.Data.Sqlite;

namespace MiaoYan.Web.Services;

/// <summary>
/// Probes optional local capabilities once. The UI and <c>/healthz</c> use this
/// to explain what is available instead of silently falling back to a remote
/// service.
/// </summary>
public sealed class CapabilitiesService
{
    private readonly Lazy<SqliteCapabilities> _sqlite;
    private readonly MiaoYanOptions _options;

    public CapabilitiesService(MiaoYanOptions options)
    {
        _options = options;
        _sqlite = new Lazy<SqliteCapabilities>(ProbeSqlite);
    }

    public SqliteCapabilities Sqlite => _sqlite.Value;

    public bool PlantUmlAvailable => !string.IsNullOrWhiteSpace(PlantUmlJarPath) && File.Exists(PlantUmlJarPath);

    public string? PlantUmlJarPath =>
        FirstExisting(
            _options.PlantUmlJar,
            Environment.GetEnvironmentVariable("MIAOYAN_PLANTUML_JAR"),
            "/opt/plantuml/plantuml.jar");

    public bool WhisperConfigured => !string.IsNullOrWhiteSpace(
        _options.WhisperCommand ?? Environment.GetEnvironmentVariable("MIAOYAN_WHISPER_CMD"));

    public bool OcrConfigured => !string.IsNullOrWhiteSpace(
        _options.OcrCommand ?? Environment.GetEnvironmentVariable("MIAOYAN_OCR_CMD"));

    public bool FfmpegAvailable => FindOnPath("ffmpeg") is not null;

    public static string? FindOnPath(string executable)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, executable);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static SqliteCapabilities ProbeSqlite()
    {
        try
        {
            using var connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "select sqlite_version()";
            var version = command.ExecuteScalar()?.ToString() ?? "unknown";

            var fts5 = false;
            var trigram = false;
            try
            {
                command.CommandText = "create virtual table probe_fts using fts5(body)";
                command.ExecuteNonQuery();
                fts5 = true;
                command.CommandText = "create virtual table probe_trigram using fts5(body, tokenize='trigram')";
                command.ExecuteNonQuery();
                trigram = true;
            }
            catch (SqliteException)
            {
                // FTS5 or the trigram tokenizer is not compiled in.
            }

            return new SqliteCapabilities(version, fts5, trigram);
        }
        catch (Exception ex) when (ex is SqliteException or DllNotFoundException)
        {
            return new SqliteCapabilities("unavailable", false, false);
        }
    }

    private static string? FirstExisting(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}

public sealed record SqliteCapabilities(string Version, bool Fts5, bool Trigram);
