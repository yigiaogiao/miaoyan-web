using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MiaoYan.Web.Api;
using MiaoYan.Web.Services;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MiaoYan.Web.Tests;

public sealed class MediaToolsTests : IClassFixture<MediaToolsTests.ToolsFixture>
{
    private readonly ToolsFixture _tools;

    public MediaToolsTests(ToolsFixture tools)
    {
        _tools = tools;
    }

    [Fact]
    public async Task OcrReturnsTheCommandOutput()
    {
        var note = await _tools.CreateNoteAsync("OCR");
        var asset = await _tools.UploadAsync(note.Id, "scan.png", "image/png", "识别到的文字"u8.ToArray());

        var response = await _tools.Client.PostAsJsonAsync(
            $"/api/notes/{note.Id}/ocr",
            new MediaToolRequest($"i/{asset.Name}"));

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<MediaToolResult>();
        Assert.NotNull(result);
        Assert.Equal("ocr", result!.Kind);
        Assert.Contains("识别到的文字", result.Text);
    }

    [Fact]
    public async Task TranscriptionReturnsTheCommandOutput()
    {
        var note = await _tools.CreateNoteAsync("转写");
        var asset = await _tools.UploadAsync(note.Id, "voice.m4a", "audio/mp4", [1, 2, 3]);

        var response = await _tools.Client.PostAsJsonAsync(
            $"/api/notes/{note.Id}/transcribe",
            new MediaToolRequest($"files/{asset.Name}"));

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<MediaToolResult>();
        Assert.NotNull(result);
        Assert.Equal("transcribe", result!.Kind);
        Assert.Equal("转写后的文字", result.Text);
    }

    [Fact]
    public async Task WrongMediaTypeIsRejected()
    {
        var note = await _tools.CreateNoteAsync("类型");
        var asset = await _tools.UploadAsync(note.Id, "voice.m4a", "audio/mp4", [1, 2, 3]);

        var response = await _tools.Client.PostAsJsonAsync(
            $"/api/notes/{note.Id}/ocr",
            new MediaToolRequest($"files/{asset.Name}"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TraversalOutsideTheVaultIsRejected()
    {
        var note = await _tools.CreateNoteAsync("越界");
        var response = await _tools.Client.PostAsJsonAsync(
            $"/api/notes/{note.Id}/ocr",
            new MediaToolRequest("i/../../outside.png"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AssetUploadSanitizesMarkdownBreakingNames()
    {
        var note = await _tools.CreateNoteAsync("图片");
        var asset = await _tools.UploadAsync(
            note.Id, "image 1 (copy).png", "image/png", [0x89, 0x50]);

        Assert.Equal("image-1-copy.png", asset.Name);
        Assert.Equal("![](i/image-1-copy.png)", asset.Markdown);
        Assert.DoesNotContain(" ", asset.Markdown);

        var served = await _tools.Client.GetAsync(asset.Url);
        served.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task DuplicateAssetNamesKeepMarkdownSafeSuffixes()
    {
        var note = await _tools.CreateNoteAsync("同名图片");
        var first = await _tools.UploadAsync(note.Id, "image 1.png", "image/png", [0x89, 0x50]);
        var second = await _tools.UploadAsync(note.Id, "image 1.png", "image/png", [0x89, 0x50, 0x00]);

        Assert.Equal("image-1.png", first.Name);
        Assert.Equal("image-1-1.png", second.Name);
        Assert.DoesNotContain(" ", second.Markdown);
    }

    [Fact]
    public async Task UnusedAssetsKeepsLegacySpacedMarkdownReferences()
    {
        var note = await _tools.CreateNoteAsync("旧图片");
        var directory = Path.Combine(_tools.Library, "i");
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory, "image 1.png"), [0x89, 0x50]);

        var saved = await _tools.Client.PutAsJsonAsync(
            $"/api/notes/{note.Id}",
            new SaveNoteRequest("![](i/image 1.png)\n", note.Etag));
        saved.EnsureSuccessStatusCode();

        var scan = await _tools.Client.GetFromJsonAsync<UnusedAssetsResult>(
            "/api/maintenance/unused-assets");
        Assert.NotNull(scan);
        Assert.DoesNotContain("i/image 1.png", scan!.Files);
    }

    [Fact]
    public async Task MissingConfigurationReturns503()
    {
        using var plain = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            var root = Path.Combine(Path.GetTempPath(), "miaoyan-tools-plain", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "library"));
            Directory.CreateDirectory(Path.Combine(root, "state"));
            builder.UseSetting("Miaoyan:Library", Path.Combine(root, "library"));
            builder.UseSetting("Miaoyan:StateDir", Path.Combine(root, "state"));
        });

        using var client = plain.CreateClient();
        var note = await client.PostAsJsonAsync("/api/notes", new CreateNoteRequest(null, "plain"));
        note.EnsureSuccessStatusCode();
        var created = await note.Content.ReadFromJsonAsync<NoteDto>();

        var response = await client.PostAsJsonAsync(
            $"/api/notes/{created!.Id}/ocr",
            new MediaToolRequest("i/missing.png"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    public sealed class ToolsFixture : IDisposable
    {
        public ToolsFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "miaoyan-tools-tests", Guid.NewGuid().ToString("N"));
            Library = Path.Combine(Root, "library");
            var state = Path.Combine(Root, "state");
            Directory.CreateDirectory(Library);
            Directory.CreateDirectory(state);

            Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Miaoyan:Library", Library);
                builder.UseSetting("Miaoyan:StateDir", state);
                // The configured command is admin-supplied; tests use sh so
                // the placeholder substitution can be observed.
                builder.UseSetting("Miaoyan:OcrCommand", "sh -c \"cat {input}\"");
                builder.UseSetting("Miaoyan:WhisperCommand", "sh -c \"printf '转写后的文字'\"");
            });
            Client = Factory.CreateClient();
        }

        public string Root { get; }
        public string Library { get; }
        public WebApplicationFactory<Program> Factory { get; }
        public HttpClient Client { get; }

        public async Task<NoteDto> CreateNoteAsync(string title)
        {
            var response = await Client.PostAsJsonAsync("/api/notes", new CreateNoteRequest(null, title));
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<NoteDto>())!;
        }

        public async Task<AssetDto> UploadAsync(string noteId, string name, string contentType, byte[] bytes)
        {
            using var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
            form.Add(file, "file", name);
            var response = await Client.PostAsync($"/api/notes/{noteId}/assets", form);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<AssetDto>())!;
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
            }
        }
    }
}
