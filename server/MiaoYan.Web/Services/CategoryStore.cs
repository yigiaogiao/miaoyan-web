using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using MiaoYan.Web.Api;

namespace MiaoYan.Web.Services;

/// <summary>
/// Category registry used by Ingest clients. Tags themselves live in note
/// frontmatter; this file only remembers a colour for tags that have no note
/// yet. It is state, not data: deleting it loses colours but no notes.
/// </summary>
public sealed class CategoryStore
{
    private sealed record CategoryRecord(string Name, string? Color, DateTimeOffset CreatedAt);

    private readonly ConcurrentDictionary<string, object> _gates = new(StringComparer.Ordinal);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public IReadOnlyList<IngestCategoryDto> List(
        VaultRuntime runtime,
        IReadOnlyDictionary<string, int> counts)
    {
        var registry = Load(runtime);
        var result = new Dictionary<string, IngestCategoryDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in registry.Values)
        {
            result[item.Name] = new IngestCategoryDto(item.Name, item.Color, counts.GetValueOrDefault(item.Name));
        }

        foreach (var (name, count) in counts)
        {
            if (!result.TryGetValue(name, out var existing))
            {
                result[name] = new IngestCategoryDto(name, null, count);
            }
            else if (existing.Count != count)
            {
                result[name] = existing with { Count = count };
            }
        }

        return result.Values
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IngestCategoryDto Save(VaultRuntime runtime, IngestCategoryRequest request, int count)
    {
        var name = (request.Name ?? string.Empty).Trim();
        if (name.Length is 0 or > 64 || name.Any(char.IsControl))
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "invalid-category", "A category name is required.");
        }

        var color = request.Color?.Trim();
        if (color is { Length: > 32 } || (color is not null && color.Any(char.IsControl)))
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "invalid-color", "Invalid category colour.");
        }

        lock (Gate(runtime))
        {
            var registry = Load(runtime);
            registry[name] = new CategoryRecord(name, string.IsNullOrEmpty(color) ? null : color, DateTimeOffset.UtcNow);
            Write(runtime, registry);
        }

        return new IngestCategoryDto(name, string.IsNullOrEmpty(color) ? null : color, count);
    }

    public void Delete(VaultRuntime runtime, string name)
    {
        lock (Gate(runtime))
        {
            var registry = Load(runtime);
            if (registry.Remove(name))
            {
                Write(runtime, registry);
            }
        }
    }

    private object Gate(VaultRuntime runtime)
        => _gates.GetOrAdd(runtime.Key, _ => new object());

    private static string PathFor(VaultRuntime runtime)
        => Path.Combine(runtime.Paths.StateRoot, "categories.json");

    private static Dictionary<string, CategoryRecord> Load(VaultRuntime runtime)
    {
        var path = PathFor(runtime);
        if (!File.Exists(path))
        {
            return new Dictionary<string, CategoryRecord>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var records = JsonSerializer.Deserialize<List<CategoryRecord>>(File.ReadAllText(path), Json) ?? [];
            return records
                .GroupBy(record => record.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new Dictionary<string, CategoryRecord>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void Write(VaultRuntime runtime, Dictionary<string, CategoryRecord> registry)
    {
        Directory.CreateDirectory(runtime.Paths.StateRoot);
        var path = PathFor(runtime);
        var temporary = path + ".tmp";
        var json = JsonSerializer.Serialize(registry.Values.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase), Json);
        File.WriteAllText(temporary, json, new UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
    }
}
