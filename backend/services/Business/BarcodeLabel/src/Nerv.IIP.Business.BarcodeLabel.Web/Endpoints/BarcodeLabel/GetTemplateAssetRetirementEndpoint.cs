using FastEndpoints;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelTemplateAggregate;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Queries.LabelTemplates;
using Nerv.IIP.Contracts.BarcodeLabel;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Endpoints.BarcodeLabel;

public sealed class GetTemplateAssetRetirementRequestValidator : Validator<GetTemplateAssetRetirementRequest>
{
    public GetTemplateAssetRetirementRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.TemplateId).NotEmpty();
        RuleFor(x => x.FileId).NotEmpty().MaximumLength(150);
    }
}

public sealed class GetTemplateAssetRetirementEndpoint(ISender sender)
    : BarcodeLabelEndpoint<GetTemplateAssetRetirementRequest, ResponseData<TemplateAssetRetirementResponse>>
{
    public override void Configure() =>
        ConfigureBarcodeLabelContract(BarcodeLabelEndpointContracts.Get<GetTemplateAssetRetirementEndpoint>());

    public override async Task HandleAsync(GetTemplateAssetRetirementRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new GetTemplateAssetRetirementQuery(
            req.OrganizationId, req.EnvironmentId, new LabelTemplateId(req.TemplateId), req.FileId), ct);
        await Send.OkAsync(result.AsResponseData(), cancellation: ct);
    }
}
