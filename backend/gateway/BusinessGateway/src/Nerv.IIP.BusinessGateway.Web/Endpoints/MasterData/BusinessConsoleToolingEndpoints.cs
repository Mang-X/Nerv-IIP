using FastEndpoints;
using FluentValidation;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.MasterData;

[Tags("Business Console MasterData")]
[HttpGet("/api/business-console/v1/master-data/tooling-assets")]
[BusinessGatewayOperationId("listBusinessConsoleToolingAssets")]
public sealed class ListBusinessConsoleToolingAssetsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleListToolingAssetsRequest, BusinessConsoleToolingAssetListResponse>(
        auth,
        BusinessGatewayPermissions.MasterDataResourcesRead)
{
    protected override string OrganizationId(BusinessConsoleListToolingAssetsRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleListToolingAssetsRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleToolingAssetListResponse> ForwardAsync(
        BusinessConsoleListToolingAssetsRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        masterData.ListToolingAssetsAsync(tokenProvider.BearerToken, request, ResolveCorrelationId(), cancellationToken);
}

[Tags("Business Console MasterData")]
[HttpPost("/api/business-console/v1/master-data/tooling-assets")]
[BusinessGatewayOperationId("registerBusinessConsoleToolingAsset")]
public sealed class RegisterBusinessConsoleToolingAssetEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleRegisterToolingAssetRequest, BusinessConsoleToolingRegistrationResponse>(
        auth,
        BusinessGatewayPermissions.MasterDataResourcesManage)
{
    protected override string OrganizationId(BusinessConsoleRegisterToolingAssetRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleRegisterToolingAssetRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleToolingRegistrationResponse> ForwardAsync(
        BusinessConsoleRegisterToolingAssetRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        masterData.RegisterToolingAssetAsync(tokenProvider.BearerToken, request, ResolveCorrelationId(), cancellationToken);
}

[Tags("Business Console MasterData")]
[HttpPost("/api/business-console/v1/master-data/tooling-assets/status")]
[BusinessGatewayOperationId("changeBusinessConsoleToolingStatus")]
public sealed class ChangeBusinessConsoleToolingStatusEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleChangeToolingStatusRequest, BusinessConsoleAcceptedResponse>(
        auth,
        BusinessGatewayPermissions.MasterDataResourcesManage)
{
    protected override string OrganizationId(BusinessConsoleChangeToolingStatusRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleChangeToolingStatusRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleAcceptedResponse> ForwardAsync(
        BusinessConsoleChangeToolingStatusRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        masterData.ChangeToolingStatusAsync(tokenProvider.BearerToken, request, ResolveCorrelationId(), cancellationToken);
}

[Tags("Business Console MasterData")]
[HttpPost("/api/business-console/v1/master-data/tooling-assets/usage")]
[BusinessGatewayOperationId("recordBusinessConsoleToolingUsage")]
public sealed class RecordBusinessConsoleToolingUsageEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleRecordToolingUsageRequest, BusinessConsoleAcceptedResponse>(
        auth,
        BusinessGatewayPermissions.MasterDataResourcesManage)
{
    protected override string OrganizationId(BusinessConsoleRecordToolingUsageRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleRecordToolingUsageRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleAcceptedResponse> ForwardAsync(
        BusinessConsoleRecordToolingUsageRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        masterData.RecordToolingUsageAsync(tokenProvider.BearerToken, request, ResolveCorrelationId(), cancellationToken);
}

public sealed class BusinessConsoleListToolingAssetsRequestValidator : Validator<BusinessConsoleListToolingAssetsRequest>
{
    public BusinessConsoleListToolingAssetsRequestValidator()
    {
        RuleFor(request => request.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(request => request.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(request => request.Keyword).MaximumLength(200);
        RuleFor(request => request.Status).IsInEnum().When(request => request.Status.HasValue);
        RuleFor(request => request.Skip).GreaterThanOrEqualTo(0);
        RuleFor(request => request.Take).InclusiveBetween(1, 500);
    }
}

public sealed class BusinessConsoleRegisterToolingAssetRequestValidator : Validator<BusinessConsoleRegisterToolingAssetRequest>
{
    public BusinessConsoleRegisterToolingAssetRequestValidator()
    {
        RuleFor(request => request.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(request => request.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(request => request.Name).NotEmpty();
        RuleFor(request => request.ToolingType).NotEmpty();
        RuleFor(request => request.WorkCenterCodes).NotEmpty();
        RuleFor(request => request.SkuCodes).NotEmpty();
        RuleFor(request => request.MaintenanceLifeCount).GreaterThan(0).When(request => request.MaintenanceLifeCount.HasValue);
    }
}

public sealed class BusinessConsoleChangeToolingStatusRequestValidator : Validator<BusinessConsoleChangeToolingStatusRequest>
{
    public BusinessConsoleChangeToolingStatusRequestValidator()
    {
        RuleFor(request => request.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(request => request.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(request => request.Code).NotEmpty();
        RuleFor(request => request.Status).IsInEnum();
        RuleFor(request => request.Reason).NotEmpty();
    }
}

public sealed class BusinessConsoleRecordToolingUsageRequestValidator : Validator<BusinessConsoleRecordToolingUsageRequest>
{
    public BusinessConsoleRecordToolingUsageRequestValidator()
    {
        RuleFor(request => request.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(request => request.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(request => request.Code).NotEmpty();
        RuleFor(request => request.Count).GreaterThan(0);
    }
}
