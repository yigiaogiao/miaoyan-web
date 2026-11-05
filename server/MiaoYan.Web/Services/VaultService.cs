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
    private readonly ILogger<VaultService> _logger;
    private readonly ConcurrentDictionary<string, VaultCache> _caches = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    private static readonly JsonSerializerOptions TrashJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    [GeneratedRegex(@"\A---\r?\n(?:.*\r?\n)*?---\r?\n", RegexOptions.Singleline)]
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

    public VaultService(VaultEventHub hub, ILogger<VaultService> logger)
    {
        _hub = hub;
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

        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            notes = notes.Where(note =>
                note.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
                || note.Excerpt.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        return notes
            .OrderByDescending(note => note.ModifiedAt)
            .Select(ToSummary)
            .ToList();
    }

    public NoteDto GetNote(VaultRuntime runtime, string id)
    {
        var note = RequireNote(runtime, id);
        try
        {
            var bytes = File.ReadAllBytes(note.FullPath);
            return ToNote(note, bytes);
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
        return ToNote(note, File.ReadAllBytes(fullPath));
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

            var hasBom = HasBom(currentBytes);
            var bytes = new UTF8Encoding(hasBom).GetBytes(request.Content);
            await WriteAtomicAsync(note.FullPath, bytes, cancellationToken);
            _hub.Suppress(runtime.Key, note.FullPath);
            Invalidate(runtime.Key);

            var fresh = RequireNote(runtime, NoteIdFor(note.RelPath));
            _hub.Publish(runtime.Key, new VaultChange("saved", fresh.Id, fresh.RelPath, DateTimeOffset.UtcNow));
            return new SaveResult(ToNote(fresh, bytes), null);
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
            _hub.Publish(runtime.Key, new VaultChange("renamed", fresh.Id, relative, DateTimeOffset.UtcNow));
            return ToNote(fresh, File.ReadAllBytes(targetPath));
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
        var stem = TitleHelper.ToFileStem(Path.GetFileNameWithoutExtension(originalName));
        if (stem.Length == 0)
        {
            stem = isImage ? "image" : "attachment";
        }

        var extension = Path.GetExtension(originalName);
        if (extension.Length > 16 || extension.Any(char.IsControl) || extension.IndexOfAny(['/', '\\']) >= 0)
        {
            extension = string.Empty;
        }

        var uniqueStem = TitleHelper.UniqueFileStem(targetDirectory, stem, extension);
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
            throw new ApiException(StatusCodes.Status404NotFound, "asset-not-found", "Asset not found.");
        }

        return new AssetFile(fullPath, Path.GetFileName(fullPath));
    }

    public string ComputeEtag(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes))[..32].ToLowerInvariant();

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
                    title == TitleHelper.Untitled));
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

    private NoteSummaryDto ToSummary(VaultNote note)
        => new(
            note.Id,
            note.Title,
            note.RelPath,
            note.ProjectId,
            note.ProjectName,
            note.Folder,
            note.Excerpt,
            note.ModifiedAt,
            note.CreatedAt,
            note.Size,
            note.IsUntitled);

    private NoteDto ToNote(VaultNote note, byte[] bytes)
    {
        var decoded = Decode(bytes);
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
            decoded.HasBom);
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
        bool IsUntitled);

    private sealed record TrashMeta(
        string Kind,
        string Title,
        string OriginalPath,
        DateTimeOffset DeletedAt,
        long Size);
}
