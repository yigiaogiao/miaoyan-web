using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using MiaoYan.Web.Api;

namespace MiaoYan.Web.Tests;

public sealed class IngestCompatTests : IClassFixture<IngestCompatTests.CompatFixture>
{
    private readonly CompatFixture _vault;

    public IngestCompatTests(CompatFixture vault)
    {
        _vault = vault;
    }

    [Fact]
    public async Task Publish_CreatesManagedNoteAndListsFullBody()
    {
        var published = await _vault.PublishAsync(
            "ingest-example",
            "示例文章",
            "正文第一段。\n\n![](/api/media/photo.png)\n\n![[voice.m4a]]",
            tags: ["科技", "阅读"],
            pinned: true);

        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        Assert.NotNull(published.Headers.ETag);
        var note = await published.Content.ReadFromJsonAsync<NoteDto>();
        Assert.NotNull(note);
        Assert.Equal("示例文章", note.Title);
        Assert.Equal("ingest-example", note.ManagedId);
        Assert.True(note.Pinned);
        Assert.Contains("![](i/photo.png)", note.Body);
        Assert.Contains("![](files/voice.m4a)", note.Body);
        Assert.Contains("正文第一段", note.Content);
        Assert.Contains("id: \"ingest-example\"", note.Content);
        Assert.True(File.Exists(Path.Combine(_vault.Library, "示例文章.md")));

        var list = await _vault.Client.GetFromJsonAsync<List<NoteSummaryDto>>("/api/notes");
        Assert.NotNull(list);
        var listed = Assert.Single(list!, item => item.Id == note.Id);
        Assert.Equal("示例文章", listed.Title);
        Assert.Contains("正文第一段", listed.Body);
        Assert.Contains("![](i/photo.png)", listed.Body);
        Assert.Equal(new[] { "科技", "阅读" }, listed.Tags);
        Assert.True(listed.Pinned);
        Assert.Equal("note", listed.Kind);

        var summary = await _vault.Client.GetFromJsonAsync<List<NoteSummaryDto>>(
            "/api/notes?summary=1");
        Assert.NotNull(summary);
        Assert.All(summary!, item => Assert.Null(item.Body));
    }

    [Fact]
    public async Task Republish_ReusesManagedIdAndHonoursIfMatch()
    {
        var first = await _vault.PublishAsync(
            "ingest-republish",
            "重复发布",
            "第一篇正文。");
        first.EnsureSuccessStatusCode();
        var created = await first.Content.ReadFromJsonAsync<NoteDto>();
        Assert.NotNull(created);
        var etag = first.Headers.ETag!.Tag.Trim('"');

        var second = await _vault.Client.PutAsync(
            "/api/notes/ingest-republish",
            CompatFixture.JsonContent(new SaveNoteRequest(
                null,
                null,
                "第二篇正文。",
                ["更新"],
                "#ff8800",
                "note",
                false,
                true)));
        second.EnsureSuccessStatusCode();
        var updated = await second.Content.ReadFromJsonAsync<NoteDto>();
        Assert.NotNull(updated);
        Assert.Equal(created.Id, updated.Id);
        Assert.Equal("ingest-republish", updated.ManagedId);
        Assert.Equal("第二篇正文。", updated.Body);
        Assert.True(updated.Archived);
        Assert.Equal(new[] { "更新" }, updated.Tags);
        Assert.False(updated.Pinned);

        var list = await _vault.Client.GetFromJsonAsync<List<NoteSummaryDto>>("/api/notes");
        Assert.NotNull(list);
        Assert.Single(list!, item => item.ManagedId == "ingest-republish");

        var stale = await _vault.PutWithIfMatchAsync(
            "ingest-republish",
            new SaveNoteRequest(null, null, "过期写入。", [], null, "note", false, false),
            etag);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        Assert.NotNull(stale.Headers.ETag);
        var conflict = await stale.Content.ReadFromJsonAsync<ConflictDto>();
        Assert.NotNull(conflict);
        Assert.True(File.Exists(Path.Combine(_vault.Library, conflict.ConflictPath)));

        var current = await _vault.Client.GetFromJsonAsync<NoteDto>("/api/notes/ingest-republish");
        Assert.NotNull(current);
        Assert.Equal("第二篇正文。", current.Body);
    }

    [Fact]
    public async Task ManagedId_SurvivesNativeRenameAndProjectMove()
    {
        var published = await _vault.PublishAsync(
            "ingest-stable-id",
            "稳定标识",
            "重命名后仍然可以按旧 id 访问。");
        published.EnsureSuccessStatusCode();
        var note = await published.Content.ReadFromJsonAsync<NoteDto>();
        Assert.NotNull(note);

        var projectResponse = await _vault.Client.PostAsJsonAsync(
            "/api/projects",
            new CreateProjectRequest("入库项目"));
        projectResponse.EnsureSuccessStatusCode();
        var project = await projectResponse.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(project);

        var moved = await _vault.Client.PatchAsJsonAsync(
            $"/api/notes/{note.Id}",
            new RenameNoteRequest("稳定标识改名", project.Id));
        moved.EnsureSuccessStatusCode();
        var movedNote = await moved.Content.ReadFromJsonAsync<NoteDto>();
        Assert.NotNull(movedNote);
        Assert.NotEqual(note.Id, movedNote.Id);
        Assert.Equal("入库项目", movedNote.ProjectName);

        var byManagedId = await _vault.Client.GetFromJsonAsync<NoteDto>("/api/notes/ingest-stable-id");
        Assert.NotNull(byManagedId);
        Assert.Equal(movedNote.Id, byManagedId.Id);
        Assert.Equal(project.Id, byManagedId.ProjectId);

        var republish = await _vault.PutWithIfMatchAsync(
            "ingest-stable-id",
            new SaveNoteRequest(
                null,
                null,
                "移动之后再次发布。",
                ["入库"],
                null,
                "note",
                false,
                false),
            byManagedId.Etag);
        republish.EnsureSuccessStatusCode();
        var final = await republish.Content.ReadFromJsonAsync<NoteDto>();
        Assert.NotNull(final);
        Assert.Equal(movedNote.Id, final.Id);
        Assert.Equal("入库项目", final.ProjectName);
        Assert.Contains("移动之后再次发布", final.Body);
    }

    [Fact]
    public async Task Media_UploadDeduplicatesAndServesHeadAndGet()
    {
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

        var upload = await _vault.UploadAsync(png, "封面图.png", "image/png");
        upload.EnsureSuccessStatusCode();
        var media = await upload.Content.ReadFromJsonAsync<IngestMediaDto>();
        Assert.NotNull(media);
        Assert.Equal("image", media.Kind);
        Assert.Equal("image/png", media.Mime);
        Assert.Contains("-", media.Filename);
        Assert.StartsWith("/api/media/", media.Url);
        Assert.StartsWith("![](i/", media.Markdown);
        Assert.True(File.Exists(Path.Combine(_vault.Library, "i", media.Filename)));

        var head = await _vault.Client.SendAsync(new HttpRequestMessage(HttpMethod.Head, media.Url));
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Equal((long)png.Length, head.Content.Headers.ContentLength);

        var get = await _vault.Client.GetAsync(media.Url);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal("image/png", get.Content.Headers.ContentType?.MediaType);
        Assert.Equal(png, await get.Content.ReadAsByteArrayAsync());

        var again = await _vault.UploadAsync(png, "另一个名字.png", "image/png");
        again.EnsureSuccessStatusCode();
        var duplicate = await again.Content.ReadFromJsonAsync<IngestMediaDto>();
        Assert.NotNull(duplicate);
        Assert.Equal(media.Filename, duplicate.Filename);

        var rejected = await _vault.UploadAsync(Encoding.UTF8.GetBytes("MZ"), "evil.exe", "application/octet-stream");
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, rejected.StatusCode);

        var traversal = await _vault.Client.GetAsync("/api/media/..%2F..%2Fetc%2Fpasswd");
        Assert.True(traversal.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Categories_UpsertAndUnionNoteTags()
    {
        await _vault.PublishAsync("ingest-category", "分类文章", "正文。", tags: ["数据库", "设计"]);

        var created = await _vault.Client.PostAsJsonAsync(
            "/api/categories",
            new IngestCategoryRequest("数据库", "#00aa66"));
        created.EnsureSuccessStatusCode();
        var category = await created.Content.ReadFromJsonAsync<IngestCategoryDto>();
        Assert.NotNull(category);
        Assert.Equal("数据库", category.Name);
        Assert.Equal("#00aa66", category.Color);
        Assert.Equal(1, category.Count);

        var empty = await _vault.Client.PostAsJsonAsync(
            "/api/categories",
            new IngestCategoryRequest("稍后使用", null));
        empty.EnsureSuccessStatusCode();

        var list = await _vault.Client.GetFromJsonAsync<List<IngestCategoryDto>>("/api/categories");
        Assert.NotNull(list);
        Assert.Equal(0, Assert.Single(list!, item => item.Name == "稍后使用").Count);
        var design = Assert.Single(list!, item => item.Name == "设计");
        Assert.Equal(1, design.Count);
        Assert.Null(design.Color);
        Assert.Equal("#00aa66", Assert.Single(list!, item => item.Name == "数据库").Color);
    }

    [Fact]
    public async Task Publish_PreservesForeignFrontmatterKeys()
    {
        var published = await _vault.PublishAsync(
            "ingest-frontmatter",
            "保留元数据",
            "初始正文。");
        published.EnsureSuccessStatusCode();
        var note = await published.Content.ReadFromJsonAsync<NoteDto>();
        Assert.NotNull(note);

        var path = Path.Combine(_vault.Library, note.Path);
        var content = await File.ReadAllTextAsync(path);
        content = content.Replace(
            "id: \"ingest-frontmatter\"",
            "id: \"ingest-frontmatter\"\naliases: [旧链接]\ncustom_key: keep-me",
            StringComparison.Ordinal);
        await File.WriteAllTextAsync(path, content);

        var republish = await _vault.PutWithIfMatchAsync(
            "ingest-frontmatter",
            new SaveNoteRequest(
                null,
                null,
                "更新正文。",
                ["元数据"],
                null,
                "note",
                false,
                false),
            null);
        republish.EnsureSuccessStatusCode();

        var onDisk = await File.ReadAllTextAsync(path);
        Assert.Contains("aliases: [旧链接]", onDisk);
        Assert.Contains("custom_key: keep-me", onDisk);
        Assert.Contains("更新正文。", onDisk);
    }

    [Fact]
    public async Task LongFrontmatterNote_DoesNotStallVaultScan()
    {
        var paragraphs = string.Join(
            "\n\n",
            Enumerable.Range(0, 300).Select(index =>
                $"第 {index} 段正文：中文内容用来把摘要撑到两千字节以上。"));
        var body = paragraphs + "\n\n---\n\n---\n\n尾部正文。";
        var published = await _vault.PublishAsync("ingest-long-scan", "长正文扫描", body);
        published.EnsureSuccessStatusCode();

        // The old frontmatter regex backtracked catastrophically on the 2 KB
        // excerpt, so this request would never come back.
        var list = await _vault.Client
            .GetFromJsonAsync<List<NoteSummaryDto>>("/api/notes")
            .WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(list);
        var listed = Assert.Single(list!, item => item.Title == "长正文扫描");
        Assert.StartsWith("第 0 段正文", listed.Body);
    }

    public sealed class CompatFixture : IDisposable
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        public CompatFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "miaoyan-ingest-tests", Guid.NewGuid().ToString("N"));
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

        public Task<HttpResponseMessage> PublishAsync(
            string id,
            string title,
            string body,
            IReadOnlyList<string>? tags = null,
            bool pinned = false,
            bool archived = false,
            string kind = "note",
            string? color = null)
        {
            var request = new SaveNoteRequest(
                null,
                null,
                body,
                tags ?? [],
                color,
                kind,
                pinned,
                archived,
                Title: title);
            return Client.PutAsync($"/api/notes/{Uri.EscapeDataString(id)}", JsonContent(request));
        }

        public Task<HttpResponseMessage> PutWithIfMatchAsync(
            string id,
            SaveNoteRequest request,
            string? etag)
        {
            var message = new HttpRequestMessage(HttpMethod.Put, $"/api/notes/{Uri.EscapeDataString(id)}")
            {
                Content = JsonContent(request),
            };
            if (!string.IsNullOrWhiteSpace(etag))
            {
                message.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{etag}\""));
            }

            return Client.SendAsync(message);
        }

        public async Task<HttpResponseMessage> UploadAsync(byte[] bytes, string name, string mime)
        {
            using var form = new MultipartFormDataContent();
            using var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue(mime);
            form.Add(file, "file", name);
            return await Client.PostAsync("/api/media/upload", form);
        }

        public static StringContent JsonContent(SaveNoteRequest request)
            => new(JsonSerializer.Serialize(request, Json), Encoding.UTF8, "application/json");

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
