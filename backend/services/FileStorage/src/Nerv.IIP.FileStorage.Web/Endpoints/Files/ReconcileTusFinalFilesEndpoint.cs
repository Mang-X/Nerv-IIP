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
        var result = await reconciliation.ScanAsync(ct);
        HttpContext.Response.Headers.CacheControl = "no-store";
        await HttpContext.Response.WriteAsJsonAsync(result, ct);
    }
}
