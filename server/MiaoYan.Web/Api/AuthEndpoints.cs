using MiaoYan.Web.Services;

namespace MiaoYan.Web.Api;

public sealed record LoginRequest(string? Secret);

public static class AuthEndpoints
{
    public static void MapAuthApi(this WebApplication app)
    {
        var auth = app.MapGroup("/api/auth");

        auth.MapGet("/status", (HttpContext context, AuthService service) => Results.Ok(new
        {
            required = service.Enabled,
            authenticated = service.IsAuthenticated(context),
            password = service.HasPassword,
            apiKey = service.HasApiKey,
        }));

        auth.MapPost("/login", (LoginRequest? request, HttpContext context, AuthService service) =>
        {
            if (!service.Enabled)
            {
                return Results.Ok(new { ok = true, required = false });
            }

            var result = service.Login(context, request?.Secret);
            if (result == LoginResult.Success)
            {
                service.IssueSession(context);
                return Results.Ok(new { ok = true, required = true });
            }

            if (result == LoginResult.RateLimited)
            {
                return Results.Json(
                    new { error = "rate-limited", message = "Too many attempts. Try again later." },
                    statusCode: StatusCodes.Status429TooManyRequests);
            }

            return Results.Json(
                new { error = "unauthorized", message = "Wrong password or API key." },
                statusCode: StatusCodes.Status401Unauthorized);
        });

        auth.MapPost("/logout", (HttpContext context, AuthService service) =>
        {
            service.ClearSession(context);
            return Results.Ok(new { ok = true });
        });
    }

    /// <summary>
    /// Guards every <c>/api</c> call when a credential is configured. The SPA
    /// shell stays public so the login screen can load; health checks remain
    /// public for the LazyCat supervisor.
    /// </summary>
    public static void UseMiaoYanAuth(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            var auth = context.RequestServices.GetRequiredService<AuthService>();
            if (!auth.Enabled
                || !context.Request.Path.StartsWithSegments("/api")
                || AuthService.IsPublicPath(context.Request.Path))
            {
                await next();
                return;
            }

            if (!auth.IsAuthenticated(context))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json; charset=utf-8";
                await context.Response.WriteAsJsonAsync(
                    new { error = "unauthorized", message = "Sign in to continue." });
                return;
            }

            if (!HttpMethods.IsGet(context.Request.Method)
                && !HttpMethods.IsHead(context.Request.Method)
                && auth.UsesCookie(context)
                && !auth.CheckOrigin(context))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json; charset=utf-8";
                await context.Response.WriteAsJsonAsync(
                    new { error = "forbidden-origin", message = "Cross-origin writes are not allowed." });
                return;
            }

            await next();
        });
    }
}
