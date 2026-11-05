using System.Net;
using System.Net.Http.Json;
using MiaoYan.Web.Api;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MiaoYan.Web.Tests;

/// <summary>
/// Exercises the LazyCat layout without a LazyCat host: document.private
/// mounts /lzcapp/documents/&lt;uid&gt; and the gateway passes the signed UID in
/// X-HC-User-ID. MIAOYAN_DOCUMENTS_ROOT accepts the same shape for self-hosting
/// and for this test.
/// </summary>
public sealed class DocumentsRootTests : IClassFixture<DocumentsRootTests.DocumentsFixture>
{
    private const string Alice = "alice-uid";
    private const string Bob = "bob-uid";
    private readonly DocumentsFixture _root;

    public DocumentsRootTests(DocumentsFixture root)
    {
        _root = root;
    }

    [Fact]
    public async Task UsersGetIsolatedLibrariesUnderDocumentsRoot()
    {
        using var alice = _root.ClientFor(Alice);
        var created = await alice.PostAsJsonAsync("/api/notes", new CreateNoteRequest(null, "Alice 的笔记"));
        created.EnsureSuccessStatusCode();
        var note = await created.Content.ReadFromJsonAsync<NoteDto>();
        Assert.NotNull(note);
        Assert.True(File.Exists(Path.Combine(_root.DocumentsRoot, Alice, "MiaoYan", note.Path)));

        using var bob = _root.ClientFor(Bob);
        var bobNotes = await bob.GetFromJsonAsync<List<NoteSummaryDto>>("/api/notes");
        Assert.Empty(bobNotes!);
        Assert.True(Directory.Exists(Path.Combine(_root.DocumentsRoot, Bob, "MiaoYan")));

        var aliceNotes = await alice.GetFromJsonAsync<List<NoteSummaryDto>>("/api/notes");
        Assert.Single(aliceNotes!);
        Assert.Equal("Alice 的笔记", aliceNotes![0].Title);
    }

    [Fact]
    public async Task MissingUserHeader_DistinguishesMultipleUsers()
    {
        using var anonymous = _root.Factory.CreateClient();
        var response = await anonymous.GetAsync("/api/notes");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UserSegmentCannotEscapeDocumentsRoot()
    {
        using var client = _root.ClientFor("../escape");
        var response = await client.GetAsync("/api/notes");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(Directory.Exists(Path.Combine(_root.Root, "escape")));
    }

    public sealed class DocumentsFixture : IDisposable
    {
        public DocumentsFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "miaoyan-docroot-tests", Guid.NewGuid().ToString("N"));
            DocumentsRoot = Path.Combine(Root, "documents");
            var state = Path.Combine(Root, "state");
            Directory.CreateDirectory(DocumentsRoot);
            Directory.CreateDirectory(state);

            Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Miaoyan:DocumentsRoot", DocumentsRoot);
                builder.UseSetting("Miaoyan:StateDir", state);
            });
            // Materialise both user folders so the anonymous request sees
            // multiple users and cannot pick one implicitly.
            Directory.CreateDirectory(Path.Combine(DocumentsRoot, Alice));
            Directory.CreateDirectory(Path.Combine(DocumentsRoot, Bob));
        }

        public string Root { get; }

        public string DocumentsRoot { get; }

        public WebApplicationFactory<Program> Factory { get; }

        public HttpClient ClientFor(string uid)
        {
            var client = Factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-HC-User-ID", uid);
            return client;
        }

        public void Dispose()
        {
            Factory.Dispose();
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
                // SQLite may still hold a handle briefly.
            }
        }
    }
}
