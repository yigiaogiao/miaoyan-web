using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MiaoYan.Web.Api;
using MiaoYan.Web.Options;
using Microsoft.AspNetCore.StaticFiles;

namespace MiaoYan.Web.Services;

public sealed record MediaFile(string FullPath, string FileName);

/// <summary>
/// Root-level <c>i/</c> and <c>files/</c> media used by the ingest-compatible
/// API. Uploads are content-addressed, so re-publishing an article reuses the
/// same file instead of accumulating copies. Note-relative assets created by
/// the editor keep working through the fallback in VaultService.ResolveAsset.
/// </summary>
public sealed partial class MediaLibrary
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".avif", ".bmp", ".tif", ".tiff",
        ".heic", ".heif", ".svg",
    };

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".m4a", ".mp3", ".wav", ".flac", ".ogg", ".opus", ".aac", ".weba", ".oga", ".weba",
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".webm", ".mov", ".m4v", ".mkv", ".ogv",
    };

    private static readonly HashSet<string> FileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".zip", ".txt", ".md", ".json", ".csv", ".doc", ".docx", ".xls", ".xlsx",
        ".ppt", ".pptx", ".epub",
    };

    private static readonly FileExtensionContentTypeProvider ContentTypes = CreateContentTypes();

    private readonly MiaoYanOptions _options;

    [GeneratedRegex(@"/api/media/([^\s<>\(\)\[\]""']+)")]
    private static partial Regex MediaUrlRegex();

    [GeneratedRegex(@"!\[\[([^\]\r\n]+)\]\]")]
    private static partial Regex EmbedRegex();

    public MediaLibrary(MiaoYanOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// Rewrites the placeholders miaoyan-ingest emits into normal MiaoYan
    /// relative media. Audio is an image-style embed because the preview turns
    /// known audio extensions into an audio element.
    /// </summary>
    public static string NormalizeBody(string body)
    {
        var text = MediaUrlRegex().Replace(body, match =>
        {
            var name = Uri.UnescapeDataString(match.Groups[1].Value);
            if (name.Length == 0 || name.Contains('/') || name.Contains('\\'))
            {
                return match.Value;
            }

            return SubdirectoryFor(name) + "/" + Uri.EscapeDataString(name);
        });

        return EmbedRegex().Replace(text, match =>
        {
            var raw = match.Groups[1].Value;
            var pipe = raw.IndexOf('|');
            var name = (pipe >= 0 ? raw[..pipe] : raw).Trim();
            if (name.Length == 0 || name.Contains('/') || name.Contains('\\'))
            {
                return match.Value;
            }

            return "![](" + SubdirectoryFor(name) + "/" + Uri.EscapeDataString(name) + ")";
        });
    }

    public async Task<IngestMediaDto> SaveAsync(
        VaultRuntime runtime,
        IFormFile file,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        if (file.Length <= 0)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "empty-file", "The uploaded file is empty.");
        }

        if (file.Length > maxBytes)
        {
            throw new ApiException(StatusCodes.Status413PayloadTooLarge, "file-too-large", "The uploaded file is too large.");
        }

        var original = Path.GetFileName(file.FileName);
        var extension = Path.GetExtension(original);
        if (extension.Length > 16
            || extension.Any(char.IsControl)
            || !IsAllowedExtension(extension))
        {
            throw new ApiException(StatusCodes.Status415UnsupportedMediaType, "unsupported-media", "This file type is not allowed.");
        }

        extension = extension.ToLowerInvariant();
        var kind = KindFor(extension);
        var subdirectory = kind == "image" ? "i" : "files";
        var stem = TitleHelper.ToAssetStem(Path.GetFileNameWithoutExtension(original));
        if (stem.Length == 0)
        {
            stem = kind;
        }

        var temporary = Path.Combine(runtime.Paths.TempRoot, $"media-{Guid.NewGuid():N}");
        Directory.CreateDirectory(runtime.Paths.TempRoot);
        string hash;
        long size = 0;
        try
        {
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var input = file.OpenReadStream())
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    size += read;
                    if (size > maxBytes)
                    {
                        throw new ApiException(StatusCodes.Status413PayloadTooLarge, "file-too-large", "The uploaded file is too large.");
                    }

                    hasher.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }

            if (size == 0)
            {
                throw new ApiException(StatusCodes.Status400BadRequest, "empty-file", "The uploaded file is empty.");
            }

            hash = Convert.ToHexString(hasher.GetHashAndReset())[..12].ToLowerInvariant();
            var existing = FindByHash(runtime, hash, extension);
            if (existing is not null)
            {
                return Describe(existing.FullPath, Path.GetFileName(existing.FullPath), kind, hash);
            }

            var directory = Path.Combine(runtime.Paths.VaultRoot, subdirectory);
            Directory.CreateDirectory(directory);
            var name = $"{stem}-{hash}{extension}";
            var target = VaultPath.Combine(runtime.Paths.VaultRoot, $"{subdirectory}/{name}");
            File.Move(temporary, target);
            return Describe(target, name, kind, hash);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    public MediaFile? Resolve(VaultRuntime runtime, string filename)
    {
        if (!IsSafeFileName(filename))
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "invalid-media", "Invalid media file name.");
        }

        var root = runtime.Paths.VaultRoot;
        foreach (var subdirectory in new[] { "i", "files" })
        {
            var candidate = VaultPath.Combine(root, $"{subdirectory}/{filename}");
            if (File.Exists(candidate))
            {
                return new MediaFile(candidate, filename);
            }
        }

        // Imported MiaoYan libraries keep attachments next to their note. A
        // bare media name resolves to the first project copy, which is what the
        // ingest resolver expects after a note was moved out of its project.
        try
        {
            foreach (var project in Directory.EnumerateDirectories(root))
            {
                var name = Path.GetFileName(project);
                if (name.StartsWith('.')
                    || name.Equals("i", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("files", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var projectRelative = VaultPath.Relative(root, project);
                foreach (var subdirectory in new[] { "i", "files" })
                {
                    var candidate = VaultPath.Combine(root, $"{projectRelative}/{subdirectory}/{filename}");
                    if (File.Exists(candidate))
                    {
                        return new MediaFile(candidate, filename);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Treat an unreadable project folder as "not found".
        }

        return null;
    }

    public static string ContentTypeFor(string filename)
        => ContentTypes.TryGetContentType(filename, out var contentType)
            ? contentType
            : "application/octet-stream";

    public static string SubdirectoryFor(string filename)
        => KindFor(Path.GetExtension(filename)) == "image" ? "i" : "files";

    public static string KindFor(string extension)
    {
        if (ImageExtensions.Contains(extension))
        {
            return "image";
        }

        if (AudioExtensions.Contains(extension))
        {
            return "audio";
        }

        if (VideoExtensions.Contains(extension))
        {
            return "video";
        }

        return "file";
    }

    private static bool IsAllowedExtension(string extension)
        => ImageExtensions.Contains(extension)
            || AudioExtensions.Contains(extension)
            || VideoExtensions.Contains(extension)
            || FileExtensions.Contains(extension);

    private static bool IsSafeFileName(string filename)
        => filename.Length is > 0 and <= 255
            && filename is not ("." or "..")
            && !filename.Contains('/')
            && !filename.Contains('\\')
            && !filename.Any(char.IsControl);

    private static MediaFile? FindByHash(VaultRuntime runtime, string hash, string extension)
    {
        foreach (var subdirectory in new[] { "i", "files" })
        {
            if (!Directory.Exists(Path.Combine(runtime.Paths.VaultRoot, subdirectory)))
            {
                continue;
            }

            var directory = VaultPath.Combine(runtime.Paths.VaultRoot, subdirectory);

            var match = Directory.EnumerateFiles(directory, "*-" + hash + extension)
                .FirstOrDefault();
            if (match is not null)
            {
                return new MediaFile(match, Path.GetFileName(match));
            }
        }

        return null;
    }

    private static IngestMediaDto Describe(string fullPath, string name, string kind, string hash)
    {
        var info = new FileInfo(fullPath);
        var url = "/api/media/" + Uri.EscapeDataString(name);
        // Names are sanitised by ToAssetStem, so the Markdown stays readable
        // and still parses without percent-encoded whitespace.
        var markdown = $"![]({SubdirectoryFor(name)}/{name})";
        return new IngestMediaDto(
            name,
            kind,
            ContentTypeFor(name),
            info.Length,
            hash,
            url,
            markdown);
    }

    private static FileExtensionContentTypeProvider CreateContentTypes()
    {
        var provider = new FileExtensionContentTypeProvider();
        provider.Mappings[".m4a"] = "audio/mp4";
        provider.Mappings[".flac"] = "audio/flac";
        provider.Mappings[".webm"] = "video/webm";
        provider.Mappings[".weba"] = "audio/webm";
        provider.Mappings[".oga"] = "audio/ogg";
        provider.Mappings[".opus"] = "audio/ogg";
        provider.Mappings[".avif"] = "image/avif";
        provider.Mappings[".heic"] = "image/heic";
        provider.Mappings[".heif"] = "image/heif";
        provider.Mappings[".mkv"] = "video/x-matroska";
        return provider;
    }
}
