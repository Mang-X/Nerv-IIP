using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.MasterData;

public sealed class BusinessConsoleResourceListRequest
{
    public string OrganizationId { get; set; } = string.Empty;

    public string EnvironmentId { get; set; } = string.Empty;

    public int? Take { get; set; }
}

public sealed record BusinessConsoleResourceListResponse(
    IReadOnlyCollection<BusinessConsoleResourceItem> Resources,
    int Total);

public sealed record BusinessConsoleResourceItem(
    string ResourceId,
    string Code,
    string Name);

[Tags("Business Console MasterData")]
[HttpGet("/api/business-console/v1/master-data/resources")]
[BusinessGatewayOperationId("listBusinessConsoleMasterDataResources")]
[Authorize(Policy = BusinessGatewayPolicies.BusinessConsoleAuthenticated)]
public sealed class ListBusinessConsoleMasterDataResourcesEndpoint : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        await ResponseDataEndpointResults.WriteErrorAsync(HttpContext, StatusCodes.Status501NotImplemented, "not-implemented", ct);
    }
}

[Tags("Business Console MasterData")]
[HttpGet("/api/business-console/v1/master-data/skus")]
[BusinessGatewayOperationId("listBusinessConsoleSkus")]
public sealed class ListBusinessConsoleSkusEndpoint(IBusinessGatewayAuthorizationClient auth)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleResourceListRequest, BusinessConsoleResourceListResponse>(
        auth,
        BusinessGatewayPermissions.MasterDataProductsRead)
{
    protected override string OrganizationId(BusinessConsoleResourceListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleResourceListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleResourceListResponse> ForwardAsync(
        BusinessConsoleResourceListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        Task.FromResult(new BusinessConsoleResourceListResponse([], 0));
}

[Tags("Business Console MasterData")]
[HttpPost("/api/business-console/v1/master-data/skus")]
[BusinessGatewayOperationId("createBusinessConsoleSku")]
[Authorize(Policy = BusinessGatewayPolicies.BusinessConsoleAuthenticated)]
public sealed class CreateBusinessConsoleSkuEndpoint : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        await ResponseDataEndpointResults.WriteErrorAsync(HttpContext, StatusCodes.Status501NotImplemented, "not-implemented", ct);
    }
}
