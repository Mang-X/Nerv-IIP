using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.FileStorage.Web.Application.Files.Tus;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.FileStorage.Web.Endpoints.Files;

[HttpGet("/internal/file-storage/v1/diagnostics/reconcile")]
[Authorize(Policy = InternalServiceAuthorizationPolicy.Name)]
public sealed class ReconcileTusFinalFilesEndpoint(TusFinalReconciliation reconciliation) : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "no-store";
        var result = await reconciliation.TryScanAsync(ct);
        if (result is null)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await HttpContext.Response.WriteAsync("A tus reconciliation scan is already in progress.", ct);
            return;
        }
        await HttpContext.Response.WriteAsJsonAsync(result, ct);
    }
}
