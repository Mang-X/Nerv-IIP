using FastEndpoints;
using FluentValidation;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.Contracts.BarcodeLabel;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Barcode;

public sealed record RetireBusinessConsoleBarcodeTemplateAssetRequest(
    string OrganizationId, string EnvironmentId, Guid TemplateId, string FileId,
    string Checksum, string Reason, string IdempotencyKey);

[Tags("Business Console Barcode")]
[HttpPost("/api/business-console/v1/barcode/template-assets/retire")]
[BusinessGatewayOperationId("retireBusinessConsoleBarcodeTemplateAsset")]
public sealed class RetireBusinessConsoleBarcodeTemplateAssetEndpoint(
    IBusinessGatewayAuthorizationClient auth, IBusinessBarcodeLabelClient barcode,
    IInternalServiceTokenProvider tokenProvider, TemplateAssetRetirementProofSigner signer)
    : AuthorizedBusinessProxyEndpoint<RetireBusinessConsoleBarcodeTemplateAssetRequest, RetireTemplateAssetResponse>(
        auth, BusinessGatewayPermissions.BarcodeTemplateAssetsRetire)
{
    protected override string OrganizationId(RetireBusinessConsoleBarcodeTemplateAssetRequest request) => request.OrganizationId;
    protected override string EnvironmentId(RetireBusinessConsoleBarcodeTemplateAssetRequest request) => request.EnvironmentId;
    protected override string ResourceType(RetireBusinessConsoleBarcodeTemplateAssetRequest request) => "barcode-template";
    protected override string ResourceId(RetireBusinessConsoleBarcodeTemplateAssetRequest request) => request.TemplateId.ToString("D");

    protected override Task<RetireTemplateAssetResponse> ForwardAsync(
        RetireBusinessConsoleBarcodeTemplateAssetRequest request, string bearerToken, CancellationToken cancellationToken) =>
        barcode.RetireTemplateAssetAsync(tokenProvider.BearerToken,
            signer.Sign(new RetireTemplateAssetRequest(request.OrganizationId, request.EnvironmentId,
                request.TemplateId, request.FileId, request.Checksum, request.Reason, request.IdempotencyKey, ""),
                RequireAuthorizedPrincipalId()), cancellationToken);
}

public sealed class RetireBusinessConsoleBarcodeTemplateAssetRequestValidator : Validator<RetireBusinessConsoleBarcodeTemplateAssetRequest>
{
    public RetireBusinessConsoleBarcodeTemplateAssetRequestValidator()
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
