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
    bool Pinned);

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
    bool HasBom);

public sealed record SaveNoteRequest(string? Content, string? Etag);

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
    bool IsUntitled);

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

public sealed record VaultInfoDto(
    string Name,
    string Root,
    int NoteCount,
    int ProjectCount);
