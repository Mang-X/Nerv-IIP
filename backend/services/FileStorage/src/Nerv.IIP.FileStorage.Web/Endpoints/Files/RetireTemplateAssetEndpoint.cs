using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.Contracts.FileStorage;
using Nerv.IIP.FileStorage.Infrastructure;
using Nerv.IIP.FileStorage.Web.Application.Files;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.FileStorage.Web.Endpoints.Files;

[Tags("Files")]
[HttpPost("/internal/file-storage/v1/template-asset-retirements")]
[Authorize(Policy = InternalServiceAuthorizationPolicy.Name)]
public sealed class RetireTemplateAssetEndpoint(TemplateAssetRetirementProof proof,
    TemplateAssetRetirementStore store, TemplateAssetRetirementOptions options)
    : Endpoint<RetireTemplateAssetRequest, RetireTemplateAssetResponse>
{
    public override async Task HandleAsync(RetireTemplateAssetRequest req, CancellationToken ct)
    {
        var capability = proof.Verify(req);
        if (capability is null)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
            await HttpContext.Response.WriteAsJsonAsync(new { code = "retirement-proof-invalid" }, ct);
            return;
        }
        RetirementAcceptance result;
        try
        {
            result = await store.AcceptAsync(capability, options.Storage, ct);
        }
        catch (ArgumentException)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await HttpContext.Response.WriteAsJsonAsync(new { code = "retirement-replay-inputs-invalid" }, ct);
            return;
        }
        if (result.Receipt is not { } receipt)
        {
            HttpContext.Response.StatusCode = result.Conflict ? StatusCodes.Status409Conflict : StatusCodes.Status404NotFound;
            await HttpContext.Response.WriteAsJsonAsync(new { code = result.Conflict ? "retirement-conflict" : "retirement-file-not-found" }, ct);
            return;
        }
        await HttpContext.Response.WriteAsJsonAsync(new RetireTemplateAssetResponse(receipt.DecisionId,
            receipt.FileId, receipt.Status, receipt.AcceptedAtUtc, receipt.ReplayHorizonSeconds), ct);
    }
}
