using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;

namespace MiaoYan.Web.Services;

/// <summary>One row of the searchable vault, produced from the in-memory scan.</summary>
public sealed record IndexedNote(
    string Id,
    string Path,
    string Title,
    string ProjectPath,
    string ProjectName,
    long ModifiedTicks,
    long Size,
    string FullPath);

public sealed record SearchHit(string Id, string Title, string Snippet);

/// <summary>
/// Per-vault SQLite FTS5 index. The filesystem stays authoritative: every
/// search stats the scan and only re-reads files whose mtime/size changed, so
/// external edits, renames and deletes are reflected without a separate
/// indexing daemon.
/// </summary>
public sealed class NoteIndex : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly object _gate = new();

    public NoteIndex(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            DefaultTimeout = 5,
        }.ToString());
        _connection.Open();
        Execute("PRAGMA journal_mode=WAL;");
        Execute("PRAGMA busy_timeout=5000;");
        Execute("""
            CREATE TABLE IF NOT EXISTS notes (
                id TEXT PRIMARY KEY,
                path TEXT NOT NULL UNIQUE,
                title TEXT NOT NULL,
                project TEXT NOT NULL,
                project_name TEXT NOT NULL,
                modified INTEGER NOT NULL,
                size INTEGER NOT NULL,
                body TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS notes_project ON notes(project);
            CREATE TABLE IF NOT EXISTS pins (
                id TEXT PRIMARY KEY
            );
            CREATE VIRTUAL TABLE IF NOT EXISTS notes_fts USING fts5(
                title,
                body,
                content='notes',
                content_rowid='rowid',
                tokenize='trigram'
            );
            CREATE TRIGGER IF NOT EXISTS notes_ai AFTER INSERT ON notes BEGIN
                INSERT INTO notes_fts(rowid, title, body) VALUES (new.rowid, new.title, new.body);
            END;
            CREATE TRIGGER IF NOT EXISTS notes_ad AFTER DELETE ON notes BEGIN
                INSERT INTO notes_fts(notes_fts, rowid, title, body)
                VALUES ('delete', old.rowid, old.title, old.body);
            END;
            CREATE TRIGGER IF NOT EXISTS notes_au AFTER UPDATE ON notes BEGIN
                INSERT INTO notes_fts(notes_fts, rowid, title, body)
                VALUES ('delete', old.rowid, old.title, old.body);
                INSERT INTO notes_fts(rowid, title, body) VALUES (new.rowid, new.title, new.body);
            END;
            """);
    }

    public IReadOnlyList<SearchHit> RefreshAndSearch(
        IReadOnlyList<IndexedNote> notes,
        string query,
        string? projectPath,
        int limit)
    {
        lock (_gate)
        {
            Refresh(notes);
            return Search(query, projectPath, limit);
        }
    }

    private void Refresh(IReadOnlyList<IndexedNote> notes)
    {
        var existing = new Dictionary<string, (long Modified, long Size)>(StringComparer.Ordinal);
        using (var command = _connection.CreateCommand())
        {
            command.CommandText = "SELECT id, modified, size FROM notes;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                existing[reader.GetString(0)] = (reader.GetInt64(1), reader.GetInt64(2));
            }
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        using var transaction = _connection.BeginTransaction();
        foreach (var note in notes)
        {
            seen.Add(note.Id);
            if (existing.TryGetValue(note.Id, out var current)
                && current.Modified == note.ModifiedTicks
                && current.Size == note.Size)
            {
                continue;
            }

            var body = TryRead(note.FullPath);
            if (body is null)
            {
                continue;
            }

            using var command = _connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO notes (id, path, title, project, project_name, modified, size, body)
                VALUES ($id, $path, $title, $project, $projectName, $modified, $size, $body)
                ON CONFLICT(id) DO UPDATE SET
                    path = excluded.path,
                    title = excluded.title,
                    project = excluded.project,
                    project_name = excluded.project_name,
                    modified = excluded.modified,
                    size = excluded.size,
                    body = excluded.body;
                """;
            command.Parameters.AddWithValue("$id", note.Id);
            command.Parameters.AddWithValue("$path", note.Path);
            command.Parameters.AddWithValue("$title", note.Title);
            command.Parameters.AddWithValue("$project", note.ProjectPath);
            command.Parameters.AddWithValue("$projectName", note.ProjectName);
            command.Parameters.AddWithValue("$modified", note.ModifiedTicks);
            command.Parameters.AddWithValue("$size", note.Size);
            command.Parameters.AddWithValue("$body", body);
            command.ExecuteNonQuery();
        }

        var stale = existing.Keys.Where(id => !seen.Contains(id)).ToList();
        foreach (var id in stale)
        {
            using var command = _connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM notes WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private IReadOnlyList<SearchHit> Search(string query, string? projectPath, int limit)
    {
        var term = query.Trim();
        if (term.Length == 0)
        {
            return [];
        }

        limit = Math.Clamp(limit, 1, 500);
        // A trigram tokenizer cannot match one- or two-character queries.
        // Fall back to a bounded LIKE scan; still executed in SQLite, never in
        // the browser, so large vaults are not shipped to the client.
        return term.Length < 3
            ? SearchLike(term, projectPath, limit)
            : SearchFts(term, projectPath, limit);
    }

    private IReadOnlyList<SearchHit> SearchFts(string term, string? projectPath, int limit)
    {
        var match = string.Join(
            " AND ",
            term.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part => "\"" + part.Replace("\"", "\"\"") + "\""));
        if (match.Length == 0)
        {
            return [];
        }

        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT n.id, n.title, snippet(notes_fts, -1, '', '', '…', 14)
            FROM notes_fts
            JOIN notes n ON n.rowid = notes_fts.rowid
            WHERE notes_fts MATCH $match
              AND ($project IS NULL OR n.project = $project)
            ORDER BY
                CASE
                    WHEN n.title = $term COLLATE NOCASE THEN 0
                    WHEN n.title LIKE $titlePattern ESCAPE '\' THEN 1
                    ELSE 2
                END,
                notes_fts.rank
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$match", match);
        command.Parameters.AddWithValue("$term", term);
        command.Parameters.AddWithValue("$titlePattern", LikePattern(term));
        command.Parameters.AddWithValue("$project", (object?)projectPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$limit", limit);
        return ReadHits(command);
    }

    private IReadOnlyList<SearchHit> SearchLike(string term, string? projectPath, int limit)
    {
        var pattern = LikePattern(term);

        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT id, title, body
            FROM notes
            WHERE (title LIKE $pattern ESCAPE '\' OR body LIKE $pattern ESCAPE '\')
              AND ($project IS NULL OR project = $project)
            ORDER BY
                CASE
                    WHEN title = $term COLLATE NOCASE THEN 0
                    WHEN title LIKE $pattern ESCAPE '\' THEN 1
                    ELSE 2
                END,
                modified DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$pattern", pattern);
        command.Parameters.AddWithValue("$term", term);
        command.Parameters.AddWithValue("$project", (object?)projectPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$limit", limit);

        var hits = new List<SearchHit>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var body = reader.GetString(2);
            hits.Add(new SearchHit(reader.GetString(0), reader.GetString(1), MakeSnippet(body, term)));
        }

        return hits;
    }

    private static IReadOnlyList<SearchHit> ReadHits(SqliteCommand command)
    {
        var hits = new List<SearchHit>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var snippet = reader.IsDBNull(2) ? string.Empty : Normalise(reader.GetString(2));
            hits.Add(new SearchHit(reader.GetString(0), reader.GetString(1), snippet));
        }

        return hits;
    }

    private static string LikePattern(string term)
    {
        return "%" + term
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal) + "%";
    }

    private static string MakeSnippet(string body, string term)
    {
        var index = body.IndexOf(term, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return Normalise(body.Length <= 160 ? body : body[..160]);
        }

        var start = Math.Max(0, index - 60);
        var end = Math.Min(body.Length, index + term.Length + 100);
        var prefix = start > 0 ? "…" : string.Empty;
        var suffix = end < body.Length ? "…" : string.Empty;
        return prefix + Normalise(body[start..end]) + suffix;
    }

    private static string Normalise(string value)
        => string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string? TryRead(string path)
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public IReadOnlySet<string> PinnedIds()
    {
        lock (_gate)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT id FROM pins;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                ids.Add(reader.GetString(0));
            }

            return ids;
        }
    }

    public void SetPinned(string id, bool pinned)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = pinned
                ? "INSERT OR IGNORE INTO pins (id) VALUES ($id);"
                : "DELETE FROM pins WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        }
    }

    public void MovePin(string fromId, string toId)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                INSERT OR IGNORE INTO pins (id)
                SELECT $toId WHERE EXISTS (SELECT 1 FROM pins WHERE id = $fromId);
                DELETE FROM pins WHERE id = $fromId;
                """;
            command.Parameters.AddWithValue("$fromId", fromId);
            command.Parameters.AddWithValue("$toId", toId);
            command.ExecuteNonQuery();
        }
    }

    private void Execute(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public void Dispose() => _connection.Dispose();
}

/// <summary>Owns one <see cref="NoteIndex"/> per LazyCat user / vault.</summary>
public sealed class NoteIndexService : IDisposable
{
    private readonly ConcurrentDictionary<string, NoteIndex> _indexes = new(StringComparer.Ordinal);

    public NoteIndex For(VaultRuntime runtime)
        => _indexes.GetOrAdd(runtime.Key, _ => new NoteIndex(runtime.Paths.DatabasePath));

    public void Dispose()
    {
        foreach (var index in _indexes.Values)
        {
            index.Dispose();
        }

        _indexes.Clear();
    }
}
