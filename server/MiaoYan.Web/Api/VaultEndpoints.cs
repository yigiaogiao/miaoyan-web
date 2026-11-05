using System.Text.Json;
using MiaoYan.Web.Options;
using MiaoYan.Web.Services;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Net.Http.Headers;

namespace MiaoYan.Web.Api;

public static class VaultEndpoints
{
    private static readonly FileExtensionContentTypeProvider ContentTypes = CreateContentTypes();

    public static void MapVaultApi(this WebApplication app)
    {
        app.MapGet("/api/events", EventsAsync);

        var api = app.MapGroup("/api");

        api.MapGet("/vault", (HttpContext context, VaultRegistry vaults, VaultWatcher watcher, VaultService service) =>
        {
            var runtime = Resolve(context, vaults, watcher);
            return Results.Ok(service.GetInfo(runtime));
        });

        api.MapGet("/projects", (HttpContext context, VaultRegistry vaults, VaultWatcher watcher, VaultService service) =>
        {
            var runtime = Resolve(context, vaults, watcher);
            return Results.Ok(service.ListProjects(runtime));
        });

        api.MapPost("/projects", (CreateProjectRequest? request, HttpContext context, VaultRegistry vaults, VaultWatcher watcher, VaultService service) =>
        {
            var runtime = Resolve(context, vaults, watcher);
            return Results.Ok(service.CreateProject(runtime, request ?? new CreateProjectRequest(null)));
        });

        api.MapPatch("/projects/{id}", (string id, RenameProjectRequest? request, HttpContext context, VaultRegistry vaults, VaultWatcher watcher, VaultService service) =>
        {
            var runtime = Resolve(context, vaults, watcher);
            return Results.Ok(service.RenameProject(runtime, id, request ?? new RenameProjectRequest(null)));
        });

        api.MapDelete("/projects/{id}", (string id, HttpContext context, VaultRegistry vaults, VaultWatcher watcher, VaultService service) =>
        {
            var runtime = Resolve(context, vaults, watcher);
            service.DeleteProject(runtime, id);
            return Results.NoContent();
        });

        api.MapGet("/notes", (string? project, string? q, HttpContext context, VaultRegistry vaults, VaultWatcher watcher, VaultService service) =>
        {
            var runtime = Resolve(context, vaults, watcher);
            return Results.Ok(service.ListNotes(runtime, project, q));
        });

        api.MapPost("/notes", (CreateNoteRequest? request, HttpContext context, VaultRegistry vaults, VaultWatcher watcher, VaultService service) =>
        {
            var runtime = Resolve(context, vaults, watcher);
            var note = service.CreateNote(runtime, request ?? new CreateNoteRequest(null, null));
            SetEtag(context, note.Etag);
            return Results.Ok(note);
        });

        api.MapGet("/notes/{id}", (string id, HttpContext context, VaultRegistry vaults, VaultWatcher watcher, VaultService service) =>
        {
            var runtime = Resolve(context, vaults, watcher);
            var note = service.GetNote(runtime, id);
            SetEtag(context, note.Etag);
            return Results.Ok(note);
        });

        api.MapGet("/links", (HttpContext context, VaultRegistry vaults, VaultWatcher watcher, VaultService service) =>
        {
            var runtime = Resolve(context, vaults, watcher);
            return Results.Ok(service.GetLinkIndex(runtime));
        });

        api.MapGet("/search", (string? q, string? project, int? limit, HttpContext context, VaultRegistry vaults, VaultWatcher watcher, VaultService service) =>
        {
            var runtime = Resolve(context, vaults, watcher);
            return Results.Ok(service.SearchNotes(runtime, q, project, limit ?? 50));
        });

        api.MapGet("/notes/{id}/backlinks", (string id, HttpContext context, VaultRegistry vaults, VaultWatcher watcher, VaultService service) =>
        {
            var runtime = Resolve(context, vaults, watcher);
            return Results.Ok(service.GetBacklinks(runtime, id));
        });

        api.MapGet("/notes/{id}/versions", (string id, HttpContext context, VaultRegistry vaults, VaultWatcher watcher, VaultService service) =>
        {
            var runtime = Resolve(context, vaults, watcher);
            return Results.Ok(service.ListVersions(runtime, id));
        });

        api.MapGet("/notes/{id}/versions/{versionId}", (string id, string versionId, HttpContext context, VaultRegistry vaults, VaultWatcher watcher, VaultService service) =>
        {
            var runtime = Resolve(context, vaults, watcher);
            return Results.Ok(service.GetVersion(runtime, id, versionId));
        });

        api.MapPost("/notes/{id}/versions/{versionId}/restore", (string id, string versionId, RestoreVersionRequest? request, HttpContext context, VaultRegistry vaults, VaultWatcher watcher, VaultService service) =>
        {
            var runtime = Resolve(context, vaults, watcher);
            var result = service.RestoreVersion(runtime, id, versionId, request ?? new RestoreVersionRequest(null));
            if (result.Conflict is not null)
            {
                return Results.Json(result.Conflict, statusCode: StatusCodes.Status412PreconditionFailed);
            }

            SetEtag(context, result.Note!.Etag);
            return Results.Ok(result.Note);
        });

        api.MapPut("/notes/{id}", async (string id, SaveNoteRequest? request, HttpContext context, VaultRegistry vaults, VaultWatcher watcher, VaultService service, CancellationToken cancellationToken) =>
        {
            var runtime = Resolve(context, vaults, watcher);
            if (request is null)
            {
                throw new ApiException(StatusCodes.Status400BadRequest, "body-required", "A JSON body is required.");
            }

            var result = await service.SaveNoteAsync(runtime, id, request, cancellationToken);
            if (result.Conflict is not null)
            {
                return Results.Json(result.Conflict, statusCode: StatusCodes.Status412PreconditionFailed);
            }

            SetEtag(context, result.Note!.Etag);
            return Results.Ok(result.Note);
        });

        api.MapPatch("/notes/{id}", (string id, RenameNoteRequest? request, HttpContext context, VaultRegistry vaults, VaultWatcher watcher, VaultService service) =>
        {
            var runtime = Resolve(context, vaults, watcher);
            var note = service.RenameNote(runtime, id, request ?? new RenameNoteRequest(null, null));
            SetEtag(context, note.Etag);
            return Results.Ok(note);
        });

        api.MapPost("/notes/{id}/duplicate", (string id, HttpContext context, VaultRegistry vaults, VaultWatcher watcher, VaultService service) =>
        {
            var runtime = Resolve(context, vaults, watcher);
            var note = service.DuplicateNote(runtime, id);
            return Results.Created($"/api/notes/{note.Id}", note);
        });

        api.MapDelete("/notes/{id}", (string id, HttpContext context, VaultRegistry vaults, VaultWatcher watcher, VaultService service) =>
        {
            var runtime = Resolve(context, vaults, watcher);
            service.DeleteNote(runtime, id);
            return Results.NoContent();
        });

        api.MapPost("/notes/{id}/assets", async (string id, HttpContext context, VaultRegistry vaults, VaultWatcher watcher, VaultService service, MiaoYanOptions options, CancellationToken cancellationToken) =>
        {
            var runtime = Resolve(context, vaults, watcher);
            if (!context.Request.HasFormContentType)
            {
                throw new ApiException(StatusCodes.Status400BadRequest, "multipart-required", "Upload must be multipart/form-data.");
            }

            var form = await context.Request.ReadFormAsync(cancellationToken);
            var file = form.Files.GetFile("file")
                ?? throw new ApiException(StatusCodes.Status400BadRequest, "file-required", "The form field 'file' is required.");
            var kind = context.Request.Query["kind"].ToString();
            if (kind.Length == 0)
            {
                kind = file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ? "image" : "file";
            }

            var asset = await service.SaveAssetAsync(runtime, id, kind, file, options.MaxUploadBytes, cancellationToken);
            return Results.Ok(asset);
        });

        api.MapGet("/notes/{id}/assets/{**path}", (string id, string path, HttpContext context, VaultRegistry vaults, VaultWatcher watcher, VaultService service) =>
        {
            var runtime = Resolve(context, vaults, watcher);
            var asset = service.ResolveAsset(runtime, id, path);
            if (!ContentTypes.TryGetContentType(asset.FileName, out var contentType))
            {
                contentType = "application/octet-stream";
            }

            context.Response.Headers.CacheControl = "private, max-age=3600";
            return Results.File(asset.FullPath, contentType, enableRangeProcessing: true);
        });

        api.MapGet("/trash", (HttpContext context, VaultRegistry vaults, VaultWatcher watcher, VaultService service) =>
        {
            var runtime = Resolve(context, vaults, watcher);
            return Results.Ok(service.ListTrash(runtime));
        });

        api.MapPost("/trash/{id}/restore", (string id, HttpContext context, VaultRegistry vaults, VaultWatcher watcher, VaultService service) =>
        {
            var runtime = Resolve(context, vaults, watcher);
            return Results.Ok(service.RestoreTrash(runtime, id));
        });

        api.MapDelete("/trash/{id}", (string id, HttpContext context, VaultRegistry vaults, VaultWatcher watcher, VaultService service) =>
        {
            var runtime = Resolve(context, vaults, watcher);
            service.PurgeTrash(runtime, id);
            return Results.NoContent();
        });
    }

    private static VaultRuntime Resolve(HttpContext context, VaultRegistry vaults, VaultWatcher watcher)
    {
        var runtime = vaults.Resolve(context);
        watcher.Ensure(runtime);
        return runtime;
    }

    private static void SetEtag(HttpContext context, string etag)
        => context.Response.Headers.ETag = new EntityTagHeaderValue($"\"{etag}\"").ToString();

    private static async Task EventsAsync(
        HttpContext context,
        VaultRegistry vaults,
        VaultWatcher watcher,
        VaultEventHub hub,
        CancellationToken cancellationToken)
    {
        var runtime = Resolve(context, vaults, watcher);

        context.Response.ContentType = "text/event-stream; charset=utf-8";
        context.Response.Headers.CacheControl = "no-cache, no-transform";
        context.Response.Headers["X-Accel-Buffering"] = "no";

        var (subscriptionId, reader) = hub.Subscribe(runtime.Key);
        try
        {
            await context.Response.WriteAsync("retry: 3000\n\n", cancellationToken);
            await context.Response.Body.FlushAsync(cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                wait.CancelAfter(TimeSpan.FromSeconds(20));
                try
                {
                    if (await reader.WaitToReadAsync(wait.Token))
                    {
                        while (reader.TryRead(out var change))
                        {
                            var json = JsonSerializer.Serialize(change, JsonSerializerOptions.Web);
                            await context.Response.WriteAsync($"event: change\ndata: {json}\n\n", cancellationToken);
                        }

                        await context.Response.Body.FlushAsync(cancellationToken);
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    await context.Response.WriteAsync(": ping\n\n", cancellationToken);
                    await context.Response.Body.FlushAsync(cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The tab closed.
        }
        finally
        {
            hub.Unsubscribe(runtime.Key, subscriptionId);
        }
    }

    private static FileExtensionContentTypeProvider CreateContentTypes()
    {
        var provider = new FileExtensionContentTypeProvider();
        provider.Mappings[".m4a"] = "audio/mp4";
        provider.Mappings[".flac"] = "audio/flac";
        provider.Mappings[".webm"] = "audio/webm";
        provider.Mappings[".md"] = "text/markdown; charset=utf-8";
        return provider;
    }
}
