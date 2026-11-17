using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MiaoYan.Web.Api;
using MiaoYan.Web.Options;

namespace MiaoYan.Web.Services;

public sealed record SaveResult(NoteDto? Note, ConflictDto? Conflict);
public sealed record TrashRestoreResult(string Kind, string? NoteId, string? ProjectId);
public sealed record AssetFile(string FullPath, string FileName);
public sealed record UnusedAssetsResult(int ScannedNotes, IReadOnlyList<string> Files);
public sealed record CleanUnusedAssetsResult(IReadOnlyList<string> Removed, IReadOnlyList<string> Failed);

/// <summary>
/// Filesystem-backed library operations. The scan is held in memory and
/// invalidated by writes and the watcher; SQLite arrives in M2 without changing
/// the file layout.
/// </summary>
public sealed partial class VaultService
{
    public const string AllProjectId = "all";
    public const string RootProjectId = "root";
    private const string RootProjectName = "未分类";

    private readonly VaultEventHub _hub;
    private readonly NoteIndexService _index;
    private readonly ILogger<VaultService> _logger;
    private readonly ConcurrentDictionary<string, VaultCache> _caches = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    // Same shape as MiaoYan's Note.attachmentPathRegex: an i/ or files/ path,
    // optionally preceded by ./ ../ or /, ending at whitespace, a quote or a
    // closing parenthesis.
    private static readonly Regex AttachmentPathRegex = new(
        @"(?:(?:\.\./|\./|/)*)(?:i|files)/[^)\s'""]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Notes written before attachment names were sanitised can contain
    // ![](i/image 1.png). AttachmentPathRegex stops at the space, which would
    // make cleanup treat the image as unused, so also read the full Markdown
    // destination from image/link syntax.
    private static readonly Regex MarkdownDestinationRegex = new(
        @"!?\[[^\]]*\]\(([^)\r\n]*)\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions TrashJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    // Lazy `.*?` anchored at line starts is linear. The previous
    // `(?:.*\r?\n)*?` with Singleline backtracked catastrophically on a
    // truncated 2 KB excerpt and, when it did match, could swallow most of the
    // body; a frontmatter note larger than ~1 KB therefore hung every vault
    // scan.
    [GeneratedRegex(@"\A---\r?\n.*?^---\r?\n", RegexOptions.Singleline | RegexOptions.Multiline)]
    private static partial Regex FrontmatterRegex();

    [GeneratedRegex(@"!\[[^\]]*\]\([^)]*\)")]
    private static partial Regex ImageRegex();

    [GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlRegex();

    [GeneratedRegex(@"[#>*_`~]+")]
    private static partial Regex MarkerRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"`[^`\r\n]*`")]
    private static partial Regex InlineCodeRegex();

    [GeneratedRegex(@"\[\[([^\[\]\r\n]{1,200})\]\]")]
    private static partial Regex WikiLinkRegex();

    public VaultService(VaultEventHub hub, NoteIndexService index, ILogger<VaultService> logger)
    {
        _hub = hub;
        _index = index;
        _logger = logger;
    }

    public static string NoteIdFor(string relativePath)
        => "n" + HashId("note:" + relativePath.Replace('\\', '/'));

    public static string ProjectIdFor(string projectPath)
        => projectPath.Length == 0 ? RootProjectId : "p" + HashId("project:" + projectPath);

    private static string HashId(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..12].ToLowerInvariant();

    public void Invalidate(string vaultKey)
    {
        if (_caches.TryGetValue(vaultKey, out var cache))
        {
            lock (cache.Gate)
            {
                cache.Dirty = true;
            }
        }
    }

    public VaultInfoDto GetInfo(VaultRuntime runtime)
    {
        var snapshot = Snapshot(runtime);
        return new VaultInfoDto(
            Path.GetFileName(runtime.Paths.VaultRoot.TrimEnd(Path.DirectorySeparatorChar)),
            runtime.Paths.VaultRoot,
            snapshot.Notes.Count,
            snapshot.Projects.Count(project => project.Kind == "folder"));
    }

    public IReadOnlyList<ProjectDto> ListProjects(VaultRuntime runtime)
        => Snapshot(runtime).Projects;

    public IReadOnlyList<NoteSummaryDto> ListNotes(VaultRuntime runtime, string? projectId, string? query)
    {
        var snapshot = Snapshot(runtime);
        IEnumerable<VaultNote> notes = snapshot.Notes;
        var pinnedIds = _index.For(runtime).PinnedIds();

        if (!string.IsNullOrWhiteSpace(projectId) && projectId != AllProjectId)
        {
            if (projectId == RootProjectId)
            {
                notes = notes.Where(note => note.ProjectPath.Length == 0);
            }
            else
            {
                var path = FindProjectPath(snapshot, projectId)
                    ?? throw new ApiException(StatusCodes.Status404NotFound, "project-not-found", "Project not found.");
                notes = notes.Where(note => note.ProjectPath == path);
            }
        }

        if (!string.IsNullOrWhiteSpace(query) && query.Trim().Length > 0)
        {
            var hits = SearchRecords(runtime, snapshot, query.Trim(), projectId, 500);
            return hits
                .Select(hit => snapshot.ById.TryGetValue(hit.Id, out var note)
                    ? ToSummary(
                        note,
                        string.IsNullOrEmpty(hit.Snippet) ? note.Excerpt : hit.Snippet,
                        pinnedIds.Contains(note.Id))
                    : null)
                .Where(summary => summary is not null)
                .Select(summary => summary!)
                .ToList();
        }

        return notes
            .OrderByDescending(note => pinnedIds.Contains(note.Id))
            .ThenByDescending(note => note.ModifiedAt)
            .Select(note => ToSummary(note, note.Excerpt, pinnedIds.Contains(note.Id)))
            .ToList();
    }

    /// <summary>
    /// Ingest's list endpoint returns full bodies. Keeping this separate from
    /// the native summary keeps the SPA responsive on a large library.
    /// </summary>
    public IReadOnlyList<NoteSummaryDto> ListIngestNotes(VaultRuntime runtime, string? projectId, string? query)
    {
        var summaries = ListNotes(runtime, projectId, query);
        var root = runtime.Paths.VaultRoot;
        var result = new List<NoteSummaryDto>(summaries.Count);
        foreach (var summary in summaries)
        {
            var content = TryReadText(Path.Combine(root, summary.Path.Replace('/', Path.DirectorySeparatorChar)));
            if (content is null)
            {
                result.Add(summary);
                continue;
            }

            var metadata = ManagedFrontmatter.Parse(content);
            result.Add(summary with
            {
                Body = metadata.Body,
                Tags = metadata.Tags,
                Color = metadata.Color,
                Kind = metadata.Kind,
                Archived = metadata.Archived,
                Updated = summary.ModifiedAt,
                ManagedId = metadata.Id ?? summary.ManagedId,
            });
        }

        return result;
    }

    public IReadOnlyList<SearchHitDto> SearchNotes(
        VaultRuntime runtime,
        string? query,
        string? projectId,
        int limit = 50)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var snapshot = Snapshot(runtime);
        var hits = SearchRecords(runtime, snapshot, query.Trim(), projectId, limit);
        return hits
            .Select((hit, index) => snapshot.ById.TryGetValue(hit.Id, out var note)
                ? new SearchHitDto(
                    note.Id,
                    note.Title,
                    note.ProjectId,
                    note.ProjectName,
                    string.IsNullOrEmpty(hit.Snippet) ? note.Excerpt : hit.Snippet,
                    note.ModifiedAt,
                    note.Size,
                    note.IsUntitled,
                    Math.Max(0.05, 1 - index * 0.01))
                : null)
            .Where(hit => hit is not null)
            .Select(hit => hit!)
            .ToList();
    }

    private IReadOnlyList<SearchHit> SearchRecords(
        VaultRuntime runtime,
        VaultSnapshot snapshot,
        string query,
        string? projectId,
        int limit)
    {
        string? projectPath;
        if (string.IsNullOrWhiteSpace(projectId) || projectId == AllProjectId)
        {
            projectPath = null;
        }
        else if (projectId == RootProjectId)
        {
            projectPath = string.Empty;
        }
        else
        {
            projectPath = FindProjectPath(snapshot, projectId)
                ?? throw new ApiException(StatusCodes.Status404NotFound, "project-not-found", "Project not found.");
        }

        var indexed = snapshot.Notes
            .Select(note => new IndexedNote(
                note.Id,
                note.RelPath,
                note.Title,
                note.ProjectPath,
                note.ProjectName,
                note.ModifiedAt.UtcTicks,
                note.Size,
                note.FullPath))
            .ToList();

        return _index.For(runtime).RefreshAndSearch(indexed, query, projectPath, limit);
    }

    /// <summary>
    /// Title index plus unresolved references, consumed by the preview to style
    /// [[links]] and by the "create note" action. Exact titles win; matching is
    /// case-insensitive only as a fallback, like MiaoYan's link index.
    /// </summary>
    public LinkIndexDto GetLinkIndex(VaultRuntime runtime)
    {
        var snapshot = Snapshot(runtime);
        var targets = snapshot.Notes
            .GroupBy(note => note.Title, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(note => note.RelPath, StringComparer.Ordinal).First())
            .OrderBy(note => note.Title, StringComparer.OrdinalIgnoreCase)
            .Select(note => new LinkTargetDto(note.Title, note.Id, note.ProjectName, note.RelPath))
            .ToList();

        var known = targets.Select(target => target.Title).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unresolved = new Dictionary<string, (string Title, int Count)>(StringComparer.OrdinalIgnoreCase);
        foreach (var note in snapshot.Notes)
        {
            var content = TryReadText(note.FullPath);
            if (content is null)
            {
                continue;
            }

            foreach (var link in ExtractWikiLinks(content))
            {
                if (known.Contains(link.Title))
                {
                    continue;
                }

                unresolved[link.Title] = unresolved.TryGetValue(link.Title, out var current)
                    ? (current.Title, current.Count + 1)
                    : (link.Title, 1);
            }
        }

        return new LinkIndexDto(
            targets,
            unresolved.Values
                .OrderByDescending(item => item.Count)
                .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
                .Select(item => new UnresolvedLinkDto(item.Title, item.Count))
                .ToList());
    }

    public IReadOnlyList<BacklinkDto> GetBacklinks(VaultRuntime runtime, string id)
    {
        var snapshot = Snapshot(runtime);
        var target = RequireNote(runtime, id);
        var results = new List<BacklinkDto>();
        foreach (var note in snapshot.Notes)
        {
            if (note.Id == target.Id)
            {
                continue;
            }

            var content = TryReadText(note.FullPath);
            if (content is null)
            {
                continue;
            }

            var hit = ExtractWikiLinks(content)
                .FirstOrDefault(link => string.Equals(link.Title, target.Title, StringComparison.OrdinalIgnoreCase));
            if (hit is not null)
            {
                results.Add(new BacklinkDto(note.Id, note.Title, note.ProjectName, hit.Line, hit.Excerpt));
            }
        }

        return results.OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public NoteDto GetNote(VaultRuntime runtime, string id)
    {
        var note = RequireNote(runtime, id);
        try
        {
            var bytes = File.ReadAllBytes(note.FullPath);
            return ToNote(runtime, note, bytes);
        }
        catch (FileNotFoundException)
        {
            Invalidate(runtime.Key);
            throw new ApiException(StatusCodes.Status404NotFound, "note-not-found", "Note not found.");
        }
        catch (DirectoryNotFoundException)
        {
            Invalidate(runtime.Key);
            throw new ApiException(StatusCodes.Status404NotFound, "note-not-found", "Note not found.");
        }
    }

    public NoteDto CreateNote(VaultRuntime runtime, CreateNoteRequest request)
    {
        var snapshot = Snapshot(runtime);
        var projectPath = ResolveProjectPath(snapshot, request.ProjectId);
        var directory = projectPath.Length == 0
            ? runtime.Paths.VaultRoot
            : VaultPath.Combine(runtime.Paths.VaultRoot, projectPath);
        Directory.CreateDirectory(directory);

        var title = (request.Title ?? string.Empty).Trim();
        var isUntitled = title.Length == 0;
        var stem = isUntitled
            ? Guid.NewGuid().ToString().ToUpperInvariant()
            : TitleHelper.ToFileStem(title);

        if (stem.Length == 0)
        {
            stem = Guid.NewGuid().ToString().ToUpperInvariant();
            isUntitled = true;
        }

        stem = TitleHelper.UniqueFileStem(directory, stem, ".md");
        var fullPath = Path.Combine(directory, stem + ".md");
        using (var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            // An empty note: MiaoYan creates the file lazily, we create it now.
        }

        var relative = VaultPath.Relative(runtime.Paths.VaultRoot, fullPath);
        Invalidate(runtime.Key);
        var note = RequireNote(runtime, NoteIdFor(relative));
        _hub.Publish(runtime.Key, new VaultChange("created", note.Id, relative, DateTimeOffset.UtcNow));
        return ToNote(runtime, note, File.ReadAllBytes(fullPath));
    }

    public async Task<SaveResult> SaveNoteAsync(VaultRuntime runtime, string id, SaveNoteRequest request, CancellationToken cancellationToken)
    {
        if (request.Content is null)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "content-required", "Content is required.");
        }

        var note = RequireNote(runtime, id);
        var gate = LockFor(note.FullPath);
        await gate.WaitAsync(cancellationToken);
        try
        {
            // Re-resolve under the lock: the note may have moved while we waited.
            note = RequireNote(runtime, id);
            byte[] currentBytes;
            try
            {
                currentBytes = await File.ReadAllBytesAsync(note.FullPath, cancellationToken);
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                Invalidate(runtime.Key);
                throw new ApiException(StatusCodes.Status404NotFound, "note-not-found", "Note not found.");
            }

            var currentEtag = ComputeEtag(currentBytes);
            if (!string.IsNullOrWhiteSpace(request.Etag)
                && !string.Equals(request.Etag, currentEtag, StringComparison.OrdinalIgnoreCase))
            {
                var conflictPath = WriteConflictCopy(runtime, note, request.Content);
                _hub.Publish(runtime.Key, new VaultChange("conflict", note.Id, note.RelPath, DateTimeOffset.UtcNow));
                return new SaveResult(null, new ConflictDto(
                    "conflict",
                    "The note changed on disk after it was opened. Your text was saved as a conflict copy.",
                    currentEtag,
                    conflictPath,
                    Decode(currentBytes).Content));
            }

            // Keep the pre-edit text as a version too, so the very first save
            // can still be undone back to the note's original content.
            SnapshotVersion(runtime, note, currentBytes);

            var hasBom = HasBom(currentBytes);
            var bytes = new UTF8Encoding(hasBom).GetBytes(request.Content);
            await WriteAtomicAsync(note.FullPath, bytes, cancellationToken);
            SnapshotVersion(runtime, note, bytes);
            _hub.Suppress(runtime.Key, note.FullPath);
            Invalidate(runtime.Key);

            var fresh = RequireNote(runtime, NoteIdFor(note.RelPath));
            _hub.Publish(runtime.Key, new VaultChange("saved", fresh.Id, fresh.RelPath, DateTimeOffset.UtcNow));
            return new SaveResult(ToNote(runtime, fresh, bytes), null);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Upsert used by the ingest-compatible API. The client owns its own note
    /// ids; they are kept in a small managed frontmatter block so a rename in
    /// the native UI does not break the next publish. Everything the client
    /// did not touch (foreign frontmatter keys, body text outside its markers)
    /// survives.
    /// </summary>
    public async Task<SaveResult> SaveIngestNoteAsync(
        VaultRuntime runtime,
        string id,
        SaveNoteRequest request,
        string? ifMatch,
        CancellationToken cancellationToken)
    {
        if (request.Body is null)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "body-required", "A body is required.");
        }

        var body = MediaLibrary.NormalizeBody(request.Body);
        var gate = _locks.GetOrAdd("ingest:" + runtime.Key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var snapshot = Snapshot(runtime);
            return snapshot.ById.TryGetValue(id, out var existing)
                ? await UpdateIngestNoteAsync(runtime, id, existing, body, request, ifMatch, cancellationToken)
                : await CreateIngestNoteAsync(runtime, id, body, request, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<SaveResult> UpdateIngestNoteAsync(
        VaultRuntime runtime,
        string id,
        VaultNote note,
        string body,
        SaveNoteRequest request,
        string? ifMatch,
        CancellationToken cancellationToken)
    {
        var gate = LockFor(note.FullPath);
        await gate.WaitAsync(cancellationToken);
        try
        {
            note = RequireNote(runtime, id);
            byte[] currentBytes;
            try
            {
                currentBytes = await File.ReadAllBytesAsync(note.FullPath, cancellationToken);
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                Invalidate(runtime.Key);
                throw new ApiException(StatusCodes.Status404NotFound, "note-not-found", "Note not found.");
            }

            var currentEtag = ComputeEtag(currentBytes);
            if (!string.IsNullOrWhiteSpace(ifMatch)
                && ifMatch != "*"
                && !string.Equals(ifMatch, currentEtag, StringComparison.OrdinalIgnoreCase))
            {
                var currentContent = Decode(currentBytes).Content;
                var conflictPath = WriteConflictCopy(runtime, note, body);
                return new SaveResult(null, new ConflictDto(
                    "conflict",
                    "The note changed on disk after it was read.",
                    currentEtag,
                    conflictPath,
                    currentContent));
            }

            var content = Decode(currentBytes).Content;
            var metadata = ManagedFrontmatter.Parse(content);
            var oldId = note.Id;
            var wasPinned = _index.For(runtime).PinnedIds().Contains(note.Id);

            var targetPath = note.FullPath;
            var desiredTitle = string.IsNullOrWhiteSpace(request.Title) ? note.Title : request.Title.Trim();
            if (!string.Equals(desiredTitle, note.Title, StringComparison.Ordinal))
            {
                targetPath = RenameForIngest(runtime, note, desiredTitle, targetPath);
            }

            var finalTitle = targetPath == note.FullPath
                ? note.Title
                : TitleHelper.TitleFromFile(targetPath);

            var merged = ManagedFrontmatter.Merge(
                content,
                new ManagedFrontmatter(
                    metadata.Id ?? ManagedFrontmatter.NormaliseId(id) ?? note.Id,
                    finalTitle,
                    request.Tags is null ? metadata.Tags : NormaliseTags(request.Tags),
                    request.Color ?? metadata.Color,
                    request.Kind ?? metadata.Kind,
                    request.Archived ?? metadata.Archived,
                    metadata.Created ?? note.CreatedAt,
                    DateTimeOffset.UtcNow,
                    body,
                    metadata.Header));

            SnapshotVersion(runtime, note, currentBytes);
            var hasBom = HasBom(currentBytes);
            var bytes = new UTF8Encoding(hasBom).GetBytes(merged);
            await WriteAtomicAsync(targetPath, bytes, cancellationToken);
            _hub.Suppress(runtime.Key, note.FullPath);
            _hub.Suppress(runtime.Key, targetPath);
            Invalidate(runtime.Key);

            var relative = VaultPath.Relative(runtime.Paths.VaultRoot, targetPath);
            var fresh = RequireNote(runtime, NoteIdFor(relative));
            MoveVersions(runtime, oldId, fresh.Id);
            if (request.Pinned is { } pinned)
            {
                _index.For(runtime).SetPinned(fresh.Id, pinned);
                if (fresh.Id != oldId)
                {
                    _index.For(runtime).SetPinned(oldId, false);
                }
            }
            else if (wasPinned && fresh.Id != oldId)
            {
                _index.For(runtime).MovePin(oldId, fresh.Id);
            }

            SnapshotVersion(runtime, fresh, bytes);
            _hub.Publish(runtime.Key, new VaultChange("saved", fresh.Id, relative, DateTimeOffset.UtcNow));
            return new SaveResult(ToNote(runtime, fresh, bytes), null);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<SaveResult> CreateIngestNoteAsync(
        VaultRuntime runtime,
        string id,
        string body,
        SaveNoteRequest request,
        CancellationToken cancellationToken)
    {
        var root = runtime.Paths.VaultRoot;
        var requestedTitle = string.IsNullOrWhiteSpace(request.Title) ? id : request.Title.Trim();
        var stem = TitleHelper.ToFileStem(requestedTitle);
        if (stem.Length == 0 || TitleHelper.IsUuid(stem))
        {
            stem = TitleHelper.Untitled;
        }

        stem = TitleHelper.UniqueFileStem(root, stem, ".md");
        var fullPath = Path.Combine(root, stem + ".md");
        var relative = VaultPath.Relative(root, fullPath);
        var title = TitleHelper.TitleFromFile(fullPath);
        var now = DateTimeOffset.UtcNow;
        var managedId = ManagedFrontmatter.NormaliseId(id) ?? NoteIdFor(relative);
        var merged = ManagedFrontmatter.Merge(
            string.Empty,
            new ManagedFrontmatter(
                managedId,
                title,
                NormaliseTags(request.Tags ?? []),
                request.Color,
                request.Kind ?? "note",
                request.Archived ?? false,
                now,
                now,
                body,
                null));

        var bytes = new UTF8Encoding(false).GetBytes(merged);
        await WriteAtomicAsync(fullPath, bytes, cancellationToken);
        _hub.Suppress(runtime.Key, fullPath);
        Invalidate(runtime.Key);

        var fresh = RequireNote(runtime, NoteIdFor(relative));
        if (request.Pinned is { } pinned)
        {
            _index.For(runtime).SetPinned(fresh.Id, pinned);
        }

        _hub.Publish(runtime.Key, new VaultChange("created", fresh.Id, relative, DateTimeOffset.UtcNow));
        return new SaveResult(ToNote(runtime, fresh, bytes), null);
    }

    private string RenameForIngest(VaultRuntime runtime, VaultNote note, string title, string currentPath)
    {
        var stem = TitleHelper.ToFileStem(title);
        if (stem.Length == 0 || TitleHelper.IsUuid(stem))
        {
            return currentPath;
        }

        var directory = Path.GetDirectoryName(currentPath)!;
        var candidate = Path.Combine(directory, stem + ".md");
        if (string.Equals(candidate, currentPath, StringComparison.Ordinal))
        {
            return currentPath;
        }

        if (File.Exists(candidate) || Directory.Exists(candidate))
        {
            candidate = Path.Combine(directory, TitleHelper.UniqueFileStem(directory, stem, ".md") + ".md");
        }

        File.Move(currentPath, candidate);
        _hub.Suppress(runtime.Key, currentPath);
        _hub.Suppress(runtime.Key, candidate);
        return candidate;
    }

    private static IReadOnlyList<string> NormaliseTags(IReadOnlyList<string> tags)
        => tags
            .Select(tag => (tag ?? string.Empty).Trim())
            .Where(tag => tag.Length is > 0 and <= 64)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(64)
            .ToList();

    public IReadOnlyList<VersionDto> ListVersions(VaultRuntime runtime, string id)
    {
        var note = RequireNote(runtime, id);
        var directory = VersionDirectory(runtime, note.Id);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var currentEtag = ComputeEtag(File.ReadAllBytes(note.FullPath));
        return Directory.EnumerateFiles(directory, "*.md")
            .Select(path => new FileInfo(path))
            .OrderByDescending(info => info.Name, StringComparer.Ordinal)
            .Select(info =>
            {
                var versionId = Path.GetFileNameWithoutExtension(info.Name);
                var etag = VersionEtag(versionId);
                return new VersionDto(
                    versionId,
                    VersionCreatedAt(versionId, info),
                    info.Length,
                    etag,
                    etag.Length > 0 && currentEtag.StartsWith(etag, StringComparison.OrdinalIgnoreCase));
            })
            .ToList();
    }

    public VersionDetailDto GetVersion(VaultRuntime runtime, string id, string versionId)
    {
        var note = RequireNote(runtime, id);
        var path = VersionPath(runtime, note.Id, versionId);
        if (!File.Exists(path))
        {
            throw new ApiException(StatusCodes.Status404NotFound, "version-not-found", "Version not found.");
        }

        var bytes = File.ReadAllBytes(path);
        return new VersionDetailDto(
            versionId,
            Decode(bytes).Content,
            VersionCreatedAt(versionId, new FileInfo(path)),
            bytes.Length,
            ComputeEtag(bytes));
    }

    public SaveResult RestoreVersion(
        VaultRuntime runtime,
        string id,
        string versionId,
        RestoreVersionRequest request)
    {
        var note = RequireNote(runtime, id);
        var gate = LockFor(note.FullPath);
        gate.Wait();
        try
        {
            note = RequireNote(runtime, id);
            var versionPath = VersionPath(runtime, note.Id, versionId);
            if (!File.Exists(versionPath))
            {
                throw new ApiException(StatusCodes.Status404NotFound, "version-not-found", "Version not found.");
            }

            var currentBytes = File.ReadAllBytes(note.FullPath);
            var currentEtag = ComputeEtag(currentBytes);
            if (!string.IsNullOrWhiteSpace(request.Etag)
                && !string.Equals(request.Etag, currentEtag, StringComparison.OrdinalIgnoreCase))
            {
                var currentContent = Decode(currentBytes).Content;
                var conflictPath = WriteConflictCopy(runtime, note, currentContent);
                return new SaveResult(null, new ConflictDto(
                    "conflict",
                    "The note changed on disk before the version could be restored.",
                    currentEtag,
                    conflictPath,
                    currentContent));
            }

            var versionBytes = File.ReadAllBytes(versionPath);
            var hasBom = HasBom(currentBytes);
            var bytes = new UTF8Encoding(hasBom).GetBytes(Decode(versionBytes).Content);
            WriteAtomicAsync(note.FullPath, bytes, CancellationToken.None).GetAwaiter().GetResult();
            SnapshotVersion(runtime, note, currentBytes);
            _hub.Suppress(runtime.Key, note.FullPath);
            Invalidate(runtime.Key);

            var fresh = RequireNote(runtime, NoteIdFor(note.RelPath));
            _hub.Publish(runtime.Key, new VaultChange("restored", fresh.Id, fresh.RelPath, DateTimeOffset.UtcNow));
            return new SaveResult(ToNote(runtime, fresh, bytes), null);
        }
        finally
        {
            gate.Release();
        }
    }

    public NoteDto RenameNote(VaultRuntime runtime, string id, RenameNoteRequest request)
    {
        var note = RequireNote(runtime, id);
        var gate = LockFor(note.FullPath);
        gate.Wait();
        try
        {
            note = RequireNote(runtime, id);
            var wasPinned = _index.For(runtime).PinnedIds().Contains(note.Id);
            var snapshot = Snapshot(runtime);
            var currentDirectory = Path.GetDirectoryName(note.FullPath)!;
            var targetProjectPath = request.ProjectId is null
                ? note.ProjectPath
                : ResolveProjectPath(snapshot, request.ProjectId);
            var targetDirectory = targetProjectPath.Length == 0
                ? runtime.Paths.VaultRoot
                : VaultPath.Combine(runtime.Paths.VaultRoot, targetProjectPath);
            Directory.CreateDirectory(targetDirectory);

            string targetName;
            if (string.IsNullOrWhiteSpace(request.Title))
            {
                targetName = Path.GetFileName(note.FullPath);
            }
            else
            {
                var stem = TitleHelper.ToFileStem(request.Title);
                if (stem.Length == 0)
                {
                    throw new ApiException(StatusCodes.Status400BadRequest, "title-required", "Title cannot be empty.");
                }

                if (TitleHelper.IsUuid(stem))
                {
                    throw new ApiException(StatusCodes.Status400BadRequest, "title-reserved", "A UUID cannot be used as a title.");
                }

                targetName = stem + ".md";
            }

            var targetPath = Path.Combine(targetDirectory, targetName);
            if (!string.Equals(targetPath, note.FullPath, StringComparison.Ordinal))
            {
                if (File.Exists(targetPath) || Directory.Exists(targetPath))
                {
                    throw new ApiException(
                        StatusCodes.Status409Conflict,
                        "name-exists",
                        "A file with that name already exists. Choose another title.");
                }

                File.Move(note.FullPath, targetPath);
                _hub.Suppress(runtime.Key, note.FullPath);
                _hub.Suppress(runtime.Key, targetPath);
            }

            Invalidate(runtime.Key);
            var relative = VaultPath.Relative(runtime.Paths.VaultRoot, targetPath);
            var fresh = RequireNote(runtime, NoteIdFor(relative));
            MoveVersions(runtime, note.Id, fresh.Id);
            if (request.Pinned is { } pinned)
            {
                _index.For(runtime).SetPinned(fresh.Id, pinned);
                if (fresh.Id != note.Id)
                {
                    _index.For(runtime).SetPinned(note.Id, false);
                }
            }
            else if (wasPinned && fresh.Id != note.Id)
            {
                _index.For(runtime).MovePin(note.Id, fresh.Id);
            }

            _hub.Publish(runtime.Key, new VaultChange("renamed", fresh.Id, relative, DateTimeOffset.UtcNow));
            return ToNote(runtime, fresh, File.ReadAllBytes(targetPath));
        }
        finally
        {
            gate.Release();
        }
    }

    public NoteDto DuplicateNote(VaultRuntime runtime, string id)
    {
        var note = RequireNote(runtime, id);
        var gate = LockFor(note.FullPath);
        gate.Wait();
        try
        {
            note = RequireNote(runtime, id);
            var directory = Path.GetDirectoryName(note.FullPath)!;
            var sourceStem = TitleHelper.ToFileStem(note.Title);
            if (sourceStem.Length == 0)
            {
                sourceStem = "Untitled Note";
            }

            var stem = sourceStem + " Copy";
            var index = 2;
            while (File.Exists(Path.Combine(directory, stem + ".md")))
            {
                stem = $"{sourceStem} Copy {index++}";
            }

            var targetPath = Path.Combine(directory, stem + ".md");
            var sourceBytes = File.ReadAllBytes(note.FullPath);
            using (var stream = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(sourceBytes);
            }

            var relative = VaultPath.Relative(runtime.Paths.VaultRoot, targetPath);
            Invalidate(runtime.Key);
            var fresh = RequireNote(runtime, NoteIdFor(relative));
            _hub.Publish(runtime.Key, new VaultChange("created", fresh.Id, relative, DateTimeOffset.UtcNow));
            return ToNote(runtime, fresh, sourceBytes);
        }
        finally
        {
            gate.Release();
        }
    }

    public void DeleteNote(VaultRuntime runtime, string id)
    {
        var note = RequireNote(runtime, id);
        var gate = LockFor(note.FullPath);
        gate.Wait();
        try
        {
            note = RequireNote(runtime, id);
            var entryId = NewTrashEntryId();
            var entry = Path.Combine(runtime.Paths.TrashRoot, entryId);
            var content = Path.Combine(entry, "content", note.RelPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(content)!);

            File.Move(note.FullPath, content);
            WriteTrashMeta(entry, new TrashMeta("note", note.Title, note.RelPath, DateTimeOffset.UtcNow, note.Size));
            _hub.Suppress(runtime.Key, note.FullPath);
            Invalidate(runtime.Key);
            _hub.Publish(runtime.Key, new VaultChange("deleted", note.Id, note.RelPath, DateTimeOffset.UtcNow));
        }
        finally
        {
            gate.Release();
        }
    }

    public ProjectDto CreateProject(VaultRuntime runtime, CreateProjectRequest request)
    {
        var name = TitleHelper.ToProjectName(request.Name ?? string.Empty);
        if (name.Length == 0)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "name-required", "Project name is required.");
        }

        var path = Path.Combine(runtime.Paths.VaultRoot, name);
        if (Directory.Exists(path) || File.Exists(path))
        {
            throw new ApiException(StatusCodes.Status409Conflict, "name-exists", "A project with that name already exists.");
        }

        Directory.CreateDirectory(path);
        Invalidate(runtime.Key);
        _hub.Publish(runtime.Key, new VaultChange("project-created", null, name, DateTimeOffset.UtcNow));
        return new ProjectDto(ProjectIdFor(name), name, name, 0, "folder");
    }

    public ProjectDto RenameProject(VaultRuntime runtime, string id, RenameProjectRequest request)
    {
        var snapshot = Snapshot(runtime);
        var currentPath = FindProjectPath(snapshot, id)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "project-not-found", "Project not found.");
        if (currentPath.Length == 0)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "cannot-rename-root", "The root project cannot be renamed.");
        }

        var name = TitleHelper.ToProjectName(request.Name ?? string.Empty);
        if (name.Length == 0)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "name-required", "Project name is required.");
        }

        var source = VaultPath.Combine(runtime.Paths.VaultRoot, currentPath);
        var target = Path.Combine(runtime.Paths.VaultRoot, name);
        if (!string.Equals(source, target, StringComparison.Ordinal))
        {
            if (Directory.Exists(target) || File.Exists(target))
            {
                throw new ApiException(StatusCodes.Status409Conflict, "name-exists", "A project with that name already exists.");
            }

            Directory.Move(source, target);
            _hub.Suppress(runtime.Key, source);
            _hub.Suppress(runtime.Key, target);
        }

        Invalidate(runtime.Key);
        _hub.Publish(runtime.Key, new VaultChange("project-renamed", null, name, DateTimeOffset.UtcNow));
        return new ProjectDto(ProjectIdFor(name), name, name, 0, "folder");
    }

    public void DeleteProject(VaultRuntime runtime, string id)
    {
        var snapshot = Snapshot(runtime);
        var path = FindProjectPath(snapshot, id)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "project-not-found", "Project not found.");
        if (path.Length == 0)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "cannot-delete-root", "The root project cannot be deleted.");
        }

        var source = VaultPath.Combine(runtime.Paths.VaultRoot, path);
        var entryId = NewTrashEntryId();
        var entry = Path.Combine(runtime.Paths.TrashRoot, entryId);
        var content = Path.Combine(entry, "content", path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(content)!);

        var size = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
            .Sum(file => new FileInfo(file).Length);
        Directory.Move(source, content);
        WriteTrashMeta(entry, new TrashMeta("project", Path.GetFileName(source), path, DateTimeOffset.UtcNow, size));
        _hub.Suppress(runtime.Key, source);
        Invalidate(runtime.Key);
        _hub.Publish(runtime.Key, new VaultChange("project-deleted", null, path, DateTimeOffset.UtcNow));
    }

    public IReadOnlyList<TrashEntryDto> ListTrash(VaultRuntime runtime)
    {
        if (!Directory.Exists(runtime.Paths.TrashRoot))
        {
            return [];
        }

        var entries = new List<TrashEntryDto>();
        foreach (var directory in Directory.EnumerateDirectories(runtime.Paths.TrashRoot))
        {
            var meta = ReadTrashMeta(directory);
            if (meta is null)
            {
                continue;
            }

            entries.Add(new TrashEntryDto(
                Path.GetFileName(directory),
                meta.Kind,
                meta.Title,
                meta.OriginalPath,
                meta.DeletedAt,
                meta.Size));
        }

        return entries.OrderByDescending(entry => entry.DeletedAt).ToList();
    }

    public TrashRestoreResult RestoreTrash(VaultRuntime runtime, string entryId)
    {
        var entry = TrashEntryDirectory(runtime, entryId);
        var meta = ReadTrashMeta(entry)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "trash-entry-not-found", "Trash entry not found.");

        var original = VaultPath.NormaliseRelative(meta.OriginalPath);
        var source = Path.Combine(entry, "content", original.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(source) && !Directory.Exists(source))
        {
            throw new ApiException(StatusCodes.Status410Gone, "trash-payload-missing", "The trashed file is missing.");
        }

        var destination = VaultPath.Combine(runtime.Paths.VaultRoot, original);
        if (File.Exists(destination) || Directory.Exists(destination))
        {
            destination = UniqueDestination(destination, meta.Kind == "project");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (meta.Kind == "project")
        {
            Directory.Move(source, destination);
        }
        else
        {
            File.Move(source, destination);
        }

        Directory.Delete(entry, recursive: true);
        Invalidate(runtime.Key);

        if (meta.Kind == "project")
        {
            var projectPath = VaultPath.Relative(runtime.Paths.VaultRoot, destination);
            _hub.Publish(runtime.Key, new VaultChange("project-restored", null, projectPath, DateTimeOffset.UtcNow));
            return new TrashRestoreResult("project", null, ProjectIdFor(projectPath));
        }

        var notePath = VaultPath.Relative(runtime.Paths.VaultRoot, destination);
        var noteId = NoteIdFor(notePath);
        _hub.Publish(runtime.Key, new VaultChange("restored", noteId, notePath, DateTimeOffset.UtcNow));
        return new TrashRestoreResult("note", noteId, null);
    }

    public void PurgeTrash(VaultRuntime runtime, string entryId)
    {
        var entry = TrashEntryDirectory(runtime, entryId);
        if (!Directory.Exists(entry))
        {
            throw new ApiException(StatusCodes.Status404NotFound, "trash-entry-not-found", "Trash entry not found.");
        }

        Directory.Delete(entry, recursive: true);
    }

    public async Task<AssetDto> SaveAssetAsync(
        VaultRuntime runtime,
        string noteId,
        string kind,
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

        var note = RequireNote(runtime, noteId);
        var isImage = kind.Equals("image", StringComparison.OrdinalIgnoreCase);
        var directory = Path.GetDirectoryName(note.FullPath)!;
        var subdirectory = isImage ? "i" : "files";
        var targetDirectory = Path.Combine(directory, subdirectory);
        Directory.CreateDirectory(targetDirectory);

        var originalName = Path.GetFileName(file.FileName);
        var stem = TitleHelper.ToAssetStem(Path.GetFileNameWithoutExtension(originalName));
        if (stem.Length == 0)
        {
            stem = isImage ? "image" : "attachment";
        }

        var extension = Path.GetExtension(originalName);
        if (extension.Length > 16 || extension.Any(char.IsControl) || extension.IndexOfAny(['/', '\\']) >= 0)
        {
            extension = string.Empty;
        }

        var uniqueStem = TitleHelper.UniqueAssetStem(targetDirectory, stem, extension);
        var fileName = uniqueStem + extension;
        var fullPath = Path.Combine(targetDirectory, fileName);

        await using (var input = file.OpenReadStream())
        await using (var output = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                total += read;
                if (total > maxBytes)
                {
                    await output.DisposeAsync();
                    File.Delete(fullPath);
                    throw new ApiException(StatusCodes.Status413PayloadTooLarge, "file-too-large", "The uploaded file is too large.");
                }

                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }

        var relative = $"{subdirectory}/{fileName}";
        var markdown = isImage
            ? $"![]({relative})"
            : $"[{fileName}]({relative})";
        var url = $"/api/notes/{note.Id}/assets/{subdirectory}/{Uri.EscapeDataString(fileName)}";
        _hub.Suppress(runtime.Key, fullPath);
        Invalidate(runtime.Key);
        _hub.Publish(runtime.Key, new VaultChange("asset", note.Id, note.RelPath, DateTimeOffset.UtcNow));

        return new AssetDto(url, markdown, fileName, file.Length, isImage ? "image" : "file");
    }

    public AssetFile ResolveAsset(VaultRuntime runtime, string noteId, string assetPath)
    {
        var note = RequireNote(runtime, noteId);
        var relative = VaultPath.NormaliseRelative(assetPath);
        var first = relative.Split('/')[0];
        if (first is not ("i" or "files"))
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "invalid-asset", "Only i/ and files/ can be served.");
        }

        var directory = Path.GetDirectoryName(note.FullPath)!;
        var fullPath = VaultPath.Combine(directory, relative);
        if (!File.Exists(fullPath))
        {
            // Media published through the ingest-compatible API lives at the
            // library root; the same relative path keeps markdown portable
            // when a project note references it.
            fullPath = VaultPath.Combine(runtime.Paths.VaultRoot, relative);
        }

        if (!File.Exists(fullPath))
        {
            throw new ApiException(StatusCodes.Status404NotFound, "asset-not-found", "Asset not found.");
        }

        return new AssetFile(fullPath, Path.GetFileName(fullPath));
    }

    public string ComputeEtag(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes))[..32].ToLowerInvariant();

    /// <summary>
    /// Writes a snapshot of the saved content. Duplicate saves of the same
    /// bytes are ignored. History is capped at 50 files or 30 days, mirroring
    /// MiaoYan's per-note version budget.
    /// </summary>
    private void SnapshotVersion(VaultRuntime runtime, VaultNote note, byte[] bytes)
    {
        try
        {
            var directory = VersionDirectory(runtime, note.Id);
            Directory.CreateDirectory(directory);
            var etag = ComputeEtag(bytes);
            var newest = Directory.EnumerateFiles(directory, "*.md")
                .OrderByDescending(path => Path.GetFileName(path), StringComparer.Ordinal)
                .FirstOrDefault();
            var newestEtag = newest is null ? string.Empty : VersionEtag(Path.GetFileNameWithoutExtension(newest));
            if (newestEtag.Length > 0 && etag.StartsWith(newestEtag, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Ticks keep two saves in the same millisecond ordered; the etag
            // suffix makes the filename idempotent for identical saves.
            var id = $"{DateTime.UtcNow.Ticks:D18}-{etag[..8]}";
            var path = Path.Combine(directory, id + ".md");
            if (!File.Exists(path))
            {
                File.WriteAllBytes(path, bytes);
            }

            var cutoff = DateTime.UtcNow.AddDays(-30);
            var files = Directory.EnumerateFiles(directory, "*.md")
                .Select(file => new FileInfo(file))
                .OrderByDescending(info => info.Name, StringComparer.Ordinal)
                .ToList();
            for (var index = 0; index < files.Count; index++)
            {
                if (index >= 50 || files[index].LastWriteTimeUtc < cutoff)
                {
                    try
                    {
                        files[index].Delete();
                    }
                    catch (IOException)
                    {
                        // A concurrent restore may still hold the file.
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not snapshot a version for {Note}", note.RelPath);
        }
    }

    private static string VersionDirectory(VaultRuntime runtime, string noteId)
    {
        if (noteId.Length == 0 || noteId.Length > 64 || noteId.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "invalid-version", "Invalid note identifier.");
        }

        return Path.Combine(runtime.Paths.VersionsRoot, noteId);
    }

    private static string VersionPath(VaultRuntime runtime, string noteId, string versionId)
    {
        if (!IsSafeVersionId(versionId))
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "invalid-version", "Invalid version identifier.");
        }

        return Path.Combine(VersionDirectory(runtime, noteId), versionId + ".md");
    }

    private static bool IsSafeVersionId(string versionId)
        => versionId.Length is > 0 and <= 64
            && versionId.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private static string VersionEtag(string versionId)
    {
        var separator = versionId.IndexOf('-');
        return separator >= 0 && separator < versionId.Length - 1
            ? versionId[(separator + 1)..]
            : string.Empty;
    }

    private static DateTimeOffset VersionCreatedAt(string versionId, FileInfo info)
    {
        var separator = versionId.IndexOf('-');
        var stamp = separator > 0 ? versionId[..separator] : versionId;
        if (stamp.Length == 18 && long.TryParse(stamp, out var ticks))
        {
            try
            {
                return new DateTimeOffset(ticks, TimeSpan.Zero);
            }
            catch (ArgumentOutOfRangeException)
            {
                // Fall through to the file timestamp.
            }
        }

        if (stamp.Length == 17
            && DateTimeOffset.TryParseExact(
                stamp,
                "yyyyMMddHHmmssfff",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal,
                out var parsed))
        {
            return parsed;
        }

        return new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);
    }

    private static void MoveVersions(VaultRuntime runtime, string oldNoteId, string newNoteId)
    {
        if (oldNoteId == newNoteId)
        {
            return;
        }

        var source = Path.Combine(runtime.Paths.VersionsRoot, oldNoteId);
        if (!Directory.Exists(source))
        {
            return;
        }

        var destination = Path.Combine(runtime.Paths.VersionsRoot, newNoteId);
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*.md"))
        {
            var target = Path.Combine(destination, Path.GetFileName(file));
            if (!File.Exists(target))
            {
                File.Move(file, target);
            }
        }

        try
        {
            Directory.Delete(source, recursive: true);
        }
        catch (IOException)
        {
            // Leave the empty directory for a later cleanup.
        }
    }

    private string WriteConflictCopy(VaultRuntime runtime, VaultNote note, string content)
    {
        var stem = Path.GetFileNameWithoutExtension(note.FullPath);
        var suffix = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..22];
        var relativeDirectory = note.RelPath.Contains('/')
            ? note.RelPath[..note.RelPath.LastIndexOf('/')]
            : string.Empty;
        var directory = relativeDirectory.Length == 0
            ? runtime.Paths.ConflictRoot
            : Path.Combine(runtime.Paths.ConflictRoot, relativeDirectory.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, $"{stem}-{suffix}.md");
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return VaultPath.Relative(runtime.Paths.VaultRoot, path);
    }

    private static async Task WriteAtomicAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)!;
        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.miaoyan-save-{Guid.NewGuid():N}");
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private VaultNote RequireNote(VaultRuntime runtime, string id)
    {
        var snapshot = Snapshot(runtime);
        if (!snapshot.ById.TryGetValue(id, out var note))
        {
            throw new ApiException(StatusCodes.Status404NotFound, "note-not-found", "Note not found.");
        }

        return note;
    }

    private static string ResolveProjectPath(VaultSnapshot snapshot, string? projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId)
            || projectId == AllProjectId
            || projectId == RootProjectId)
        {
            return string.Empty;
        }

        return FindProjectPath(snapshot, projectId)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "project-not-found", "Project not found.");
    }

    private static string? FindProjectPath(VaultSnapshot snapshot, string projectId)
    {
        if (projectId == RootProjectId)
        {
            return string.Empty;
        }

        return snapshot.Projects.FirstOrDefault(project => project.Id == projectId)?.Path;
    }

    private static string UniqueDestination(string destination, bool directory)
    {
        var parent = Path.GetDirectoryName(destination)!;
        if (directory)
        {
            var name = TitleHelper.UniqueDirectory(parent, Path.GetFileName(destination));
            return Path.Combine(parent, name);
        }

        var stem = Path.GetFileNameWithoutExtension(destination);
        var extension = Path.GetExtension(destination);
        return Path.Combine(parent, TitleHelper.UniqueFileStem(parent, stem, extension) + extension);
    }

    private string NewTrashEntryId()
        => $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}"[..22];

    private static string TrashEntryDirectory(VaultRuntime runtime, string entryId)
    {
        if (entryId.Length == 0
            || entryId.Length > 64
            || entryId is "." or ".."
            || entryId.Contains('/') || entryId.Contains('\\')
            || entryId.Any(char.IsControl))
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "invalid-trash-entry", "Invalid trash entry.");
        }

        return Path.Combine(runtime.Paths.TrashRoot, entryId);
    }

    private static void WriteTrashMeta(string entryDirectory, TrashMeta meta)
    {
        Directory.CreateDirectory(entryDirectory);
        File.WriteAllText(
            Path.Combine(entryDirectory, "meta.json"),
            JsonSerializer.Serialize(meta, TrashJson),
            new UTF8Encoding(false));
    }

    private static TrashMeta? ReadTrashMeta(string entryDirectory)
    {
        try
        {
            var path = Path.Combine(entryDirectory, "meta.json");
            return File.Exists(path)
                ? JsonSerializer.Deserialize<TrashMeta>(File.ReadAllText(path), TrashJson)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private VaultSnapshot Snapshot(VaultRuntime runtime)
    {
        var cache = _caches.GetOrAdd(runtime.Key, _ => new VaultCache());
        lock (cache.Gate)
        {
            if (cache.Dirty || cache.Value is null)
            {
                cache.Value = Scan(runtime);
                cache.Dirty = false;
            }

            return cache.Value;
        }
    }

    private VaultSnapshot Scan(VaultRuntime runtime)
    {
        var root = runtime.Paths.VaultRoot;
        Directory.CreateDirectory(root);

        var notes = new List<VaultNote>();
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        };

        foreach (var fullPath in Directory.EnumerateFiles(root, "*.md", options))
        {
            var relative = VaultPath.Relative(root, fullPath);
            if (!IsNotePath(relative))
            {
                continue;
            }

            try
            {
                var info = new FileInfo(fullPath);
                var projectPath = relative.Contains('/') ? relative[..relative.IndexOf('/')] : string.Empty;
                var projectName = projectPath.Length == 0
                    ? RootProjectName
                    : projectPath.Replace(':', '/');
                var folder = relative.Contains('/')
                    ? relative[..relative.LastIndexOf('/')]
                    : string.Empty;
                var title = TitleHelper.TitleFromFile(fullPath);

                notes.Add(new VaultNote(
                    NoteIdFor(relative),
                    relative,
                    fullPath,
                    title,
                    projectPath,
                    ProjectIdFor(projectPath),
                    projectName,
                    folder,
                    info.LastWriteTimeUtc,
                    info.CreationTimeUtc,
                    info.Length,
                    ReadExcerpt(fullPath),
                    title == TitleHelper.Untitled,
                    ReadManagedId(fullPath)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogDebug(ex, "Skipping unreadable note {Path}", fullPath);
            }
        }

        var projects = new List<ProjectDto>
        {
            new(RootProjectId, RootProjectName, string.Empty, notes.Count(note => note.ProjectPath.Length == 0), "root"),
        };

        var projectPaths = new HashSet<string>(
            notes.Select(note => note.ProjectPath).Where(path => path.Length > 0),
            StringComparer.Ordinal);
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                var name = Path.GetFileName(directory);
                if (name.StartsWith('.')
                    || name.Equals("i", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("files", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("node_modules", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                projectPaths.Add(name);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Cannot enumerate projects under {Root}", root);
        }

        foreach (var projectPath in projectPaths.OrderBy(path => path, StringComparer.Ordinal))
        {
            var count = notes.Count(note => note.ProjectPath == projectPath);
            projects.Add(new ProjectDto(
                ProjectIdFor(projectPath),
                projectPath.Replace(':', '/'),
                projectPath,
                count,
                "folder"));
        }

        var byId = notes.ToDictionary(note => note.Id, StringComparer.Ordinal);
        foreach (var note in notes)
        {
            // The path hash stays the native id. The managed id is only an
            // alias, and a native id always wins if the two ever collide.
            if (note.ManagedId is { } managedId)
            {
                byId.TryAdd(managedId, note);
            }
        }

        return new VaultSnapshot(byId, notes, projects);
    }

    private static bool IsNotePath(string relative)
    {
        if (!relative.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var segments = relative.Split('/');
        return segments.All(segment =>
            segment.Length > 0
            && !segment.StartsWith('.')
            && !segment.Equals("i", StringComparison.OrdinalIgnoreCase)
            && !segment.Equals("files", StringComparison.OrdinalIgnoreCase)
            && !segment.Equals("node_modules", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Extracts MiaoYan wikilinks from prose. Fenced code, inline code and YAML
    /// frontmatter are ignored, and line numbers are 1-based for the UI.
    /// </summary>
    private static IEnumerable<WikiLinkHit> ExtractWikiLinks(string content)
    {
        var lines = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var start = 0;
        if (lines.Length > 1 && lines[0].Trim() == "---")
        {
            for (var index = 1; index < Math.Min(lines.Length, 200); index++)
            {
                if (lines[index].Trim() == "---")
                {
                    start = index + 1;
                    break;
                }
            }
        }

        char? fence = null;
        for (var index = start; index < lines.Length; index++)
        {
            var line = lines[index];
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                var marker = trimmed[0];
                if (fence is null)
                {
                    fence = marker;
                }
                else if (fence == marker)
                {
                    fence = null;
                }

                continue;
            }

            if (fence is not null)
            {
                continue;
            }

            var masked = InlineCodeRegex().Replace(line, match => new string(' ', match.Length));
            foreach (Match match in WikiLinkRegex().Matches(masked))
            {
                var title = match.Groups[1].Value.Trim();
                if (title.Length == 0)
                {
                    continue;
                }

                var excerpt = WhitespaceRegex().Replace(line.Trim(), " ");
                if (excerpt.Length > 160)
                {
                    excerpt = excerpt[..160] + "…";
                }

                yield return new WikiLinkHit(title, index + 1, excerpt);
            }
        }
    }

    private static string? ReadManagedId(string fullPath)
    {
        try
        {
            using var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var buffer = new byte[64 * 1024];
            var read = stream.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                return null;
            }

            var text = Encoding.UTF8.GetString(buffer, 0, read);
            if (text.Length > 0 && text[0] == '﻿')
            {
                text = text[1..];
            }

            return ManagedFrontmatter.ReadId(text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? TryReadText(string fullPath)
    {
        try
        {
            using var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string ReadExcerpt(string fullPath)
    {
        try
        {
            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new byte[2048];
            var read = stream.Read(buffer, 0, buffer.Length);
            var text = Encoding.UTF8.GetString(buffer, 0, read);
            if (text.Length > 0 && text[0] == '﻿')
            {
                text = text[1..];
            }

            text = FrontmatterRegex().Replace(text, string.Empty, 1);
            text = ImageRegex().Replace(text, string.Empty);
            text = LinkRegex().Replace(text, "$1");
            text = HtmlRegex().Replace(text, " ");
            text = MarkerRegex().Replace(text, string.Empty);
            text = WhitespaceRegex().Replace(text, " ").Trim();
            return text.Length <= 180 ? text : text[..180] + "…";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private static (string Content, bool HasBom) Decode(byte[] bytes)
    {
        var hasBom = HasBom(bytes);
        var offset = hasBom ? 3 : 0;
        return (Encoding.UTF8.GetString(bytes, offset, bytes.Length - offset), hasBom);
    }

    private static bool HasBom(byte[] bytes)
        => bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;

    private static NoteSummaryDto ToSummary(VaultNote note, string excerpt, bool pinned)
        => new(
            note.Id,
            note.Title,
            note.RelPath,
            note.ProjectId,
            note.ProjectName,
            note.Folder,
            excerpt,
            note.ModifiedAt,
            note.CreatedAt,
            note.Size,
            note.IsUntitled,
            pinned);

    private NoteDto ToNote(VaultRuntime runtime, VaultNote note, byte[] bytes)
    {
        var decoded = Decode(bytes);
        var metadata = ManagedFrontmatter.Parse(decoded.Content);
        return new NoteDto(
            note.Id,
            note.Title,
            note.RelPath,
            note.ProjectId,
            note.ProjectName,
            note.Folder,
            decoded.Content,
            ComputeEtag(bytes),
            note.ModifiedAt,
            note.CreatedAt,
            bytes.Length,
            note.IsUntitled,
            _index.For(runtime).PinnedIds().Contains(note.Id),
            decoded.HasBom,
            metadata.Body,
            metadata.Tags,
            metadata.Color,
            metadata.Kind,
            metadata.Archived,
            false,
            false,
            note.ModifiedAt,
            metadata.Id ?? note.ManagedId);
    }

    /// <summary>
    /// Lists files under i/ and files/ that no note body references. Matching
    /// mirrors MiaoYan's cleanup: notes are read from disk, i/files paths are
    /// resolved against the note folder but bounded by the library, and
    /// attachments named by a Trash note are kept so a restore still works.
    /// </summary>
    public UnusedAssetsResult FindUnusedAssets(VaultRuntime runtime)
    {
        var root = Path.GetFullPath(runtime.Paths.VaultRoot);
        var referenced = new HashSet<string>(StringComparer.Ordinal);
        var trashNames = new HashSet<string>(StringComparer.Ordinal);
        var scanned = 0;

        foreach (var noteFile in EnumerateNoteFiles(root))
        {
            var relative = Path.GetRelativePath(root, noteFile).Replace('\\', '/');
            var inTrash = relative.StartsWith(".miaoyan/trash/", StringComparison.Ordinal);
            string text;
            try
            {
                text = File.ReadAllText(noteFile);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A body that cannot be read could reference any attachment.
                // Refuse the scan rather than offer a destructive answer.
                throw new ApiException(
                    StatusCodes.Status500InternalServerError,
                    "scan-failed",
                    $"Could not read {relative}; no attachments were removed.");
            }

            scanned += 1;
            var noteDirectory = Path.GetDirectoryName(noteFile)!;
            foreach (var reference in EnumerateAttachmentReferences(text))
            {
                var resolved = ResolveAttachmentPath(root, noteDirectory, reference);
                if (resolved is null)
                {
                    continue;
                }

                if (inTrash)
                {
                    trashNames.Add(Path.GetFileName(resolved));
                }
                else
                {
                    referenced.Add(resolved);
                }
            }
        }

        var files = new List<string>();
        foreach (var attachment in EnumerateAttachmentFiles(root))
        {
            var full = Path.GetFullPath(attachment);
            if (referenced.Contains(full) || trashNames.Contains(Path.GetFileName(full)))
            {
                continue;
            }

            files.Add(Path.GetRelativePath(root, full).Replace('\\', '/'));
        }

        files.Sort(StringComparer.Ordinal);
        return new UnusedAssetsResult(scanned, files);
    }

    /// <summary>
    /// Moves unreferenced attachments into the app trash instead of deleting
    /// them. MiaoYan uses the macOS Trash; the app trash is the browser/LazyCat
    /// equivalent and keeps cleanup recoverable.
    /// </summary>
    public CleanUnusedAssetsResult CleanUnusedAssets(VaultRuntime runtime)
    {
        var root = Path.GetFullPath(runtime.Paths.VaultRoot);
        var scan = FindUnusedAssets(runtime);
        if (scan.Files.Count == 0)
        {
            return new CleanUnusedAssetsResult([], []);
        }

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var trashRoot = Path.Combine(root, ".miaoyan", "trash-assets", stamp);
        var removed = new List<string>();
        var failed = new List<string>();
        foreach (var relative in scan.Files)
        {
            var source = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            var destination = Path.Combine(trashRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                destination = UniqueDestination(destination);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Move(source, destination);
                _hub.Suppress(runtime.Key, source);
                removed.Add(relative);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed.Add(relative);
                _logger.LogWarning(ex, "Could not move unused attachment {Path}", relative);
            }
        }

        if (removed.Count > 0)
        {
            Invalidate(runtime.Key);
            _hub.Publish(runtime.Key, new VaultChange("assets-cleaned", null, ".miaoyan/trash-assets", DateTimeOffset.UtcNow));
        }

        return new CleanUnusedAssetsResult(removed, failed);
    }

    private static IEnumerable<string> EnumerateNoteFiles(string root)
    {
        foreach (var file in EnumerateTree(root, includeTrash: true))
        {
            if (string.Equals(Path.GetExtension(file), ".md", StringComparison.OrdinalIgnoreCase))
            {
                yield return file;
            }
        }
    }

    private static IEnumerable<string> EnumerateAttachmentFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            FileSystemInfo[] entries;
            try
            {
                entries = new DirectoryInfo(directory).GetFileSystemInfos();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                if (entry is DirectoryInfo child)
                {
                    if (child.Name.StartsWith('.') ||
                        string.Equals(child.Name, "node_modules", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (child.Name is "i" or "files")
                    {
                        foreach (var file in EnumerateFilesUnder(child.FullName))
                        {
                            yield return file;
                        }
                        continue;
                    }

                    pending.Push(child.FullName);
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateFilesUnder(string directory)
    {
        var pending = new Stack<string>();
        pending.Push(directory);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            FileSystemInfo[] entries;
            try
            {
                entries = new DirectoryInfo(current).GetFileSystemInfos();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                if (entry is DirectoryInfo child)
                {
                    if (!child.Name.StartsWith('.'))
                    {
                        pending.Push(child.FullName);
                    }
                    continue;
                }

                if (entry.Name.StartsWith('.') || entry.Name.EndsWith(".icloud", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                yield return entry.FullName;
            }
        }
    }

    private static IEnumerable<string> EnumerateTree(string root, bool includeTrash)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            FileSystemInfo[] entries;
            try
            {
                entries = new DirectoryInfo(directory).GetFileSystemInfos();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                if (entry is DirectoryInfo child)
                {
                    if (child.Name.StartsWith('.'))
                    {
                        if (!includeTrash || child.Name != ".miaoyan")
                        {
                            continue;
                        }
                    }
                    else if (string.Equals(child.Name, "node_modules", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    pending.Push(child.FullName);
                    continue;
                }

                var relative = Path.GetRelativePath(root, entry.FullName).Replace('\\', '/');
                if (relative.StartsWith(".miaoyan/", StringComparison.Ordinal) &&
                    !relative.StartsWith(".miaoyan/trash/", StringComparison.Ordinal))
                {
                    continue;
                }

                yield return entry.FullName;
            }
        }
    }

    private static IEnumerable<string> EnumerateAttachmentReferences(string text)
    {
        foreach (Match match in AttachmentPathRegex.Matches(text))
        {
            yield return match.Value;
        }

        foreach (Match match in MarkdownDestinationRegex.Matches(text))
        {
            var destination = match.Groups[1].Value.Trim();
            if (destination.StartsWith('<') && destination.EndsWith('>'))
            {
                destination = destination[1..^1].Trim();
            }

            // Drop an optional Markdown title: ![](i/photo.png "caption").
            var titleIndex = destination.IndexOfAny(['"', '\'']);
            if (titleIndex > 0)
            {
                destination = destination[..titleIndex].TrimEnd();
            }

            if (!Regex.IsMatch(destination, @"^(?:\./|/)?(?:i|files)/"))
            {
                continue;
            }

            yield return destination;
        }
    }

    private static string? ResolveAttachmentPath(string root, string noteDirectory, string raw)
    {
        var decoded = raw;
        try
        {
            decoded = Uri.UnescapeDataString(raw);
        }
        catch (UriFormatException)
        {
        }

        if (decoded.Length == 0)
        {
            return null;
        }

        string candidate;
        if (decoded[0] == '/')
        {
            candidate = Path.Combine(root, decoded.TrimStart('/'));
        }
        else
        {
            if (decoded.StartsWith("./", StringComparison.Ordinal))
            {
                decoded = decoded[2..];
            }

            candidate = Path.Combine(noteDirectory, decoded.Replace('/', Path.DirectorySeparatorChar));
        }

        string full;
        try
        {
            full = Path.GetFullPath(candidate);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        return full.StartsWith(rootPrefix, StringComparison.Ordinal) ? full : null;
    }

    private static string UniqueDestination(string destination)
    {
        if (!File.Exists(destination))
        {
            return destination;
        }

        var directory = Path.GetDirectoryName(destination)!;
        var stem = Path.GetFileNameWithoutExtension(destination);
        var extension = Path.GetExtension(destination);
        for (var index = 1; index < 1000; index += 1)
        {
            var candidate = Path.Combine(directory, $"{stem}-{index}{extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException("No free destination name for trash entry.");
    }

    private SemaphoreSlim LockFor(string fullPath)
        => _locks.GetOrAdd(fullPath, _ => new SemaphoreSlim(1, 1));

    private sealed class VaultCache
    {
        public object Gate { get; } = new();
        public bool Dirty { get; set; } = true;
        public VaultSnapshot? Value { get; set; }
    }

    private sealed record VaultSnapshot(
        IReadOnlyDictionary<string, VaultNote> ById,
        IReadOnlyList<VaultNote> Notes,
        IReadOnlyList<ProjectDto> Projects);

    private sealed record VaultNote(
        string Id,
        string RelPath,
        string FullPath,
        string Title,
        string ProjectPath,
        string ProjectId,
        string ProjectName,
        string Folder,
        DateTimeOffset ModifiedAt,
        DateTimeOffset CreatedAt,
        long Size,
        string Excerpt,
        bool IsUntitled,
        string? ManagedId = null);

    private sealed record WikiLinkHit(string Title, int Line, string Excerpt);

    private sealed record TrashMeta(
        string Kind,
        string Title,
        string OriginalPath,
        DateTimeOffset DeletedAt,
        long Size);
}
