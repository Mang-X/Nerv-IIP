using FastEndpoints;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelTemplateAggregate;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Auth;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Commands.TemplateAssetRetirements;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.TemplateAssetRetirementDecisionAggregate;
using Nerv.IIP.Contracts.BarcodeLabel;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Endpoints.BarcodeLabel;

public sealed class RetireTemplateAssetRequestValidator : Validator<RetireTemplateAssetRequest>
{
    public RetireTemplateAssetRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.TemplateId).NotEmpty();
        RuleFor(x => x.FileId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Checksum).NotEmpty().Matches("^sha256:[0-9a-f]{64}$");
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(128);
    }
}

public sealed class RetireTemplateAssetEndpoint(ISender sender, TemplateAssetRetirementProofVerifier verifier)
    : BarcodeLabelEndpoint<RetireTemplateAssetRequest, ResponseData<RetireTemplateAssetResponse>>
{
    public override void Configure()
    {
        ConfigureBarcodeLabelContract(BarcodeLabelEndpointContracts.Get<RetireTemplateAssetEndpoint>());
        Description(builder => builder.Produces<TemplateAssetRetirementProofError>(403));
    }

    public override async Task HandleAsync(RetireTemplateAssetRequest req, CancellationToken ct)
    {
        var subject = string.IsNullOrEmpty(req.Proof) ? null : verifier.Verify(req);
        if (subject is null)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
            await HttpContext.Response.WriteAsJsonAsync(new TemplateAssetRetirementProofError(
                "template-asset-retirement-proof-invalid", "模板资产退役授权证明无效。"),
                options: null, contentType: null, cancellationToken: ct);
            return;
        }
        var id = await sender.Send(new CreateTemplateAssetRetirementDecisionCommand(
            req.OrganizationId, req.EnvironmentId, new LabelTemplateId(req.TemplateId), req.FileId,
            req.Checksum, req.IdempotencyKey, subject, TemplateAssetRetirementDecision.RequiredPermission,
            req.Reason, HttpContext.TraceIdentifier), ct);
        await Send.OkAsync(new RetireTemplateAssetResponse(id.Id).AsResponseData(), cancellation: ct);
    }
}
