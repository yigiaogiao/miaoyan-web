using System.Net;
using System.Net.Http.Json;
using MiaoYan.Web.Api;
using MiaoYan.Web.Services;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MiaoYan.Web.Tests;

public sealed class AuthTests : IClassFixture<AuthTests.AuthFixture>
{
    internal const string Password = "correct horse battery staple";
    private const string ApiKey = "test-api-key-0123456789";
    private readonly AuthFixture _auth;

    public AuthTests(AuthFixture auth)
    {
        _auth = auth;
    }

    [Fact]
    public async Task AnonymousRequestsAreRejectedButHealthAndStatusStayPublic()
    {
        using var client = _auth.Factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/notes")).StatusCode);

        var status = await client.GetFromJsonAsync<AuthStatus>("/api/auth/status");
        Assert.NotNull(status);
        Assert.True(status!.Required);
        Assert.False(status.Authenticated);
        Assert.True(status.Password);
        Assert.True(status.ApiKey);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/healthz")).StatusCode);
    }

    [Fact]
    public async Task WrongPasswordDoesNotCreateSession()
    {
        using var client = _auth.Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("wrong"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/notes")).StatusCode);
    }

    [Fact]
    public async Task PasswordLoginCreatesCookieAndLogoutClearsIt()
    {
        using var client = _auth.Factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(Password));
        login.EnsureSuccessStatusCode();

        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie"));
        Assert.Contains("HttpOnly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SameSite=Lax", cookie, StringComparison.OrdinalIgnoreCase);

        var notes = await client.GetAsync("/api/notes");
        Assert.Equal(HttpStatusCode.OK, notes.StatusCode);

        var logout = await client.PostAsync("/api/auth/logout", null);
        logout.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/notes")).StatusCode);
    }

    [Fact]
    public async Task ApiKeyAuthenticatesMachineRequests()
    {
        using var client = _auth.Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/notes");
        request.Headers.Add("X-API-Key", ApiKey);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);

        using var wrong = new HttpRequestMessage(HttpMethod.Get, "/api/notes");
        wrong.Headers.Add("X-API-Key", ApiKey + "x");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(wrong)).StatusCode);

        using var bearer = new HttpRequestMessage(HttpMethod.Get, "/api/notes");
        bearer.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ApiKey);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(bearer)).StatusCode);
    }

    [Fact]
    public async Task ApiKeyCanBeUsedAsBrowserLoginSecret()
    {
        using var client = _auth.Factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(ApiKey));
        login.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/notes")).StatusCode);
    }

    [Fact]
    public async Task ForgedSessionCookieIsRejected()
    {
        using var client = _auth.Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/notes");
        request.Headers.Add("Cookie", $"{AuthService.SessionCookie}=9999999999.AAAA");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task CookieWritesRequireMatchingOrigin()
    {
        using var client = _auth.Factory.CreateClient();
        (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(Password))).EnsureSuccessStatusCode();

        using var cross = new HttpRequestMessage(HttpMethod.Post, "/api/notes")
        {
            Content = JsonContent.Create(new CreateNoteRequest(null, "cross-origin")),
        };
        cross.Headers.Add("Origin", "https://evil.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(cross)).StatusCode);

        using var same = new HttpRequestMessage(HttpMethod.Post, "/api/notes")
        {
            Content = JsonContent.Create(new CreateNoteRequest(null, "same-origin")),
        };
        same.Headers.Add("Origin", client.BaseAddress!.GetLeftPart(UriPartial.Authority));
        var created = await client.SendAsync(same);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
    }

    public sealed class AuthFixture : IDisposable
    {
        public AuthFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "miaoyan-auth-tests", Guid.NewGuid().ToString("N"));
            Library = Path.Combine(Root, "library");
            var state = Path.Combine(Root, "state");
            Directory.CreateDirectory(Library);
            Directory.CreateDirectory(state);

            Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Miaoyan:Library", Library);
                builder.UseSetting("Miaoyan:StateDir", state);
                builder.UseSetting("Miaoyan:WebPassword", Password);
                builder.UseSetting("Miaoyan:ApiKey", ApiKey);
            });
        }

        public string Root { get; }
        public string Library { get; }
        public WebApplicationFactory<Program> Factory { get; }

        public void Dispose()
        {
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

    public sealed record AuthStatus(bool Required, bool Authenticated, bool Password, bool ApiKey);
}

public sealed class AuthRateLimitTests : IClassFixture<AuthRateLimitTests.RateFixture>
{
    private readonly RateFixture _rate;

    public AuthRateLimitTests(RateFixture rate)
    {
        _rate = rate;
    }

    [Fact]
    public async Task RepeatedFailuresAreRateLimited()
    {
        using var client = _rate.Factory.CreateClient();
        for (var i = 0; i < 8; i++)
        {
            var attempt = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("wrong"));
            Assert.Equal(HttpStatusCode.Unauthorized, attempt.StatusCode);
        }

        var blocked = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(AuthTests.Password));
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
    }

    public sealed class RateFixture : IDisposable
    {
        public RateFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "miaoyan-auth-rate", Guid.NewGuid().ToString("N"));
            var library = Path.Combine(Root, "library");
            var state = Path.Combine(Root, "state");
            Directory.CreateDirectory(library);
            Directory.CreateDirectory(state);
            Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Miaoyan:Library", library);
                builder.UseSetting("Miaoyan:StateDir", state);
                builder.UseSetting("Miaoyan:WebPassword", AuthTests.Password);
            });
        }

        public string Root { get; }
        public WebApplicationFactory<Program> Factory { get; }

        public void Dispose()
        {
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
