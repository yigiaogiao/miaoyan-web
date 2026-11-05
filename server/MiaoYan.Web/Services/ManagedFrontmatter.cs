using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MiaoYan.Web.Services;

/// <summary>
/// The small YAML header written for notes created or updated through the
/// Papyra-compatible API. MiaoYan notes never get one just for being imported;
/// the header is only added when a machine client asks us to own the metadata.
/// Foreign keys in an existing header are preserved line-by-line.
/// </summary>
public sealed partial record ManagedFrontmatter(
    string? Id,
    string? Title,
    IReadOnlyList<string> Tags,
    string? Color,
    string Kind,
    bool Archived,
    DateTimeOffset? Created,
    DateTimeOffset? Updated,
    string Body,
    string? Header)
{
    private static readonly string[] ManagedKeys =
    [
        "id", "title", "tags", "color", "kind", "archived", "created", "updated",
    ];

    public static ManagedFrontmatter Parse(string content)
    {
        if (!TrySplit(content, out var header, out var body))
        {
            return new ManagedFrontmatter(null, null, [], null, "note", false, null, null, content, null);
        }

        var values = ReadValues(header!);
        return new ManagedFrontmatter(
            Text(values, "id"),
            Text(values, "title"),
            ReadTags(values, header!),
            Text(values, "color"),
            Text(values, "kind") ?? "note",
            Bool(values, "archived"),
            Date(values, "created"),
            Date(values, "updated"),
            body!,
            header);
    }

    /// <summary>
    /// Rewrites the managed keys and keeps every foreign line (and comments) in
    /// place. With no existing header a fresh block is prepended.
    /// </summary>
    public static string Merge(string existingContent, ManagedFrontmatter metadata)
    {
        var header = TrySplit(existingContent, out var foundHeader, out _) ? foundHeader : null;

        var managed = new List<string>
        {
            "id: " + Yaml(metadata.Id),
            "title: " + Yaml(metadata.Title ?? string.Empty),
            "tags: " + InlineList(metadata.Tags),
            "color: " + (metadata.Color is null ? "null" : Yaml(metadata.Color)),
            "kind: " + Yaml(metadata.Kind.Length == 0 ? "note" : metadata.Kind),
            "archived: " + (metadata.Archived ? "true" : "false"),
            "created: " + Yaml((metadata.Created ?? DateTimeOffset.UtcNow).ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)),
            "updated: " + Yaml((metadata.Updated ?? DateTimeOffset.UtcNow).ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)),
        };

        var output = new List<string>();
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var inTagsBlock = false;

        if (header is not null)
        {
            foreach (var line in header.Split('\n'))
            {
                var trimmed = line.Trim();
                if (inTagsBlock && (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("-", StringComparison.Ordinal)))
                {
                    continue;
                }

                inTagsBlock = false;
                var key = KeyOf(trimmed);
                if (key is not null && ManagedKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
                {
                    if (written.Add(key))
                    {
                        output.AddRange(managed.Where(item => item.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase)));
                    }

                    if (key.Equals("tags", StringComparison.OrdinalIgnoreCase)
                        && line.TrimEnd().EndsWith(':'))
                    {
                        inTagsBlock = true;
                    }

                    continue;
                }

                output.Add(line);
            }
        }

        foreach (var key in ManagedKeys)
        {
            if (written.Add(key))
            {
                output.AddRange(managed.Where(item => item.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase)));
            }
        }

        // Keep the block compact but readable: drop leading/trailing blank lines.
        while (output.Count > 0 && output[0].Trim().Length == 0)
        {
            output.RemoveAt(0);
        }

        while (output.Count > 0 && output[^1].Trim().Length == 0)
        {
            output.RemoveAt(output.Count - 1);
        }

        var builder = new StringBuilder();
        builder.Append("---\n");
        builder.AppendJoin('\n', output);
        builder.Append("\n---\n");
        builder.Append(metadata.Body.TrimStart('\r', '\n'));
        return builder.ToString();
    }

    public static bool TrySplit(string content, out string? header, out string? body)
    {
        header = null;
        body = null;
        var lines = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        if (lines.Length == 0 || lines[0].Trim() != "---")
        {
            return false;
        }

        for (var index = 1; index < Math.Min(lines.Length, 500); index++)
        {
            if (lines[index].Trim() != "---")
            {
                continue;
            }

            header = string.Join('\n', lines.Skip(1).Take(index - 1));
            body = string.Join('\n', lines.Skip(index + 1));
            return true;
        }

        return false;
    }

    /// <summary>Returns the managed id if the header has a plausible one.</summary>
    public static string? ReadId(string content)
    {
        if (!TrySplit(content, out var header, out _) || header is null)
        {
            return null;
        }

        var values = ReadValues(header);
        return NormaliseId(Text(values, "id"));
    }

    public static string? NormaliseId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var id = value.Trim();
        if (id.Length is 0 or > 120
            || !char.IsAsciiLetterOrDigit(id[0])
            || id.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('.' or '_' or '-')))
        {
            return null;
        }

        return id;
    }

    private static Dictionary<string, string> ReadValues(string header)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in header.Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Length == 0 || line.TrimStart().StartsWith('#') || char.IsWhiteSpace(line[0]))
            {
                continue;
            }

            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (value.Length > 0 && value[0] is '"' or '\'')
            {
                value = Unquote(value);
            }

            values[key] = value;
        }

        return values;
    }

    private static List<string> ReadTags(Dictionary<string, string> values, string header)
    {
        if (values.TryGetValue("tags", out var inline) && inline.Length > 0)
        {
            var unwrapped = inline.Trim();
            if (unwrapped.StartsWith('[') && unwrapped.EndsWith(']'))
            {
                unwrapped = unwrapped[1..^1];
            }

            return unwrapped
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(Unquote)
                .Where(tag => tag.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        var tags = new List<string>();
        var collecting = false;
        foreach (var raw in header.Split('\n'))
        {
            var line = raw.TrimEnd();
            var trimmed = line.Trim();
            if (trimmed.Equals("tags:", StringComparison.OrdinalIgnoreCase))
            {
                collecting = true;
                continue;
            }

            if (!collecting)
            {
                continue;
            }

            if (!trimmed.StartsWith("- ", StringComparison.Ordinal) && trimmed != "-")
            {
                break;
            }

            var tag = Unquote(trimmed[1..].Trim());
            if (tag.Length > 0)
            {
                tags.Add(tag);
            }
        }

        return tags.Distinct(StringComparer.Ordinal).ToList();
    }

    private static string? KeyOf(string line)
    {
        if (line.Length == 0 || line.StartsWith('#') || char.IsWhiteSpace(line[0]))
        {
            return null;
        }

        var separator = line.IndexOf(':');
        return separator > 0 ? line[..separator].Trim() : null;
    }

    private static string? Text(Dictionary<string, string> values, string key)
        => values.TryGetValue(key, out var value) && value.Length > 0 && value != "null" && value != "~"
            ? Unquote(value)
            : null;

    private static bool Bool(Dictionary<string, string> values, string key)
        => values.TryGetValue(key, out var value)
            && bool.TryParse(Unquote(value), out var parsed)
            && parsed;

    private static DateTimeOffset? Date(Dictionary<string, string> values, string key)
        => values.TryGetValue(key, out var value)
            && DateTimeOffset.TryParse(Unquote(value), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
                ? parsed
                : null;

    private static string Unquote(string value)
    {
        var text = value.Trim();
        if (text.Length >= 2
            && ((text[0] == '"' && text[^1] == '"') || (text[0] == '\'' && text[^1] == '\'')))
        {
            text = text[1..^1];
        }

        return text.Trim();
    }

    private static string Yaml(string? value)
        => "\"" + (value ?? string.Empty)
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal) + "\"";

    private static string InlineList(IReadOnlyList<string> values)
        => "[" + string.Join(", ", values.Select(Yaml)) + "]";
}
