using FastEndpoints;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.ProductEngineering;

[Tags("Business Console Product Engineering")]
[HttpGet("/api/business-console/v1/engineering/engineering-boms")]
[BusinessGatewayOperationId("listBusinessConsoleEngineeringBoms")]
public sealed class ListBusinessConsoleEngineeringBomsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessProductEngineeringClient engineering,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleListEngineeringBomsRequest, BusinessConsoleEngineeringBomListResponse>(
        auth,
        BusinessGatewayPermissions.EngineeringBomsRead)
{
    protected override string OrganizationId(BusinessConsoleListEngineeringBomsRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleListEngineeringBomsRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleEngineeringBomListResponse> ForwardAsync(
        BusinessConsoleListEngineeringBomsRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        engineering.ListEngineeringBomsAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Product Engineering")]
[HttpGet("/api/business-console/v1/engineering/routings")]
[BusinessGatewayOperationId("listBusinessConsoleEngineeringRoutings")]
public sealed class ListBusinessConsoleEngineeringRoutingsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessProductEngineeringClient engineering,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleListRoutingsRequest, BusinessConsoleRoutingListResponse>(
        auth,
        BusinessGatewayPermissions.EngineeringRoutingsRead)
{
    protected override string OrganizationId(BusinessConsoleListRoutingsRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleListRoutingsRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleRoutingListResponse> ForwardAsync(
        BusinessConsoleListRoutingsRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        engineering.ListRoutingsAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Product Engineering")]
[HttpGet("/api/business-console/v1/engineering/production-versions")]
[BusinessGatewayOperationId("listBusinessConsoleEngineeringProductionVersions")]
public sealed class ListBusinessConsoleEngineeringProductionVersionsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessProductEngineeringClient engineering,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleListProductionVersionsRequest, BusinessConsoleProductionVersionListResponse>(
        auth,
        BusinessGatewayPermissions.EngineeringProductionVersionsRead)
{
    protected override string OrganizationId(BusinessConsoleListProductionVersionsRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleListProductionVersionsRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleProductionVersionListResponse> ForwardAsync(
        BusinessConsoleListProductionVersionsRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        engineering.ListProductionVersionsAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Product Engineering")]
[HttpGet("/api/business-console/v1/engineering/production-versions/resolve")]
[BusinessGatewayOperationId("resolveBusinessConsoleEngineeringProductionVersion")]
public sealed class ResolveBusinessConsoleEngineeringProductionVersionEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessProductEngineeringClient engineering,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleResolveProductionVersionRequest, BusinessConsoleResolveProductionVersionResponse>(
        auth,
        BusinessGatewayPermissions.EngineeringProductionVersionsRead)
{
    protected override string OrganizationId(BusinessConsoleResolveProductionVersionRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleResolveProductionVersionRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleResolveProductionVersionResponse> ForwardAsync(
        BusinessConsoleResolveProductionVersionRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        engineering.ResolveProductionVersionAsync(tokenProvider.BearerToken, request, cancellationToken);
}
