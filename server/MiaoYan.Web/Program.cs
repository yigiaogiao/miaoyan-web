using System.Reflection;
using MiaoYan.Web.Api;
using MiaoYan.Web.Options;
using MiaoYan.Web.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.StaticFiles;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables(prefix: "MIAOYAN_");

var options = new MiaoYanOptions();
builder.Configuration.GetSection("Miaoyan").Bind(options);

var maxUploadMb = ParseLong(Environment.GetEnvironmentVariable("MIAOYAN_MAX_UPLOAD_MB"), 0);
if (maxUploadMb > 0)
{
    options.MaxUploadBytes = maxUploadMb * 1024 * 1024;
}

options.WebPassword ??= Environment.GetEnvironmentVariable("MIAOYAN_WEB_PASSWORD");
options.ApiKey ??= Environment.GetEnvironmentVariable("MIAOYAN_API_KEY");
options.DocumentsRoot ??= Environment.GetEnvironmentVariable("MIAOYAN_DOCUMENTS_ROOT");
options.DefaultUid ??= Environment.GetEnvironmentVariable("MIAOYAN_DEFAULT_UID");
options.PlantUmlJar ??= Environment.GetEnvironmentVariable("MIAOYAN_PLANTUML_JAR");

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<VaultRegistry>();
builder.Services.AddSingleton<VaultEventHub>();
builder.Services.AddSingleton<NoteIndexService>();
builder.Services.AddSingleton<VaultService>();
builder.Services.AddSingleton<VaultWatcher>();
builder.Services.AddSingleton<CapabilitiesService>();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<VaultResolutionExceptionHandler>();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

builder.WebHost.ConfigureKestrel(kestrel =>
{
    // Uploads are streamed to disk; this only bounds a single request body.
    kestrel.Limits.MaxRequestBodySize = options.MaxUploadBytes + (1024 * 1024);
});

var app = builder.Build();

// Resolve the default vault at startup so a bad mount fails before the first
// request is served. A LazyCat device shared by several people may have no
// default yet; per-request resolution handles them.
var vaults = app.Services.GetRequiredService<VaultRegistry>();
var watcher = app.Services.GetRequiredService<VaultWatcher>();
var capabilities = app.Services.GetRequiredService<CapabilitiesService>();
var startupVault = vaults.TryResolveDefault();
if (startupVault is not null)
{
    watcher.Ensure(startupVault);
}
app.Logger.LogInformation(
    "MiaoYan Web {Version}: mode={Mode}, users={Users}, defaultVault={Vault}, SQLite {SqliteVersion} (fts5={Fts5}, trigram={Trigram}), PlantUML={PlantUml}",
    Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "dev",
    vaults.Mode,
    vaults.UserIds.Count,
    startupVault?.Paths.VaultRoot ?? "(none until first request with X-HC-User-ID)",
    capabilities.Sqlite.Version,
    capabilities.Sqlite.Fts5,
    capabilities.Sqlite.Trigram,
    capabilities.PlantUmlAvailable);

app.UseExceptionHandler();

app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["Referrer-Policy"] = "no-referrer";
    headers["X-Permitted-Cross-Domain-Policies"] = "none";
    headers["Cross-Origin-Opener-Policy"] = "same-origin";

    if (!context.Request.Path.StartsWithSegments("/api")
        && !context.Request.Path.StartsWithSegments("/healthz"))
    {
        headers["Content-Security-Policy"] =
            "default-src 'self'; " +
            "script-src 'self'; " +
            "style-src 'self' 'unsafe-inline'; " +
            "img-src 'self' data: blob: https:; " +
            "media-src 'self' blob:; " +
            "font-src 'self' data:; " +
            "connect-src 'self'; " +
            "frame-src 'none'; " +
            "object-src 'none'; " +
            "base-uri 'self'; " +
            "form-action 'self'";
    }

    await next();
});

var contentTypes = new FileExtensionContentTypeProvider();
contentTypes.Mappings[".m4a"] = "audio/mp4";
contentTypes.Mappings[".flac"] = "audio/flac";
contentTypes.Mappings[".webm"] = "audio/webm";
contentTypes.Mappings[".md"] = "text/markdown; charset=utf-8";

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = contentTypes,
    OnPrepareResponse = context =>
    {
        var response = context.Context.Response;
        if (context.Context.Request.Path.StartsWithSegments("/assets"))
        {
            response.Headers.CacheControl = "public, max-age=31536000, immutable";
        }
        else if (context.File.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            response.Headers.CacheControl = "no-cache, must-revalidate";
        }
    },
});

var startedAt = DateTimeOffset.UtcNow;
app.MapGet("/healthz", (VaultRegistry v, CapabilitiesService c) =>
{
    var users = v.UserIds;
    return Results.Json(new
    {
        status = "ok",
        version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "dev",
        startedAt,
        uptimeSeconds = (long)(DateTimeOffset.UtcNow - startedAt).TotalSeconds,
        vault = new
        {
            mode = v.Mode,
            documentsRoot = v.DocumentsRoot,
            explicitLibrary = v.ExplicitLibrary,
            state = v.StateBase,
            userCount = users.Count,
            defaultUid = v.DefaultUid,
        },
        sqlite = new { c.Sqlite.Version, fts5 = c.Sqlite.Fts5, trigram = c.Sqlite.Trigram },
        capabilities = new
        {
            plantuml = c.PlantUmlAvailable,
            whisper = c.WhisperConfigured,
            ocr = c.OcrConfigured,
            ffmpeg = c.FfmpegAvailable,
        },
    });
});

app.MapGet("/api/health", () => Results.Redirect("/healthz"));
app.MapVaultApi();

app.MapFallback("/api/{**path}", (HttpContext context) => Results.Json(
    new { error = "not-found", path = context.Request.Path.Value },
    statusCode: StatusCodes.Status404NotFound));

app.MapFallback(async context =>
{
    if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    var webRoot = app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");
    var index = Path.Combine(webRoot, "index.html");
    context.Response.Headers.CacheControl = "no-cache, must-revalidate";

    if (File.Exists(index))
    {
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.SendFileAsync(index);
        return;
    }

    context.Response.ContentType = "text/plain; charset=utf-8";
    await context.Response.WriteAsync(
        "MiaoYan Web API is running.\n\n" +
        "The SPA has not been built. Run `npm --prefix web install && npm --prefix web run build`, " +
        "or use the Vite dev server (npm --prefix web run dev).\n");
});

app.Run();

static long ParseLong(string? value, long fallback)
    => long.TryParse(value, out var parsed) ? parsed : fallback;

/// <summary>Exposed for integration tests.</summary>
public partial class Program;
