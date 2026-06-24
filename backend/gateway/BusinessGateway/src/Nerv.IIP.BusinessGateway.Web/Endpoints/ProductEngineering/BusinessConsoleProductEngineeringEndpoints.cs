using FastEndpoints;
using FluentValidation;
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
[HttpGet("/api/business-console/v1/engineering/documents")]
[BusinessGatewayOperationId("listBusinessConsoleEngineeringDocuments")]
public sealed class ListBusinessConsoleEngineeringDocumentsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessProductEngineeringClient engineering,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleListEngineeringDocumentsRequest, BusinessConsoleEngineeringDocumentListResponse>(
        auth,
        BusinessGatewayPermissions.EngineeringDocumentsRead)
{
    protected override string OrganizationId(BusinessConsoleListEngineeringDocumentsRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleListEngineeringDocumentsRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleEngineeringDocumentListResponse> ForwardAsync(
        BusinessConsoleListEngineeringDocumentsRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        engineering.ListEngineeringDocumentsAsync(tokenProvider.BearerToken, request, cancellationToken);
}

public sealed record BusinessConsoleGetEngineeringDocumentRequest(
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    [property: RouteParam] string DocumentNumber,
    [property: RouteParam] string Revision);

[Tags("Business Console Product Engineering")]
[HttpGet("/api/business-console/v1/engineering/documents/{documentNumber}/{revision}")]
[BusinessGatewayOperationId("getBusinessConsoleEngineeringDocument")]
public sealed class GetBusinessConsoleEngineeringDocumentEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessProductEngineeringClient engineering,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleGetEngineeringDocumentRequest, BusinessConsoleEngineeringDocumentItem>(
        auth,
        BusinessGatewayPermissions.EngineeringDocumentsRead)
{
    protected override string OrganizationId(BusinessConsoleGetEngineeringDocumentRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleGetEngineeringDocumentRequest request) => request.EnvironmentId;

    protected override string ResourceType(BusinessConsoleGetEngineeringDocumentRequest request) => "engineering-document";

    protected override string? ResourceId(BusinessConsoleGetEngineeringDocumentRequest request) => $"{request.DocumentNumber}:{request.Revision}";

    protected override Task<BusinessConsoleEngineeringDocumentItem> ForwardAsync(
        BusinessConsoleGetEngineeringDocumentRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        engineering.GetEngineeringDocumentAsync(tokenProvider.BearerToken, request.DocumentNumber, request.Revision, new BusinessConsoleEngineeringContextRequest(request.OrganizationId, request.EnvironmentId), cancellationToken);
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
[HttpGet("/api/business-console/v1/engineering/items")]
[BusinessGatewayOperationId("listBusinessConsoleEngineeringItems")]
public sealed class ListBusinessConsoleEngineeringItemsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessProductEngineeringClient engineering,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleListEngineeringItemsRequest, BusinessConsoleEngineeringItemListResponse>(
        auth,
        BusinessGatewayPermissions.EngineeringItemsRead)
{
    protected override string OrganizationId(BusinessConsoleListEngineeringItemsRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleListEngineeringItemsRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleEngineeringItemListResponse> ForwardAsync(
        BusinessConsoleListEngineeringItemsRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        engineering.ListEngineeringItemsAsync(tokenProvider.BearerToken, request, cancellationToken);
}

public sealed record BusinessConsoleGetEngineeringItemRequest(
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    [property: RouteParam] string ItemCode,
    [property: RouteParam] string Revision);

[Tags("Business Console Product Engineering")]
[HttpGet("/api/business-console/v1/engineering/items/{itemCode}/{revision}")]
[BusinessGatewayOperationId("getBusinessConsoleEngineeringItem")]
public sealed class GetBusinessConsoleEngineeringItemEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessProductEngineeringClient engineering,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleGetEngineeringItemRequest, BusinessConsoleEngineeringItemRevisionItem>(
        auth,
        BusinessGatewayPermissions.EngineeringItemsRead)
{
    protected override string OrganizationId(BusinessConsoleGetEngineeringItemRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleGetEngineeringItemRequest request) => request.EnvironmentId;

    protected override string ResourceType(BusinessConsoleGetEngineeringItemRequest request) => "engineering-item";

    protected override string? ResourceId(BusinessConsoleGetEngineeringItemRequest request) => $"{request.ItemCode}:{request.Revision}";

    protected override Task<BusinessConsoleEngineeringItemRevisionItem> ForwardAsync(
        BusinessConsoleGetEngineeringItemRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        engineering.GetEngineeringItemAsync(tokenProvider.BearerToken, request.ItemCode, request.Revision, new BusinessConsoleEngineeringContextRequest(request.OrganizationId, request.EnvironmentId), cancellationToken);
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

public sealed record BusinessConsoleGetEngineeringBomRequest(
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    [property: RouteParam] string BomCode,
    [property: RouteParam] string Revision);

[Tags("Business Console Product Engineering")]
[HttpGet("/api/business-console/v1/engineering/engineering-boms/{bomCode}/{revision}")]
[BusinessGatewayOperationId("getBusinessConsoleEngineeringBom")]
public sealed class GetBusinessConsoleEngineeringBomEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessProductEngineeringClient engineering,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleGetEngineeringBomRequest, BusinessConsoleEngineeringBomItem>(
        auth,
        BusinessGatewayPermissions.EngineeringBomsRead)
{
    protected override string OrganizationId(BusinessConsoleGetEngineeringBomRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleGetEngineeringBomRequest request) => request.EnvironmentId;

    protected override string ResourceType(BusinessConsoleGetEngineeringBomRequest request) => "engineering-bom";

    protected override string? ResourceId(BusinessConsoleGetEngineeringBomRequest request) => $"{request.BomCode}:{request.Revision}";

    protected override Task<BusinessConsoleEngineeringBomItem> ForwardAsync(
        BusinessConsoleGetEngineeringBomRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        engineering.GetEngineeringBomAsync(tokenProvider.BearerToken, request.BomCode, request.Revision, new BusinessConsoleEngineeringContextRequest(request.OrganizationId, request.EnvironmentId), cancellationToken);
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

public sealed record BusinessConsoleGetManufacturingBomRequest(
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    [property: RouteParam] string BomCode,
    [property: RouteParam] string Revision);

[Tags("Business Console Product Engineering")]
[HttpGet("/api/business-console/v1/engineering/manufacturing-boms/{bomCode}/{revision}")]
[BusinessGatewayOperationId("getBusinessConsoleEngineeringManufacturingBom")]
public sealed class GetBusinessConsoleEngineeringManufacturingBomEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessProductEngineeringClient engineering,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleGetManufacturingBomRequest, BusinessConsoleManufacturingBomItem>(
        auth,
        BusinessGatewayPermissions.EngineeringBomsRead)
{
    protected override string OrganizationId(BusinessConsoleGetManufacturingBomRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleGetManufacturingBomRequest request) => request.EnvironmentId;

    protected override string ResourceType(BusinessConsoleGetManufacturingBomRequest request) => "manufacturing-bom";

    protected override string? ResourceId(BusinessConsoleGetManufacturingBomRequest request) => $"{request.BomCode}:{request.Revision}";

    protected override Task<BusinessConsoleManufacturingBomItem> ForwardAsync(
        BusinessConsoleGetManufacturingBomRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        engineering.GetManufacturingBomAsync(tokenProvider.BearerToken, request.BomCode, request.Revision, new BusinessConsoleEngineeringContextRequest(request.OrganizationId, request.EnvironmentId), cancellationToken);
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

public sealed record BusinessConsoleGetRoutingRequest(
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    [property: RouteParam] string RoutingCode,
    [property: RouteParam] string Revision);

[Tags("Business Console Product Engineering")]
[HttpGet("/api/business-console/v1/engineering/routings/{routingCode}/{revision}")]
[BusinessGatewayOperationId("getBusinessConsoleEngineeringRouting")]
public sealed class GetBusinessConsoleEngineeringRoutingEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessProductEngineeringClient engineering,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleGetRoutingRequest, BusinessConsoleRoutingItem>(
        auth,
        BusinessGatewayPermissions.EngineeringRoutingsRead)
{
    protected override string OrganizationId(BusinessConsoleGetRoutingRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleGetRoutingRequest request) => request.EnvironmentId;

    protected override string ResourceType(BusinessConsoleGetRoutingRequest request) => "routing";

    protected override string? ResourceId(BusinessConsoleGetRoutingRequest request) => $"{request.RoutingCode}:{request.Revision}";

    protected override Task<BusinessConsoleRoutingItem> ForwardAsync(
        BusinessConsoleGetRoutingRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        engineering.GetRoutingAsync(tokenProvider.BearerToken, request.RoutingCode, request.Revision, new BusinessConsoleEngineeringContextRequest(request.OrganizationId, request.EnvironmentId), cancellationToken);
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
[HttpGet("/api/business-console/v1/engineering/standard-operations")]
[BusinessGatewayOperationId("listBusinessConsoleEngineeringStandardOperations")]
public sealed class ListBusinessConsoleEngineeringStandardOperationsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessProductEngineeringClient engineering,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleListStandardOperationsRequest, BusinessConsoleStandardOperationListResponse>(
        auth,
        BusinessGatewayPermissions.EngineeringStandardOperationsRead)
{
    protected override string OrganizationId(BusinessConsoleListStandardOperationsRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleListStandardOperationsRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleStandardOperationListResponse> ForwardAsync(
        BusinessConsoleListStandardOperationsRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        engineering.ListStandardOperationsAsync(tokenProvider.BearerToken, request, cancellationToken);
}

public sealed record BusinessConsoleGetStandardOperationRequest(
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    [property: RouteParam] string OperationCode);

[Tags("Business Console Product Engineering")]
[HttpGet("/api/business-console/v1/engineering/standard-operations/{operationCode}")]
[BusinessGatewayOperationId("getBusinessConsoleEngineeringStandardOperation")]
public sealed class GetBusinessConsoleEngineeringStandardOperationEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessProductEngineeringClient engineering,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleGetStandardOperationRequest, BusinessConsoleStandardOperationItem>(
        auth,
        BusinessGatewayPermissions.EngineeringStandardOperationsRead)
{
    protected override string OrganizationId(BusinessConsoleGetStandardOperationRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleGetStandardOperationRequest request) => request.EnvironmentId;

    protected override string ResourceType(BusinessConsoleGetStandardOperationRequest request) => "standard-operation";

    protected override string? ResourceId(BusinessConsoleGetStandardOperationRequest request) => request.OperationCode;

    protected override Task<BusinessConsoleStandardOperationItem> ForwardAsync(
        BusinessConsoleGetStandardOperationRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        engineering.GetStandardOperationAsync(tokenProvider.BearerToken, request.OperationCode, new BusinessConsoleEngineeringContextRequest(request.OrganizationId, request.EnvironmentId), cancellationToken);
}

[Tags("Business Console Product Engineering")]
[HttpPost("/api/business-console/v1/engineering/standard-operations")]
[BusinessGatewayOperationId("createBusinessConsoleEngineeringStandardOperation")]
public sealed class CreateBusinessConsoleEngineeringStandardOperationEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessProductEngineeringClient engineering,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateStandardOperationRequest, BusinessConsoleStandardOperationResponse>(
        auth,
        BusinessGatewayPermissions.EngineeringStandardOperationsManage)
{
    protected override string OrganizationId(BusinessConsoleCreateStandardOperationRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateStandardOperationRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleStandardOperationResponse> ForwardAsync(
        BusinessConsoleCreateStandardOperationRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        engineering.CreateStandardOperationAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Product Engineering")]
[HttpPut("/api/business-console/v1/engineering/standard-operations/{operationCode}")]
[BusinessGatewayOperationId("updateBusinessConsoleEngineeringStandardOperation")]
public sealed class UpdateBusinessConsoleEngineeringStandardOperationEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessProductEngineeringClient engineering,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleUpdateStandardOperationRequest, BusinessConsoleStandardOperationResponse>(
        auth,
        BusinessGatewayPermissions.EngineeringStandardOperationsManage)
{
    protected override string OrganizationId(BusinessConsoleUpdateStandardOperationRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleUpdateStandardOperationRequest request) => request.EnvironmentId;

    protected override string ResourceType(BusinessConsoleUpdateStandardOperationRequest request) => "standard-operation";

    protected override string? ResourceId(BusinessConsoleUpdateStandardOperationRequest request) => Route<string>("operationCode") ?? request.OperationCode;

    protected override Task<BusinessConsoleStandardOperationResponse> ForwardAsync(
        BusinessConsoleUpdateStandardOperationRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var operationCode = Route<string>("operationCode") ?? request.OperationCode;
        return engineering.UpdateStandardOperationAsync(
            tokenProvider.BearerToken,
            operationCode,
            request with { OperationCode = operationCode },
            cancellationToken);
    }
}

[Tags("Business Console Product Engineering")]
[HttpPost("/api/business-console/v1/engineering/standard-operations/{operationCode}/archive")]
[BusinessGatewayOperationId("archiveBusinessConsoleEngineeringStandardOperation")]
public sealed class ArchiveBusinessConsoleEngineeringStandardOperationEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessProductEngineeringClient engineering,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleArchiveStandardOperationRequest, BusinessConsoleAcceptedResponse>(
        auth,
        BusinessGatewayPermissions.EngineeringStandardOperationsManage)
{
    protected override string OrganizationId(BusinessConsoleArchiveStandardOperationRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleArchiveStandardOperationRequest request) => request.EnvironmentId;

    protected override string ResourceType(BusinessConsoleArchiveStandardOperationRequest request) => "standard-operation";

    protected override string? ResourceId(BusinessConsoleArchiveStandardOperationRequest request) => Route<string>("operationCode") ?? request.OperationCode;

    protected override Task<BusinessConsoleAcceptedResponse> ForwardAsync(
        BusinessConsoleArchiveStandardOperationRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var operationCode = Route<string>("operationCode") ?? request.OperationCode;
        return engineering.ArchiveStandardOperationAsync(
            tokenProvider.BearerToken,
            operationCode,
            request with { OperationCode = operationCode },
            cancellationToken);
    }
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
[HttpGet("/api/business-console/v1/engineering/engineering-changes")]
[BusinessGatewayOperationId("listBusinessConsoleEngineeringChanges")]
public sealed class ListBusinessConsoleEngineeringChangesEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessProductEngineeringClient engineering,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleListEngineeringChangesRequest, BusinessConsoleEngineeringChangeListResponse>(
        auth,
        BusinessGatewayPermissions.EngineeringChangesRead)
{
    protected override string OrganizationId(BusinessConsoleListEngineeringChangesRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleListEngineeringChangesRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleEngineeringChangeListResponse> ForwardAsync(
        BusinessConsoleListEngineeringChangesRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        engineering.ListEngineeringChangesAsync(tokenProvider.BearerToken, request, cancellationToken);
}

public sealed record BusinessConsoleGetEngineeringChangeRequest(
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    [property: RouteParam] string ChangeNumber);

[Tags("Business Console Product Engineering")]
[HttpGet("/api/business-console/v1/engineering/engineering-changes/{changeNumber}")]
[BusinessGatewayOperationId("getBusinessConsoleEngineeringChange")]
public sealed class GetBusinessConsoleEngineeringChangeEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessProductEngineeringClient engineering,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleGetEngineeringChangeRequest, BusinessConsoleEngineeringChangeItem>(
        auth,
        BusinessGatewayPermissions.EngineeringChangesRead)
{
    protected override string OrganizationId(BusinessConsoleGetEngineeringChangeRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleGetEngineeringChangeRequest request) => request.EnvironmentId;

    protected override string ResourceType(BusinessConsoleGetEngineeringChangeRequest request) => "engineering-change";

    protected override string? ResourceId(BusinessConsoleGetEngineeringChangeRequest request) => request.ChangeNumber;

    protected override Task<BusinessConsoleEngineeringChangeItem> ForwardAsync(
        BusinessConsoleGetEngineeringChangeRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        engineering.GetEngineeringChangeAsync(tokenProvider.BearerToken, request.ChangeNumber, new BusinessConsoleEngineeringContextRequest(request.OrganizationId, request.EnvironmentId), cancellationToken);
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

public sealed class BusinessConsoleRegisterEngineeringDocumentRequestValidator : Validator<BusinessConsoleRegisterEngineeringDocumentRequest>
{
    public BusinessConsoleRegisterEngineeringDocumentRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DocumentNumber).MaximumLength(100);
        RuleFor(x => x.Revision).NotEmpty().MaximumLength(50);
        RuleFor(x => x.FileId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(255);
        RuleFor(x => x.ContentType).NotEmpty().MaximumLength(120);
        RuleFor(x => x.DocumentType).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ItemCode).MaximumLength(100);
    }
}

public sealed class BusinessConsoleCreateEngineeringItemRevisionRequestValidator : Validator<BusinessConsoleCreateEngineeringItemRevisionRequest>
{
    public BusinessConsoleCreateEngineeringItemRevisionRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ItemCode).MaximumLength(100);
        RuleFor(x => x.Revision).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(255);
    }
}

public sealed class BusinessConsoleReleaseEngineeringBomRequestValidator : Validator<BusinessConsoleReleaseEngineeringBomRequest>
{
    public BusinessConsoleReleaseEngineeringBomRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.BomCode).MaximumLength(100);
        RuleFor(x => x.Revision).NotEmpty().MaximumLength(50);
        RuleFor(x => x.ParentItemCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(x => x.ComponentCode).NotEmpty().MaximumLength(100);
            line.RuleFor(x => x.Quantity).GreaterThan(0);
            line.RuleFor(x => x.UnitOfMeasureCode).NotEmpty().MaximumLength(50);
        });
    }
}

public sealed class BusinessConsoleReleaseManufacturingBomRequestValidator : Validator<BusinessConsoleReleaseManufacturingBomRequest>
{
    public BusinessConsoleReleaseManufacturingBomRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.BomCode).MaximumLength(100);
        RuleFor(x => x.Revision).NotEmpty().MaximumLength(50);
        RuleFor(x => x.SkuCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EngineeringBomCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EngineeringBomRevision).NotEmpty().MaximumLength(50);
        RuleFor(x => x.MaterialLines).NotEmpty();
        RuleForEach(x => x.MaterialLines).ChildRules(line =>
        {
            line.RuleFor(x => x.SkuCode).NotEmpty().MaximumLength(100);
            line.RuleFor(x => x.Quantity).GreaterThan(0);
            line.RuleFor(x => x.UnitOfMeasureCode).NotEmpty().MaximumLength(50);
            line.RuleFor(x => x.ScrapRate).GreaterThanOrEqualTo(0);
        });
        RuleForEach(x => x.RecipeLines).ChildRules(line =>
        {
            line.RuleFor(x => x.ParameterCode).NotEmpty().MaximumLength(100);
            line.RuleFor(x => x.TargetValue).NotEmpty().MaximumLength(200);
            line.RuleFor(x => x.UnitOfMeasureCode).NotEmpty().MaximumLength(50);
        });
    }
}

public sealed class BusinessConsoleReleaseRoutingRequestValidator : Validator<BusinessConsoleReleaseRoutingRequest>
{
    public BusinessConsoleReleaseRoutingRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.RoutingCode).MaximumLength(100);
        RuleFor(x => x.Revision).NotEmpty().MaximumLength(50);
        RuleFor(x => x.SkuCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Operations).NotEmpty();
        RuleForEach(x => x.Operations).ChildRules(operation =>
        {
            operation.RuleFor(x => x.Sequence).GreaterThan(0);
            operation.RuleFor(x => x.WorkCenterCode).NotEmpty().MaximumLength(100);
            operation.RuleFor(x => x.OperationCode).Must(value => !string.IsNullOrWhiteSpace(value)).MaximumLength(100);
            operation.RuleFor(x => x.OperationName).Must(value => !string.IsNullOrWhiteSpace(value)).MaximumLength(200);
            operation.RuleFor(x => x.StandardMinutes).GreaterThan(0);
        });
    }
}

public sealed class BusinessConsoleReleaseEngineeringChangeRequestValidator : Validator<BusinessConsoleReleaseEngineeringChangeRequest>
{
    public BusinessConsoleReleaseEngineeringChangeRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ChangeNumber).MaximumLength(100);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.ApprovalReferenceId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.AffectedVersions).NotEmpty();
        RuleForEach(x => x.AffectedVersions).ChildRules(version =>
        {
            version.RuleFor(x => x.VersionKind).NotEmpty().MaximumLength(100);
            version.RuleFor(x => x.VersionId).NotEmpty().MaximumLength(150);
            version.RuleFor(x => x.SupersededByVersionId).MaximumLength(150);
        });
    }
}

public sealed class BusinessConsoleCreateStandardOperationRequestValidator : Validator<BusinessConsoleCreateStandardOperationRequest>
{
    public BusinessConsoleCreateStandardOperationRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.OperationCode).MaximumLength(100);
        RuleFor(x => x.IdempotencyKey).MaximumLength(150);
        RuleFor(x => x.OperationName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.DefaultWorkCenterCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.StandardSetupMinutes).GreaterThanOrEqualTo(0);
        RuleFor(x => x.StandardRunMinutes).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ControlKey).NotEmpty().MaximumLength(100);
    }
}

public sealed class BusinessConsoleUpdateStandardOperationRequestValidator : Validator<BusinessConsoleUpdateStandardOperationRequest>
{
    public BusinessConsoleUpdateStandardOperationRequestValidator()
    {
        RuleFor(x => x.OperationCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.OperationName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.DefaultWorkCenterCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.StandardSetupMinutes).GreaterThanOrEqualTo(0);
        RuleFor(x => x.StandardRunMinutes).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ControlKey).NotEmpty().MaximumLength(100);
    }
}

public sealed class BusinessConsoleArchiveStandardOperationRequestValidator : Validator<BusinessConsoleArchiveStandardOperationRequest>
{
    public BusinessConsoleArchiveStandardOperationRequestValidator()
    {
        RuleFor(x => x.OperationCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public sealed class BusinessConsoleCreateProductionVersionRequestValidator : Validator<BusinessConsoleCreateProductionVersionRequest>
{
    public BusinessConsoleCreateProductionVersionRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SkuCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.MbomVersionId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.RoutingVersionId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Priority).GreaterThanOrEqualTo(0);
        RuleFor(x => x.LotSizeMin).GreaterThanOrEqualTo(0).When(x => x.LotSizeMin.HasValue);
        RuleFor(x => x.LotSizeMax).GreaterThanOrEqualTo(0).When(x => x.LotSizeMax.HasValue);
    }
}

public sealed class BusinessConsoleUpdateProductionVersionRequestValidator : Validator<BusinessConsoleUpdateProductionVersionRequest>
{
    public BusinessConsoleUpdateProductionVersionRequestValidator()
    {
        RuleFor(x => x.ProductionVersionId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.MbomVersionId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.RoutingVersionId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Priority).GreaterThanOrEqualTo(0);
        RuleFor(x => x.LotSizeMin).GreaterThanOrEqualTo(0).When(x => x.LotSizeMin.HasValue);
        RuleFor(x => x.LotSizeMax).GreaterThanOrEqualTo(0).When(x => x.LotSizeMax.HasValue);
    }
}

public sealed class BusinessConsoleArchiveProductionVersionRequestValidator : Validator<BusinessConsoleArchiveProductionVersionRequest>
{
    public BusinessConsoleArchiveProductionVersionRequestValidator()
    {
        RuleFor(x => x.ProductionVersionId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}
