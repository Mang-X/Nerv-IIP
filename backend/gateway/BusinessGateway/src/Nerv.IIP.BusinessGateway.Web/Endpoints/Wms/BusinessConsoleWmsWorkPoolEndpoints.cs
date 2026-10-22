using FastEndpoints;
using FluentValidation;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Wms;

// 仓库作业池维护（#3849）：仓库主管在控制台建池、加减成员；分配入库/出库/盘点单时也从这里取池。
// 站点边界与派工同口径：只能看、只能改自己 IAM 精确站点授权内的作业池。

[Tags("Business Console WMS")]
[HttpGet("/api/business-console/v1/wms/work-pools")]
[BusinessGatewayOperationId("listBusinessConsoleWmsWorkPools")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status403Forbidden)]
public sealed class ListBusinessConsoleWmsWorkPoolsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider,
    WmsTrustedRequestContextResolver trustedContextResolver)
    : BusinessConsoleWmsTrustedProxyEndpoint<BusinessConsoleWmsWorkPoolListRequest, BusinessConsoleWmsWorkPoolListResponse>(
        auth,
        trustedContextResolver,
        BusinessGatewayPermissions.WmsWorkPoolsManage)
{
    protected override string OrganizationId(BusinessConsoleWmsWorkPoolListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleWmsWorkPoolListRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleWmsWorkPoolListResponse> ForwardAsync(
        BusinessConsoleWmsWorkPoolListRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var trusted = await ResolveTrustedContextAsync(request, cancellationToken);
        return await wms.ListWorkPoolsAsync(
            tokenProvider.BearerToken,
            new BusinessWmsWorkScopeCatalogRequest(
                request.OrganizationId,
                request.EnvironmentId,
                trusted.ActorPrincipalId,
                trusted.AuthorizedSiteCodes),
            cancellationToken);
    }
}

[Tags("Business Console WMS")]
[HttpPost("/api/business-console/v1/wms/work-pools")]
[BusinessGatewayOperationId("createBusinessConsoleWmsWorkPool")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status403Forbidden)]
public sealed class CreateBusinessConsoleWmsWorkPoolEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider,
    WmsTrustedRequestContextResolver trustedContextResolver)
    : BusinessConsoleWmsTrustedProxyEndpoint<BusinessConsoleCreateWmsWorkPoolRequest, BusinessConsoleWmsWorkPoolResult>(
        auth,
        trustedContextResolver,
        BusinessGatewayPermissions.WmsWorkPoolsManage)
{
    protected override string OrganizationId(BusinessConsoleCreateWmsWorkPoolRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateWmsWorkPoolRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleWmsWorkPoolResult> ForwardAsync(
        BusinessConsoleCreateWmsWorkPoolRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var trusted = await ResolveTrustedContextAsync(request, cancellationToken);
        return await wms.ProvisionWorkPoolAsync(
            tokenProvider.BearerToken,
            new BusinessWmsProvisionWorkPoolRequest(
                request.OrganizationId,
                request.EnvironmentId,
                trusted.ActorPrincipalId,
                trusted.AuthorizedSiteCodes,
                null,
                request.DisplayName,
                request.SiteCode,
                request.IdempotencyKey),
            cancellationToken);
    }
}

[Tags("Business Console WMS")]
[HttpPost("/api/business-console/v1/wms/work-pools/{poolCode}/members")]
[BusinessGatewayOperationId("addBusinessConsoleWmsWorkPoolMember")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status403Forbidden)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status422UnprocessableEntity)]
public sealed class AddBusinessConsoleWmsWorkPoolMemberEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider,
    WmsTrustedRequestContextResolver trustedContextResolver)
    : BusinessConsoleWmsTrustedProxyEndpoint<BusinessConsoleAddWmsWorkPoolMemberRequest, BusinessConsoleWmsWorkPoolMemberResult>(
        auth,
        trustedContextResolver,
        BusinessGatewayPermissions.WmsWorkPoolsManage)
{
    protected override string OrganizationId(BusinessConsoleAddWmsWorkPoolMemberRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleAddWmsWorkPoolMemberRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleWmsWorkPoolMemberResult> ForwardAsync(
        BusinessConsoleAddWmsWorkPoolMemberRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var trusted = await ResolveTrustedContextAsync(request, cancellationToken);
        return await wms.AddWorkPoolMemberAsync(
            tokenProvider.BearerToken,
            new BusinessWmsAddWorkPoolMemberRequest(
                Route<string>("poolCode") ?? request.PoolCode,
                request.OrganizationId,
                request.EnvironmentId,
                trusted.ActorPrincipalId,
                trusted.AuthorizedSiteCodes,
                request.PrincipalId),
            cancellationToken);
    }
}

[Tags("Business Console WMS")]
[HttpPost("/api/business-console/v1/wms/work-pools/{poolCode}/members/{principalId}/remove")]
[BusinessGatewayOperationId("removeBusinessConsoleWmsWorkPoolMember")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status403Forbidden)]
public sealed class RemoveBusinessConsoleWmsWorkPoolMemberEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider,
    WmsTrustedRequestContextResolver trustedContextResolver)
    : BusinessConsoleWmsTrustedProxyEndpoint<BusinessConsoleRemoveWmsWorkPoolMemberRequest, BusinessConsoleWmsWorkPoolMemberRemovalResult>(
        auth,
        trustedContextResolver,
        BusinessGatewayPermissions.WmsWorkPoolsManage)
{
    protected override string OrganizationId(BusinessConsoleRemoveWmsWorkPoolMemberRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleRemoveWmsWorkPoolMemberRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleWmsWorkPoolMemberRemovalResult> ForwardAsync(
        BusinessConsoleRemoveWmsWorkPoolMemberRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var trusted = await ResolveTrustedContextAsync(request, cancellationToken);
        return await wms.RemoveWorkPoolMemberAsync(
            tokenProvider.BearerToken,
            new BusinessWmsRemoveWorkPoolMemberRequest(
                Route<string>("poolCode") ?? request.PoolCode,
                Route<string>("principalId") ?? request.PrincipalId,
                request.OrganizationId,
                request.EnvironmentId,
                trusted.ActorPrincipalId,
                trusted.AuthorizedSiteCodes),
            cancellationToken);
    }
}

public sealed class BusinessConsoleCreateWmsWorkPoolRequestValidator : Validator<BusinessConsoleCreateWmsWorkPoolRequest>
{
    public BusinessConsoleCreateWmsWorkPoolRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.SiteCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(150);
    }
}

public sealed class BusinessConsoleAddWmsWorkPoolMemberRequestValidator : Validator<BusinessConsoleAddWmsWorkPoolMemberRequest>
{
    public BusinessConsoleAddWmsWorkPoolMemberRequestValidator()
    {
        RuleFor(x => x.PoolCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PrincipalId).NotEmpty().MaximumLength(200);
    }
}

public sealed class BusinessConsoleRemoveWmsWorkPoolMemberRequestValidator : Validator<BusinessConsoleRemoveWmsWorkPoolMemberRequest>
{
    public BusinessConsoleRemoveWmsWorkPoolMemberRequestValidator()
    {
        RuleFor(x => x.PoolCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PrincipalId).NotEmpty().MaximumLength(200);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
    }
}
