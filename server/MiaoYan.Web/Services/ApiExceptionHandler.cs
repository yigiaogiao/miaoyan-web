using Microsoft.AspNetCore.Diagnostics;

namespace MiaoYan.Web.Services;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not ApiException api)
        {
            return false;
        }

        if (api.StatusCode >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "API failure for {Path}", context.Request.Path);
        }
        else
        {
            logger.LogDebug("API {Code} for {Path}: {Message}", api.Code, context.Request.Path, api.Message);
        }

        context.Response.StatusCode = api.StatusCode;
        await context.Response.WriteAsJsonAsync(new
        {
            error = api.Code,
            message = api.Message,
        }, cancellationToken);
        return true;
    }
}
