using FastEndpoints;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.ProductEngineering;

[Tags("Business Console Product Engineering")]
[HttpPost("/api/business-console/v1/engineering/documents")]
[BusinessGatewayOperationId("registerBusinessConsoleEngineeringDocument")]
public sealed class RegisterBusinessConsoleEngineeringDocumentEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessProductEngineeringClient engineering,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleRegisterEngineeringDocumentRequest, BusinessConsoleEngineeringEntityResponse>(
        auth,
        BusinessGatewayPermissions.EngineeringDocumentsManage)
{
    protected override string OrganizationId(BusinessConsoleRegisterEngineeringDocumentRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleRegisterEngineeringDocumentRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleEngineeringEntityResponse> ForwardAsync(
        BusinessConsoleRegisterEngineeringDocumentRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        engineering.RegisterEngineeringDocumentAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Product Engineering")]
[HttpPost("/api/business-console/v1/engineering/items")]
[BusinessGatewayOperationId("createBusinessConsoleEngineeringItemRevision")]
public sealed class CreateBusinessConsoleEngineeringItemRevisionEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessProductEngineeringClient engineering,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateEngineeringItemRevisionRequest, BusinessConsoleEngineeringEntityResponse>(
        auth,
        BusinessGatewayPermissions.EngineeringItemsManage)
{
    protected override string OrganizationId(BusinessConsoleCreateEngineeringItemRevisionRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateEngineeringItemRevisionRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleEngineeringEntityResponse> ForwardAsync(
        BusinessConsoleCreateEngineeringItemRevisionRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        engineering.CreateEngineeringItemRevisionAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Product Engineering")]
[HttpPost("/api/business-console/v1/engineering/engineering-boms/release")]
[BusinessGatewayOperationId("releaseBusinessConsoleEngineeringBom")]
public sealed class ReleaseBusinessConsoleEngineeringBomEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessProductEngineeringClient engineering,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleReleaseEngineeringBomRequest, BusinessConsoleEngineeringEntityResponse>(
        auth,
        BusinessGatewayPermissions.EngineeringBomsManage)
{
    protected override string OrganizationId(BusinessConsoleReleaseEngineeringBomRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleReleaseEngineeringBomRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleEngineeringEntityResponse> ForwardAsync(
        BusinessConsoleReleaseEngineeringBomRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        engineering.ReleaseEngineeringBomAsync(tokenProvider.BearerToken, request, cancellationToken);
}

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
[HttpGet("/api/business-console/v1/engineering/manufacturing-boms")]
[BusinessGatewayOperationId("listBusinessConsoleEngineeringManufacturingBoms")]
public sealed class ListBusinessConsoleEngineeringManufacturingBomsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessProductEngineeringClient engineering,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleListManufacturingBomsRequest, BusinessConsoleManufacturingBomListResponse>(
        auth,
        BusinessGatewayPermissions.EngineeringBomsRead)
{
    protected override string OrganizationId(BusinessConsoleListManufacturingBomsRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleListManufacturingBomsRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleManufacturingBomListResponse> ForwardAsync(
        BusinessConsoleListManufacturingBomsRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        engineering.ListManufacturingBomsAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Product Engineering")]
[HttpPost("/api/business-console/v1/engineering/manufacturing-boms/release")]
[BusinessGatewayOperationId("releaseBusinessConsoleEngineeringManufacturingBom")]
public sealed class ReleaseBusinessConsoleEngineeringManufacturingBomEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessProductEngineeringClient engineering,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleReleaseManufacturingBomRequest, BusinessConsoleEngineeringEntityResponse>(
        auth,
        BusinessGatewayPermissions.EngineeringBomsManage)
{
    protected override string OrganizationId(BusinessConsoleReleaseManufacturingBomRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleReleaseManufacturingBomRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleEngineeringEntityResponse> ForwardAsync(
        BusinessConsoleReleaseManufacturingBomRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        engineering.ReleaseManufacturingBomAsync(tokenProvider.BearerToken, request, cancellationToken);
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
[HttpPost("/api/business-console/v1/engineering/routings/release")]
[BusinessGatewayOperationId("releaseBusinessConsoleEngineeringRouting")]
public sealed class ReleaseBusinessConsoleEngineeringRoutingEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessProductEngineeringClient engineering,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleReleaseRoutingRequest, BusinessConsoleEngineeringEntityResponse>(
        auth,
        BusinessGatewayPermissions.EngineeringRoutingsManage)
{
    protected override string OrganizationId(BusinessConsoleReleaseRoutingRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleReleaseRoutingRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleEngineeringEntityResponse> ForwardAsync(
        BusinessConsoleReleaseRoutingRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        engineering.ReleaseRoutingAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Product Engineering")]
[HttpPost("/api/business-console/v1/engineering/engineering-changes/release")]
[BusinessGatewayOperationId("releaseBusinessConsoleEngineeringChange")]
public sealed class ReleaseBusinessConsoleEngineeringChangeEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessProductEngineeringClient engineering,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleReleaseEngineeringChangeRequest, BusinessConsoleEngineeringEntityResponse>(
        auth,
        BusinessGatewayPermissions.EngineeringChangesManage)
{
    protected override string OrganizationId(BusinessConsoleReleaseEngineeringChangeRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleReleaseEngineeringChangeRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleEngineeringEntityResponse> ForwardAsync(
        BusinessConsoleReleaseEngineeringChangeRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        engineering.ReleaseEngineeringChangeAsync(tokenProvider.BearerToken, request, cancellationToken);
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
[HttpPost("/api/business-console/v1/engineering/production-versions")]
[BusinessGatewayOperationId("createBusinessConsoleEngineeringProductionVersion")]
public sealed class CreateBusinessConsoleEngineeringProductionVersionEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessProductEngineeringClient engineering,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateProductionVersionRequest, BusinessConsoleCreateProductionVersionResponse>(
        auth,
        BusinessGatewayPermissions.EngineeringProductionVersionsManage)
{
    protected override string OrganizationId(BusinessConsoleCreateProductionVersionRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateProductionVersionRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleCreateProductionVersionResponse> ForwardAsync(
        BusinessConsoleCreateProductionVersionRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        engineering.CreateProductionVersionAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Product Engineering")]
[HttpPut("/api/business-console/v1/engineering/production-versions/{productionVersionId}")]
[BusinessGatewayOperationId("updateBusinessConsoleEngineeringProductionVersion")]
public sealed class UpdateBusinessConsoleEngineeringProductionVersionEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessProductEngineeringClient engineering,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleUpdateProductionVersionRequest, BusinessConsoleCreateProductionVersionResponse>(
        auth,
        BusinessGatewayPermissions.EngineeringProductionVersionsManage)
{
    protected override string OrganizationId(BusinessConsoleUpdateProductionVersionRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleUpdateProductionVersionRequest request) => request.EnvironmentId;

    protected override string ResourceType(BusinessConsoleUpdateProductionVersionRequest request) => "production-version";

    protected override string? ResourceId(BusinessConsoleUpdateProductionVersionRequest request) => Route<string>("productionVersionId") ?? request.ProductionVersionId;

    protected override Task<BusinessConsoleCreateProductionVersionResponse> ForwardAsync(
        BusinessConsoleUpdateProductionVersionRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var productionVersionId = Route<string>("productionVersionId") ?? request.ProductionVersionId;
        return engineering.UpdateProductionVersionAsync(
            tokenProvider.BearerToken,
            productionVersionId,
            request with { ProductionVersionId = productionVersionId },
            cancellationToken);
    }
}

[Tags("Business Console Product Engineering")]
[HttpPost("/api/business-console/v1/engineering/production-versions/{productionVersionId}/archive")]
[BusinessGatewayOperationId("archiveBusinessConsoleEngineeringProductionVersion")]
public sealed class ArchiveBusinessConsoleEngineeringProductionVersionEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessProductEngineeringClient engineering,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleArchiveProductionVersionRequest, BusinessConsoleAcceptedResponse>(
        auth,
        BusinessGatewayPermissions.EngineeringProductionVersionsManage)
{
    protected override string OrganizationId(BusinessConsoleArchiveProductionVersionRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleArchiveProductionVersionRequest request) => request.EnvironmentId;

    protected override string ResourceType(BusinessConsoleArchiveProductionVersionRequest request) => "production-version";

    protected override string? ResourceId(BusinessConsoleArchiveProductionVersionRequest request) => Route<string>("productionVersionId") ?? request.ProductionVersionId;

    protected override Task<BusinessConsoleAcceptedResponse> ForwardAsync(
        BusinessConsoleArchiveProductionVersionRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var productionVersionId = Route<string>("productionVersionId") ?? request.ProductionVersionId;
        return engineering.ArchiveProductionVersionAsync(
            tokenProvider.BearerToken,
            productionVersionId,
            request with { ProductionVersionId = productionVersionId },
            cancellationToken);
    }
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
