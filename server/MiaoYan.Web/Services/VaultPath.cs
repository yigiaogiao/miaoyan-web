namespace MiaoYan.Web.Services;

/// <summary>
/// Every path that comes from a request or from note content must stay inside
/// the vault. Symlinks are resolved component by component so a link inside the
/// vault cannot read the host filesystem.
/// </summary>
public static class VaultPath
{
    public static string NormaliseRelative(string relative)
    {
        var value = relative.Replace('\\', '/').Trim('/');
        if (value.Length == 0 || value is "." or "..")
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "invalid-path", "Empty or invalid path.");
        }

        var segments = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment is "." or ".." || segment.Contains('\0')))
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "invalid-path", "Path traversal is not allowed.");
        }

        return string.Join('/', segments);
    }

    public static string Combine(string root, string relative)
    {
        var relativeNormalised = NormaliseRelative(relative);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(fullRoot, relativeNormalised.Replace('/', Path.DirectorySeparatorChar)));

        if (!IsInside(fullRoot, full))
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "invalid-path", "Path escapes the vault.");
        }

        EnsureSymlinksInside(fullRoot, full);
        return full;
    }

    public static string Relative(string root, string fullPath)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(fullPath);
        if (!IsInside(fullRoot, full))
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "invalid-path", "Path escapes the vault.");
        }

        return Path.GetRelativePath(fullRoot, full).Replace('\\', '/');
    }

    public static bool IsInside(string root, string candidate)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        var fullCandidate = Path.GetFullPath(candidate);
        return fullCandidate.Equals(fullRoot, StringComparison.Ordinal)
            || fullCandidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static void EnsureSymlinksInside(string root, string full)
    {
        var relative = Path.GetRelativePath(root, full);
        var current = root;
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (!info.Exists)
            {
                continue;
            }

            var target = info.ResolveLinkTarget(returnFinalTarget: true);
            if (target is null)
            {
                continue;
            }

            if (!IsInside(root, target.FullName))
            {
                throw new ApiException(
                    StatusCodes.Status400BadRequest,
                    "invalid-path",
                    "A symlink points outside the vault.");
            }
        }
    }
}

public sealed class ApiException(int statusCode, string code, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}
