using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MiaoYan.Web.Tests;

public sealed class HealthTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "miaoyan-tests", Guid.NewGuid().ToString("N"));
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public HealthTests()
    {
        var library = Path.Combine(_root, "library");
        var state = Path.Combine(_root, "state");
        Directory.CreateDirectory(library);
        Directory.CreateDirectory(state);

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Miaoyan:Library", library);
            builder.UseSetting("Miaoyan:StateDir", state);
        });
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Healthz_ReportsServerAndSqliteCapabilities()
    {
        var response = await _client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var health = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.NotNull(health);
        Assert.Equal("ok", health.Status);
        Assert.Equal("explicit", health.Vault.Mode);
        Assert.True(health.Sqlite.Fts5);
    }

    [Fact]
    public async Task UnknownApiRoute_ReturnsJson404()
    {
        var response = await _client.GetAsync("/api/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task SpaFallback_ServesIndexOrBuildHint()
    {
        var response = await _client.GetAsync("/some/deep/route");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("text/", response.Content.Headers.ContentType?.MediaType);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // SQLite may still hold a handle briefly on some platforms.
        }
    }

    private sealed record HealthResponse(string Status, VaultInfo Vault, SqliteInfo Sqlite);
    private sealed record VaultInfo(string Mode);
    private sealed record SqliteInfo(string Version, bool Fts5, bool Trigram);
}
