using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using MiaoYan.Web.Api;
using MiaoYan.Web.Services;

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
    public async Task Pin_SurvivesRenameAndCanBeCleared()
    {
        var note = await _vault.CreateNoteAsync();
        var renamed = await _vault.Client.PatchAsJsonAsync(
            $"/api/notes/{note.Id}",
            new RenameNoteRequest("置顶测试", null));
        renamed.EnsureSuccessStatusCode();
        var target = await renamed.Content.ReadFromJsonAsync<NoteDto>();
        Assert.NotNull(target);

        var pinned = await _vault.Client.PatchAsJsonAsync(
            $"/api/notes/{target.Id}",
            new RenameNoteRequest(null, null, true));
        pinned.EnsureSuccessStatusCode();

        var list = await _vault.Client.GetFromJsonAsync<List<NoteSummaryDto>>("/api/notes?project=root");
        Assert.NotNull(list);
        Assert.True(Assert.Single(list!, item => item.Id == target.Id).Pinned);
        Assert.Equal(target.Id, list![0].Id);

        var moved = await _vault.Client.PatchAsJsonAsync(
            $"/api/notes/{target.Id}",
            new RenameNoteRequest("置顶测试改名", null));
        moved.EnsureSuccessStatusCode();
        var movedNote = await moved.Content.ReadFromJsonAsync<NoteDto>();
        Assert.NotNull(movedNote);
        Assert.NotEqual(target.Id, movedNote.Id);

        list = await _vault.Client.GetFromJsonAsync<List<NoteSummaryDto>>("/api/notes?project=root");
        Assert.NotNull(list);
        Assert.True(Assert.Single(list!, item => item.Id == movedNote.Id).Pinned);

        var unpinned = await _vault.Client.PatchAsJsonAsync(
            $"/api/notes/{movedNote.Id}",
            new RenameNoteRequest(null, null, false));
        unpinned.EnsureSuccessStatusCode();
        list = await _vault.Client.GetFromJsonAsync<List<NoteSummaryDto>>("/api/notes?project=root");
        Assert.NotNull(list);
        Assert.False(Assert.Single(list!, item => item.Id == movedNote.Id).Pinned);
    }

    [Fact]
    public async Task Duplicate_CopiesContentAndUsesCopyNaming()
    {
        var note = await _vault.CreateNoteAsync();
        var renamed = await _vault.Client.PatchAsJsonAsync(
            $"/api/notes/{note.Id}",
            new RenameNoteRequest("复制源", null));
        renamed.EnsureSuccessStatusCode();
        var source = await renamed.Content.ReadFromJsonAsync<NoteDto>();
        Assert.NotNull(source);

        var saved = await _vault.Client.PutAsJsonAsync(
            $"/api/notes/{source.Id}",
            new SaveNoteRequest("# 复制源\n\n正文保持不变。", source.Etag));
        saved.EnsureSuccessStatusCode();

        var firstCopy = await _vault.Client.PostAsync($"/api/notes/{source.Id}/duplicate", null);
        Assert.Equal(HttpStatusCode.Created, firstCopy.StatusCode);
        var copy = await firstCopy.Content.ReadFromJsonAsync<NoteDto>();
        Assert.NotNull(copy);
        Assert.Equal("复制源 Copy", copy.Title);
        Assert.Equal("# 复制源\n\n正文保持不变。", copy.Content);
        Assert.NotEqual(source.Path, copy.Path);

        var secondCopy = await _vault.Client.PostAsync($"/api/notes/{source.Id}/duplicate", null);
        secondCopy.EnsureSuccessStatusCode();
        var copy2 = await secondCopy.Content.ReadFromJsonAsync<NoteDto>();
        Assert.NotNull(copy2);
        Assert.Equal("复制源 Copy 2", copy2.Title);
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
    public async Task Wikilinks_ResolveBacklinksAndReportUnresolved()
    {
        var target = await _vault.CreateNoteAsync();
        var renameTarget = await _vault.Client.PatchAsJsonAsync(
            $"/api/notes/{target.Id}",
            new RenameNoteRequest("链接目标", null));
        renameTarget.EnsureSuccessStatusCode();
        var targetNote = await renameTarget.Content.ReadFromJsonAsync<NoteDto>();
        Assert.NotNull(targetNote);

        var source = await _vault.CreateNoteAsync();
        var body = """
            # 来源

            正常链接 [[链接目标]]，大小写 [[链接目标]]。
            未解析 [[还没写的笔记]]。

            ```md
            [[代码块里的链接不算]]
            ```

            行内 `[[行内代码不算]]` 也应忽略。
            """;
        var saved = await _vault.Client.PutAsJsonAsync(
            $"/api/notes/{source.Id}",
            new SaveNoteRequest(body, source.Etag));
        saved.EnsureSuccessStatusCode();

        var index = await _vault.Client.GetFromJsonAsync<LinkIndexDto>("/api/links");
        Assert.NotNull(index);
        Assert.Contains(index.Targets, item => item.Title == "链接目标" && item.NoteId == targetNote.Id);
        Assert.Contains(index.Unresolved, item => item.Title == "还没写的笔记" && item.Count == 1);
        Assert.DoesNotContain(index.Unresolved, item => item.Title == "代码块里的链接不算");
        Assert.DoesNotContain(index.Unresolved, item => item.Title == "行内代码不算");

        var backlinks = await _vault.Client.GetFromJsonAsync<List<BacklinkDto>>(
            $"/api/notes/{targetNote.Id}/backlinks");
        Assert.NotNull(backlinks);
        var backlink = Assert.Single(backlinks!);
        Assert.Equal(source.Id, backlink.NoteId);
        Assert.Equal(3, backlink.Line);
        Assert.Contains("正常链接", backlink.Excerpt);

        // Renaming the source keeps the backlink alive under its new id/title.
        var renamedSource = await _vault.Client.PatchAsJsonAsync(
            $"/api/notes/{source.Id}",
            new RenameNoteRequest("来源改名", null));
        renamedSource.EnsureSuccessStatusCode();
        var renamedSourceNote = await renamedSource.Content.ReadFromJsonAsync<NoteDto>();
        Assert.NotNull(renamedSourceNote);
        backlinks = await _vault.Client.GetFromJsonAsync<List<BacklinkDto>>(
            $"/api/notes/{targetNote.Id}/backlinks");
        Assert.NotNull(backlinks);
        backlink = Assert.Single(backlinks!);
        Assert.Equal(renamedSourceNote.Id, backlink.NoteId);
        Assert.Equal("来源改名", backlink.Title);

        // Renaming the target does not rewrite other notes: old links become
        // unresolved and the new title has no backlinks yet.
        var renamedTarget = await _vault.Client.PatchAsJsonAsync(
            $"/api/notes/{targetNote.Id}",
            new RenameNoteRequest("链接目标改名", null));
        renamedTarget.EnsureSuccessStatusCode();
        var renamedTargetNote = await renamedTarget.Content.ReadFromJsonAsync<NoteDto>();
        Assert.NotNull(renamedTargetNote);
        index = await _vault.Client.GetFromJsonAsync<LinkIndexDto>("/api/links");
        Assert.NotNull(index);
        Assert.Contains(index.Targets, item => item.Title == "链接目标改名" && item.NoteId == renamedTargetNote.Id);
        Assert.Contains(index.Unresolved, item => item.Title == "链接目标");
        backlinks = await _vault.Client.GetFromJsonAsync<List<BacklinkDto>>(
            $"/api/notes/{renamedTargetNote.Id}/backlinks");
        Assert.NotNull(backlinks);
        Assert.Empty(backlinks!);
    }

    [Fact]
    public async Task Search_UsesFtsForLongQueriesAndFallsBackForShortChinese()
    {
        var note = await _vault.CreateNoteAsync();
        var renamed = await _vault.Client.PatchAsJsonAsync(
            $"/api/notes/{note.Id}",
            new RenameNoteRequest("全文搜索目标", null));
        renamed.EnsureSuccessStatusCode();
        var target = await renamed.Content.ReadFromJsonAsync<NoteDto>();
        Assert.NotNull(target);

        var body = "# 搜索测试\n\n这是一段关于量子纠缠咖啡的正文，只出现在这一篇里。\n";
        var saved = await _vault.Client.PutAsJsonAsync(
            $"/api/notes/{target.Id}",
            new SaveNoteRequest(body, target.Etag));
        saved.EnsureSuccessStatusCode();

        var titled = await _vault.CreateNoteAsync();
        var titledRename = await _vault.Client.PatchAsJsonAsync(
            $"/api/notes/{titled.Id}",
            new RenameNoteRequest("量子纠缠", null));
        titledRename.EnsureSuccessStatusCode();

        var fts = await _vault.Client.GetFromJsonAsync<List<SearchHitDto>>(
            "/api/search?q=" + Uri.EscapeDataString("量子纠缠"));
        Assert.NotNull(fts);
        var hit = Assert.Single(fts!, item => item.Id == target.Id);
        Assert.Contains("量子纠缠", hit.Snippet);
        Assert.Equal("量子纠缠", fts[0].Title);

        var shortTitled = await _vault.CreateNoteAsync();
        var shortRename = await _vault.Client.PatchAsJsonAsync(
            $"/api/notes/{shortTitled.Id}",
            new RenameNoteRequest("咖啡", null));
        shortRename.EnsureSuccessStatusCode();

        var shortQuery = await _vault.Client.GetFromJsonAsync<List<SearchHitDto>>(
            "/api/search?q=" + Uri.EscapeDataString("咖啡"));
        Assert.NotNull(shortQuery);
        Assert.Contains(shortQuery!, item => item.Id == target.Id);
        Assert.Equal("咖啡", shortQuery[0].Title);

        var wildcard = await _vault.Client.GetFromJsonAsync<List<SearchHitDto>>(
            "/api/search?q=" + Uri.EscapeDataString("%"));
        Assert.NotNull(wildcard);
        Assert.DoesNotContain(wildcard!, item => item.Id == target.Id);

        var list = await _vault.Client.GetFromJsonAsync<List<NoteSummaryDto>>(
            "/api/notes?q=" + Uri.EscapeDataString("量子纠缠"));
        Assert.NotNull(list);
        Assert.Contains(list!, item => item.Id == target.Id);

        var otherProject = await _vault.Client.PostAsJsonAsync("/api/projects", new CreateProjectRequest("搜索隔离"));
        otherProject.EnsureSuccessStatusCode();
        var project = await otherProject.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(project);
        var scoped = await _vault.Client.GetFromJsonAsync<List<SearchHitDto>>(
            "/api/search?q=" + Uri.EscapeDataString("量子纠缠") + "&project=" + project.Id);
        Assert.NotNull(scoped);
        Assert.DoesNotContain(scoped!, item => item.Id == target.Id);
    }

    [Fact]
    public async Task Versions_ListRestoreAndSurviveRename()
    {
        var note = await _vault.CreateNoteAsync();
        var first = await _vault.Client.PutAsJsonAsync(
            $"/api/notes/{note.Id}",
            new SaveNoteRequest("第一版内容", note.Etag));
        first.EnsureSuccessStatusCode();
        var v1 = await first.Content.ReadFromJsonAsync<NoteDto>();
        Assert.NotNull(v1);

        var second = await _vault.Client.PutAsJsonAsync(
            $"/api/notes/{note.Id}",
            new SaveNoteRequest("第二版内容", v1.Etag));
        second.EnsureSuccessStatusCode();
        var v2 = await second.Content.ReadFromJsonAsync<NoteDto>();
        Assert.NotNull(v2);

        var versions = await _vault.Client.GetFromJsonAsync<List<VersionDto>>($"/api/notes/{note.Id}/versions");
        Assert.NotNull(versions);
        Assert.Equal(3, versions!.Count);
        Assert.True(
            versions[0].IsCurrent,
            $"current={v2.Etag}; versions={string.Join(" | ", versions.Select(v => $"{v.Id}:{v.Etag}:{v.IsCurrent}"))}");
        Assert.Equal("第二版内容", File.ReadAllText(Path.Combine(_vault.Library, v2.Path)));

        var initial = await _vault.Client.GetFromJsonAsync<VersionDetailDto>(
            $"/api/notes/{note.Id}/versions/{versions[^1].Id}");
        Assert.NotNull(initial);
        Assert.NotEqual("第一版内容", initial!.Content);

        var old = versions[^2];
        var detail = await _vault.Client.GetFromJsonAsync<VersionDetailDto>(
            $"/api/notes/{note.Id}/versions/{old.Id}");
        Assert.NotNull(detail);
        Assert.Equal("第一版内容", detail!.Content);

        var restored = await _vault.Client.PostAsJsonAsync(
            $"/api/notes/{note.Id}/versions/{old.Id}/restore",
            new RestoreVersionRequest(v2.Etag));
        restored.EnsureSuccessStatusCode();
        var restoredNote = await restored.Content.ReadFromJsonAsync<NoteDto>();
        Assert.NotNull(restoredNote);
        Assert.Equal("第一版内容", restoredNote.Content);

        var stale = await _vault.Client.PostAsJsonAsync(
            $"/api/notes/{note.Id}/versions/{versions[0].Id}/restore",
            new RestoreVersionRequest(v2.Etag));
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);

        var renamed = await _vault.Client.PatchAsJsonAsync(
            $"/api/notes/{restoredNote.Id}",
            new RenameNoteRequest("带版本的笔记", null));
        renamed.EnsureSuccessStatusCode();
        var renamedNote = await renamed.Content.ReadFromJsonAsync<NoteDto>();
        Assert.NotNull(renamedNote);
        var afterRename = await _vault.Client.GetFromJsonAsync<List<VersionDto>>(
            $"/api/notes/{renamedNote.Id}/versions");
        Assert.NotNull(afterRename);
        Assert.True(afterRename!.Count >= 2);
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

    [Fact]
    public async Task ExportVault_ReturnsZipAndSkipsDotDirectories()
    {
        var note = await _vault.CreateNoteAsync();
        var renamed = await _vault.Client.PatchAsJsonAsync(
            $"/api/notes/{note.Id}", new RenameNoteRequest("导出测试", null));
        renamed.EnsureSuccessStatusCode();

        var hidden = Path.Combine(_vault.Library, ".miaoyan", "trash");
        Directory.CreateDirectory(hidden);
        await File.WriteAllTextAsync(Path.Combine(hidden, "deleted.md"), "trashed");

        var response = await _vault.Client.GetAsync("/api/export/vault");
        response.EnsureSuccessStatusCode();
        Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        Assert.Contains(archive.Entries, entry => entry.FullName == "导出测试.md");
        Assert.DoesNotContain(archive.Entries, entry => entry.FullName.StartsWith(".miaoyan/"));
    }

    [Fact]
    public async Task CleanUnusedAssets_MovesOnlyUnreferencedFiles()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var note = await _vault.CreateNoteAsync();
        var renamed = await _vault.Client.PatchAsJsonAsync(
            $"/api/notes/{note.Id}", new RenameNoteRequest($"附件清理-{suffix}", null));
        renamed.EnsureSuccessStatusCode();
        var target = await renamed.Content.ReadFromJsonAsync<NoteDto>();
        Assert.NotNull(target);

        var directory = Path.Combine(_vault.Library, "i");
        Directory.CreateDirectory(directory);
        var usedName = $"cleanup-used-{suffix}.png";
        var orphanName = $"cleanup-orphan-{suffix}.png";
        await File.WriteAllBytesAsync(Path.Combine(directory, usedName), [0x89, 0x50]);
        await File.WriteAllBytesAsync(Path.Combine(directory, orphanName), [0x89, 0x50]);

        var saved = await _vault.Client.PutAsJsonAsync(
            $"/api/notes/{target.Id}",
            new SaveNoteRequest($"![](i/{usedName})\n", target.Etag));
        saved.EnsureSuccessStatusCode();

        var scan = await _vault.Client.GetFromJsonAsync<UnusedAssetsResult>(
            "/api/maintenance/unused-assets");
        Assert.NotNull(scan);
        Assert.Contains($"i/{orphanName}", scan!.Files);
        Assert.DoesNotContain($"i/{usedName}", scan.Files);

        var clean = await _vault.Client.PostAsync("/api/maintenance/unused-assets/clean", null);
        clean.EnsureSuccessStatusCode();
        var result = await clean.Content.ReadFromJsonAsync<CleanUnusedAssetsResult>();
        Assert.NotNull(result);
        Assert.Contains($"i/{orphanName}", result!.Removed);

        Assert.False(File.Exists(Path.Combine(directory, orphanName)));
        Assert.True(File.Exists(Path.Combine(directory, usedName)));
        Assert.NotEmpty(Directory.EnumerateFiles(
            Path.Combine(_vault.Library, ".miaoyan", "trash-assets"),
            orphanName,
            SearchOption.AllDirectories));
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
