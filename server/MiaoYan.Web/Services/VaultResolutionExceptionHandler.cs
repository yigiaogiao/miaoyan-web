using Microsoft.AspNetCore.Diagnostics;

namespace MiaoYan.Web.Services;

/// <summary>
/// Turns an unresolvable LazyCat user into a clean JSON error instead of a
/// stack trace. This happens when the client did not pass through ingress
/// (for example a direct LAN call to the container).
/// </summary>
public sealed class VaultResolutionExceptionHandler(ILogger<VaultResolutionExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not VaultResolutionException vaultError)
        {
            return false;
        }

        logger.LogWarning("Vault resolution failed for {Path}: {Message}", context.Request.Path, vaultError.Message);

        context.Response.StatusCode = vaultError.StatusCode;
        await context.Response.WriteAsJsonAsync(new
        {
            error = "vault-unavailable",
            message = vaultError.Message,
        }, cancellationToken);

        return true;
    }
}
