using System.Text;
using MiaoYan.Web.Services;

namespace MiaoYan.Web.Api;

public static class RenderEndpoints
{
    public static void MapRenderApi(this WebApplication app)
    {
        app.MapPost("/api/render/plantuml", async (
            HttpContext context,
            PlantUmlRenderer renderer,
            CancellationToken cancellationToken) =>
        {
            if (!renderer.Available)
            {
                return Results.Json(
                    new { error = "plantuml-unavailable", message = "PlantUML is not installed on this server." },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var source = await ReadLimitedAsync(
                context.Request.Body,
                PlantUmlRenderer.MaxSourceBytes,
                cancellationToken);
            if (source is null)
            {
                return Results.Json(
                    new { error = "plantuml-too-large", message = "Diagram source is too large." },
                    statusCode: StatusCodes.Status413PayloadTooLarge);
            }

            var result = await renderer.RenderAsync(source, cancellationToken);
            if (!result.Ok)
            {
                return Results.Json(
                    new { error = result.Available ? "plantuml-invalid" : "plantuml-unavailable", message = result.Error },
                    statusCode: result.Available
                        ? StatusCodes.Status422UnprocessableEntity
                        : StatusCodes.Status503ServiceUnavailable);
            }

            context.Response.Headers.CacheControl = "private, max-age=300";
            return Results.Text(result.Svg!, "image/svg+xml", Encoding.UTF8);
        });
    }

    private static async Task<string?> ReadLimitedAsync(
        Stream body,
        int limit,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var read = await body.ReadAsync(chunk, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (buffer.Length + read > limit)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
