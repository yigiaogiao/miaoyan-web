using System.Text.RegularExpressions;

namespace MiaoYan.Web.Services;

/// <summary>
/// MiaoYan keeps the title in the file name. A UUID file name is shown as
/// "Untitled Note", and a slash in the title is stored as a colon on disk so
/// Finder/macOS stays happy. We reproduce that mapping exactly.
/// </summary>
public static partial class TitleHelper
{
    public const string Untitled = "Untitled Note";

    [GeneratedRegex(@"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")]
    private static partial Regex UuidRegex();

    [GeneratedRegex(@"[\\*?""<>|\x00-\x1F]")]
    private static partial Regex InvalidFileNameChars();

    // Markdown image/link destinations and MiaoYan's attachment scanner end at
    // whitespace, quotes and closing parentheses. Keep those characters out of
    // attachment names so a generated link always parses as an image.
    [GeneratedRegex(@"[\s:()\[\]{}'""#?%]+")]
    private static partial Regex UnsafeAssetChars();

    public static bool IsUuid(string value) => UuidRegex().IsMatch(value);

    public static string TitleFromFile(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        if (IsUuid(stem))
        {
            return Untitled;
        }

        var title = stem.Replace(':', '/').Trim();
        return title.Length == 0 ? Untitled : title;
    }

    /// <summary>
    /// Convert a user title to the on-disk stem. "A/B" becomes "A:B", and a
    /// literal colon becomes a dash, matching NameHelper in MiaoYan.
    /// </summary>
    public static string ToFileStem(string title)
    {
        var value = title.Trim();
        value = value.Replace(':', '-').Replace('/', ':');
        value = InvalidFileNameChars().Replace(value, "-");
        value = value.Trim(' ', '.');
        return value;
    }

    /// <summary>
    /// Attachment names must survive both Markdown and the attachment scanner.
    /// Note titles keep spaces (MiaoYan does the same), but an image called
    /// "image 1.png" would produce <c>![](i/image 1.png)</c>, which is plain
    /// text in CommonMark. Collapse unsafe characters to a single dash.
    /// </summary>
    public static string ToAssetStem(string name)
    {
        var value = ToFileStem(name);
        value = UnsafeAssetChars().Replace(value, "-");
        value = Regex.Replace(value, "-{2,}", "-");
        return value.Trim('-', ' ', '.');
    }

    public static string ToProjectName(string name)
    {
        var value = name.Trim();
        value = InvalidFileNameChars().Replace(value, "-");
        value = value.Replace(':', '-').Replace('/', '-');
        return value.Trim(' ', '.');
    }

    /// <summary>MiaoYan-style unique file name: "Name 2.md", "Name 3.md".</summary>
    public static string UniqueFileStem(string directory, string stem, string extension)
    {
        var candidate = stem;
        var postfix = 0;
        while (File.Exists(Path.Combine(directory, candidate + extension)))
        {
            var match = Regex.Match(candidate, @"^(.+)\s(\d+)$");
            if (match.Success)
            {
                candidate = match.Groups[1].Value;
                if (int.TryParse(match.Groups[2].Value, out var parsed))
                {
                    postfix = parsed;
                }
            }

            candidate = $"{candidate} {++postfix}";
        }

        return candidate;
    }

    /// <summary>
    /// Attachment names must stay Markdown-safe even when an upload gets a
    /// numeric suffix. <see cref="UniqueFileStem"/> uses a space ("image 1"),
    /// which CommonMark turns back into plain text, so sanitise and retry
    /// with dashes instead.
    /// </summary>
    public static string UniqueAssetStem(string directory, string stem, string extension)
    {
        var baseStem = ToAssetStem(stem);
        if (baseStem.Length == 0)
        {
            baseStem = "asset";
        }

        var candidate = baseStem;
        var postfix = 0;
        while (File.Exists(Path.Combine(directory, candidate + extension)))
        {
            candidate = $"{baseStem}-{++postfix}";
        }

        return candidate;
    }

    public static string UniqueDirectory(string parent, string name)
    {
        var candidate = name;
        var index = 1;
        while (Directory.Exists(Path.Combine(parent, candidate)) || File.Exists(Path.Combine(parent, candidate)))
        {
            candidate = $"{name} {++index}";
        }

        return candidate;
    }
}
