using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using MiaoYan.Web.Api;

namespace MiaoYan.Web.Tests;

public sealed class VaultApiTests : IClassFixture<VaultApiTests.VaultFixture>
{
    private readonly VaultFixture _vault;

    public VaultApiTests(VaultFixture vault)
    {
        _vault = vault;
    }

    [Fact]
    public async Task CreateNote_UsesUuidFileNameAndUntitledTitle()
    {
        var note = await _vault.CreateNoteAsync();

        Assert.Equal("Untitled Note", note.Title);
        Assert.True(note.IsUntitled);
        Assert.Matches("^[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}\\.md$", note.Path);
        Assert.True(File.Exists(Path.Combine(_vault.Library, note.Path)));
    }

    [Fact]
    public async Task RenameNote_MapsSlashToColonAndRejectsDuplicateDestination()
    {
        var note = await _vault.CreateNoteAsync();
        var response = await _vault.Client.PatchAsJsonAsync(
            $"/api/notes/{note.Id}",
            new RenameNoteRequest("会议/周报", null));

        response.EnsureSuccessStatusCode();
        var renamed = await response.Content.ReadFromJsonAsync<NoteDto>();
        Assert.NotNull(renamed);
        Assert.Equal("会议/周报", renamed.Title);
        Assert.Equal("会议:周报.md", renamed.Path);
        Assert.True(File.Exists(Path.Combine(_vault.Library, renamed.Path)));

        var other = await _vault.CreateNoteAsync();
        var conflict = await _vault.Client.PatchAsJsonAsync(
            $"/api/notes/{other.Id}",
            new RenameNoteRequest("会议/周报", null));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
    }

    [Fact]
    public async Task SaveNote_StaleEtag_Returns412AndWritesConflictBackup()
    {
        var note = await _vault.CreateNoteAsync();
        var first = await _vault.Client.PutAsJsonAsync(
            $"/api/notes/{note.Id}",
            new SaveNoteRequest("version one", note.Etag));
        first.EnsureSuccessStatusCode();
        var saved = await first.Content.ReadFromJsonAsync<NoteDto>();
        Assert.NotNull(saved);

        var stale = await _vault.Client.PutAsJsonAsync(
            $"/api/notes/{note.Id}",
            new SaveNoteRequest("version two", note.Etag));

        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        var conflict = await stale.Content.ReadFromJsonAsync<ConflictDto>();
        Assert.NotNull(conflict);
        Assert.Equal("conflict", conflict.Error);
        Assert.Equal(saved.Etag, conflict.CurrentEtag);
        Assert.Equal("version one", conflict.CurrentContent);
        Assert.True(File.Exists(Path.Combine(_vault.Library, conflict.ConflictPath)));

        var onDisk = await File.ReadAllTextAsync(Path.Combine(_vault.Library, saved.Path));
        Assert.Equal("version one", onDisk);
    }

    [Fact]
    public async Task DeleteAndRestore_RoundTripsContent()
    {
        var note = await _vault.CreateNoteAsync();
        var saved = await _vault.Client.PutAsJsonAsync(
            $"/api/notes/{note.Id}",
            new SaveNoteRequest("# 回收站测试\n\n内容保留。", note.Etag));
        saved.EnsureSuccessStatusCode();

        var deleted = await _vault.Client.DeleteAsync($"/api/notes/{note.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _vault.Client.GetAsync($"/api/notes/{note.Id}")).StatusCode);

        var trash = await _vault.Client.GetFromJsonAsync<List<TrashEntryDto>>("/api/trash");
        var entry = Assert.Single(trash!, item => item.Kind == "note" && item.Title == "Untitled Note");

        var restore = await _vault.Client.PostAsync($"/api/trash/{entry.Id}/restore", null);
        restore.EnsureSuccessStatusCode();
        var restored = await restore.Content.ReadFromJsonAsync<TrashRestoreResultDto>();
        Assert.NotNull(restored);
        Assert.NotNull(restored.NoteId);

        var noteAfter = await _vault.Client.GetFromJsonAsync<NoteDto>($"/api/notes/{restored.NoteId}");
        Assert.NotNull(noteAfter);
        Assert.Contains("回收站测试", noteAfter.Content);
    }

    [Fact]
    public async Task UploadImage_StoresUnderImagesFolderAndServesIt()
    {
        var note = await _vault.CreateNoteAsync();
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

        using var form = new MultipartFormDataContent();
        using var file = new ByteArrayContent(png);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "截图.png");

        var response = await _vault.Client.PostAsync($"/api/notes/{note.Id}/assets", form);
        response.EnsureSuccessStatusCode();
        var asset = await response.Content.ReadFromJsonAsync<AssetDto>();
        Assert.NotNull(asset);
        Assert.Equal("image", asset.Kind);
        Assert.StartsWith("![", asset.Markdown);
        Assert.Contains("(i/", asset.Markdown);
        Assert.True(File.Exists(Path.Combine(_vault.Library, "i", "截图.png")));

        var download = await _vault.Client.GetAsync(asset.Url);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("image/png", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal(png, await download.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task AssetPathTraversal_IsRejected()
    {
        var note = await _vault.CreateNoteAsync();
        var response = await _vault.Client.GetAsync(
            $"/api/notes/{note.Id}/assets/..%2F..%2F..%2Fetc%2Fpasswd");

        Assert.True(
            response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound,
            $"Unexpected status {response.StatusCode}");
    }

    [Fact]
    public async Task ProjectLifecycle_CreateRenameDelete()
    {
        var created = await _vault.Client.PostAsJsonAsync("/api/projects", new CreateProjectRequest("项目测试"));
        created.EnsureSuccessStatusCode();
        var project = await created.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(project);
        Assert.Equal("项目测试", project.Name);
        Assert.True(Directory.Exists(Path.Combine(_vault.Library, "项目测试")));

        var renamed = await _vault.Client.PatchAsJsonAsync(
            $"/api/projects/{project.Id}",
            new RenameProjectRequest("项目测试-改名"));
        renamed.EnsureSuccessStatusCode();
        var renamedProject = await renamed.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(renamedProject);
        Assert.True(Directory.Exists(Path.Combine(_vault.Library, "项目测试-改名")));

        var deleted = await _vault.Client.DeleteAsync($"/api/projects/{renamedProject.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.False(Directory.Exists(Path.Combine(_vault.Library, "项目测试-改名")));
    }

    [Fact]
    public async Task CreateNoteInProject_AppearsInProjectList()
    {
        var created = await _vault.Client.PostAsJsonAsync("/api/projects", new CreateProjectRequest("项目笔记"));
        created.EnsureSuccessStatusCode();
        var project = await created.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(project);

        var note = await _vault.CreateNoteAsync(project.Id);
        Assert.Equal(project.Id, note.ProjectId);

        var again = await _vault.Client.GetFromJsonAsync<List<ProjectDto>>("/api/projects");
        var listed = Assert.Single(again!, item => item.Id == project.Id);
        Assert.Equal(1, listed.NoteCount);
    }

    public sealed record TrashRestoreResultDto(string Kind, string? NoteId, string? ProjectId);

    public sealed class VaultFixture : IDisposable
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        public VaultFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "miaoyan-api-tests", Guid.NewGuid().ToString("N"));
            Library = Path.Combine(Root, "library");
            State = Path.Combine(Root, "state");
            Directory.CreateDirectory(Library);
            Directory.CreateDirectory(State);

            Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Miaoyan:Library", Library);
                builder.UseSetting("Miaoyan:StateDir", State);
            });
            Client = Factory.CreateClient();
        }

        public string Root { get; }

        public string Library { get; }

        public string State { get; }

        public WebApplicationFactory<Program> Factory { get; }

        public HttpClient Client { get; }

        public async Task<NoteDto> CreateNoteAsync(string projectId = "root")
        {
            var response = await Client.PostAsJsonAsync("/api/notes", new CreateNoteRequest(projectId, null));
            response.EnsureSuccessStatusCode();
            var note = await response.Content.ReadFromJsonAsync<NoteDto>(Json);
            Assert.NotNull(note);
            return note;
        }

        public void Dispose()
        {
            Client.Dispose();
            Factory.Dispose();
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
                // SQLite may hold the file briefly on some platforms.
            }
        }
    }
}
