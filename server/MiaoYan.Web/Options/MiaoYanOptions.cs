namespace MiaoYan.Web.Options;

/// <summary>
/// Resolved filesystem locations for one user's library. The vault is the
/// user's folder; the state directory holds everything that can be rebuilt
/// (index, versions, cache).
/// </summary>
public sealed class MiaoYanPaths
{
    public required string VaultRoot { get; init; }
    public required string StateRoot { get; init; }

    public string DatabasePath => Path.Combine(StateRoot, "index.db");
    public string VersionsRoot => Path.Combine(StateRoot, "versions");
    public string CacheRoot => Path.Combine(StateRoot, "cache");
    public string TempRoot => Path.Combine(StateRoot, "tmp");
    public string ConflictRoot => Path.Combine(VaultRoot, ".miaoyan-conflicts");
    public string TrashRoot => Path.Combine(VaultRoot, ".miaoyan", "trash");

    public void EnsureCreated()
    {
        Directory.CreateDirectory(VaultRoot);
        Directory.CreateDirectory(StateRoot);
        Directory.CreateDirectory(VersionsRoot);
        Directory.CreateDirectory(CacheRoot);
        Directory.CreateDirectory(TempRoot);
        Directory.CreateDirectory(ConflictRoot);
        Directory.CreateDirectory(TrashRoot);
    }
}

public sealed class MiaoYanOptions
{
    public string? Library { get; set; }
    public string? StateDir { get; set; }
    public string? DefaultUid { get; set; }
    public string? WebPassword { get; set; }
    public string? ApiKey { get; set; }
    public long MaxUploadBytes { get; set; } = 100L * 1024 * 1024;
    public string? PlantUmlJar { get; set; }
    public string? WhisperCommand { get; set; }
    public string? OcrCommand { get; set; }
    public bool TrustProxy { get; set; } = true;
}
