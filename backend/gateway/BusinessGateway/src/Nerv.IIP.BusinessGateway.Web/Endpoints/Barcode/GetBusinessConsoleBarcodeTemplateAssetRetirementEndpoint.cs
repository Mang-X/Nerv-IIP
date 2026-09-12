using FastEndpoints;
using FluentValidation;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.Contracts.BarcodeLabel;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Barcode;

[Tags("Business Console Barcode")]
[HttpGet("/api/business-console/v1/barcode/template-assets/retirement")]
[BusinessGatewayOperationId("getBusinessConsoleBarcodeTemplateAssetRetirement")]
public sealed class GetBusinessConsoleBarcodeTemplateAssetRetirementEndpoint(
    IBusinessGatewayAuthorizationClient auth, IBusinessBarcodeLabelClient barcode,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<GetTemplateAssetRetirementRequest, TemplateAssetRetirementResponse>(
        auth, BusinessGatewayPermissions.BarcodeTemplateAssetsRetire)
{
    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode =>
        BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;

    protected override string OrganizationId(GetTemplateAssetRetirementRequest request) => request.OrganizationId;
    protected override string EnvironmentId(GetTemplateAssetRetirementRequest request) => request.EnvironmentId;
    protected override string ResourceType(GetTemplateAssetRetirementRequest request) => "barcode-template";
    protected override string ResourceId(GetTemplateAssetRetirementRequest request) => request.TemplateId.ToString("D");

    protected override Task<TemplateAssetRetirementResponse> ForwardAsync(
        GetTemplateAssetRetirementRequest request, string bearerToken, CancellationToken cancellationToken) =>
        barcode.GetTemplateAssetRetirementAsync(tokenProvider.BearerToken, request, cancellationToken);
}

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
