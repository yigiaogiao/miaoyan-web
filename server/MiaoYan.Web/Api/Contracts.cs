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
    bool IsUntitled);

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
    bool HasBom);

public sealed record SaveNoteRequest(string? Content, string? Etag);

public sealed record RenameNoteRequest(string? Title, string? ProjectId);

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

public sealed record VaultInfoDto(
    string Name,
    string Root,
    int NoteCount,
    int ProjectCount);
