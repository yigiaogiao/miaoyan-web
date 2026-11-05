namespace MiaoYan.Web.Api;

public sealed record ProjectDto(
    string Id,
    string Name,
    string Path,
    int NoteCount,
    string Kind);

public sealed record NoteSummaryDto(
    string Id,
    string Title,
    string Path,
    string ProjectId,
    string ProjectName,
    string Folder,
    string Excerpt,
    DateTimeOffset ModifiedAt,
    DateTimeOffset CreatedAt,
    long Size,
    bool IsUntitled,
    bool Pinned,
    // Papyra compatibility fields. Native list calls pass summary=1 and do not
    // pay for reading every body.
    string? Body = null,
    IReadOnlyList<string>? Tags = null,
    string? Color = null,
    string Kind = "note",
    bool Archived = false,
    bool Secure = false,
    bool Trashed = false,
    DateTimeOffset? Updated = null,
    string? ManagedId = null);

public sealed record NoteDto(
    string Id,
    string Title,
    string Path,
    string ProjectId,
    string ProjectName,
    string Folder,
    string Content,
    string Etag,
    DateTimeOffset ModifiedAt,
    DateTimeOffset CreatedAt,
    long Size,
    bool IsUntitled,
    bool Pinned,
    bool HasBom,
    string? Body = null,
    IReadOnlyList<string>? Tags = null,
    string? Color = null,
    string Kind = "note",
    bool Archived = false,
    bool Secure = false,
    bool Trashed = false,
    DateTimeOffset? Updated = null,
    string? ManagedId = null);

public sealed record SaveNoteRequest(
    string? Content,
    string? Etag,
    string? Body = null,
    IReadOnlyList<string>? Tags = null,
    string? Color = null,
    string? Kind = null,
    bool? Pinned = null,
    bool? Archived = null,
    bool? Secure = null,
    string? Title = null);

public sealed record RenameNoteRequest(string? Title, string? ProjectId, bool? Pinned = null);

public sealed record CreateNoteRequest(string? ProjectId, string? Title);

public sealed record CreateProjectRequest(string? Name);

public sealed record RenameProjectRequest(string? Name);

public sealed record ConflictDto(
    string Error,
    string Message,
    string CurrentEtag,
    string ConflictPath,
    string CurrentContent);

public sealed record TrashEntryDto(
    string Id,
    string Kind,
    string Title,
    string OriginalPath,
    DateTimeOffset DeletedAt,
    long Size);

public sealed record AssetDto(
    string Url,
    string Markdown,
    string Name,
    long Size,
    string Kind);

public sealed record LinkTargetDto(
    string Title,
    string NoteId,
    string ProjectName,
    string Path);

public sealed record UnresolvedLinkDto(
    string Title,
    int Count);

public sealed record LinkIndexDto(
    IReadOnlyList<LinkTargetDto> Targets,
    IReadOnlyList<UnresolvedLinkDto> Unresolved);

public sealed record BacklinkDto(
    string NoteId,
    string Title,
    string ProjectName,
    int Line,
    string Excerpt);

public sealed record SearchHitDto(
    string Id,
    string Title,
    string ProjectId,
    string ProjectName,
    string Snippet,
    DateTimeOffset ModifiedAt,
    long Size,
    bool IsUntitled,
    double Score = 1);

public sealed record PapyraCategoryDto(string Name, string? Color, int Count);

public sealed record PapyraCategoryRequest(string? Name, string? Color);

public sealed record PapyraMediaDto(
    string Filename,
    string Kind,
    string Mime,
    long Size,
    string Version,
    string Url,
    string Markdown,
    int? Width = null,
    int? Height = null,
    long? DurationMs = null,
    bool? Animated = null,
    string? Poster = null,
    string? Thumb = null);

public sealed record VersionDto(
    string Id,
    DateTimeOffset CreatedAt,
    long Size,
    string Etag,
    bool IsCurrent);

public sealed record VersionDetailDto(
    string Id,
    string Content,
    DateTimeOffset CreatedAt,
    long Size,
    string Etag);

public sealed record RestoreVersionRequest(string? Etag);

public sealed record MediaToolRequest(string? Path);

public sealed record MediaToolResult(string Kind, string Text);

public sealed record VaultInfoDto(
    string Name,
    string Root,
    int NoteCount,
    int ProjectCount);
