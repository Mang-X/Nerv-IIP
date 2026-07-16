using FastEndpoints;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Telemetry;

[Tags("Business Console Telemetry")]
[HttpGet("/api/business-console/v1/telemetry/connectors/{connectorId}/collection-health")]
[BusinessGatewayOperationId("queryBusinessConsoleTelemetryConnectorCollectionHealth")]
public sealed class QueryBusinessConsoleTelemetryConnectorCollectionHealthEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessAppHubClient appHub,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleConnectorCollectionHealthRequest, BusinessConsoleConnectorCollectionHealthResponse>(auth, BusinessGatewayPermissions.IiotTelemetryRead)
{
    protected override string OrganizationId(BusinessConsoleConnectorCollectionHealthRequest request) => request.OrganizationId;
    protected override string EnvironmentId(BusinessConsoleConnectorCollectionHealthRequest request) => request.EnvironmentId;
    protected override string ResourceType(BusinessConsoleConnectorCollectionHealthRequest request) => "connector";
    protected override string ResourceId(BusinessConsoleConnectorCollectionHealthRequest request) => request.ConnectorId;
    protected override Task<BusinessConsoleConnectorCollectionHealthResponse> ForwardAsync(BusinessConsoleConnectorCollectionHealthRequest request, string bearerToken, CancellationToken cancellationToken) =>
        appHub.GetCollectionHealthAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Telemetry")]
[HttpGet("/api/business-console/v1/telemetry/connectors/collection-health")]
[BusinessGatewayOperationId("listBusinessConsoleTelemetryConnectorCollectionHealth")]
public sealed class ListBusinessConsoleTelemetryConnectorCollectionHealthEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessAppHubClient appHub,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleConnectorCollectionHealthListRequest, BusinessConsoleConnectorCollectionHealthListResponse>(auth, BusinessGatewayPermissions.IiotTelemetryRead)
{
    protected override string OrganizationId(BusinessConsoleConnectorCollectionHealthListRequest request) => request.OrganizationId;
    protected override string EnvironmentId(BusinessConsoleConnectorCollectionHealthListRequest request) => request.EnvironmentId;
    protected override Task<BusinessConsoleConnectorCollectionHealthListResponse> ForwardAsync(BusinessConsoleConnectorCollectionHealthListRequest request, string bearerToken, CancellationToken cancellationToken) =>
        appHub.GetCollectionHealthListAsync(tokenProvider.BearerToken, request, cancellationToken);
}
