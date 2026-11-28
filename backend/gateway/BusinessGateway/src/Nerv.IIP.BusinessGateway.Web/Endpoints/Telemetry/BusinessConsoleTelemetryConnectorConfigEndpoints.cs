using FastEndpoints;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.Contracts.IndustrialTelemetry;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Telemetry;

public sealed record BusinessConsoleListTelemetryConnectorConfigsRequest(string OrganizationId, string EnvironmentId);
public sealed record BusinessConsoleSaveTelemetryConnectorConfigRequest(string OrganizationId, string EnvironmentId, TelemetryConnectorConfigInput Config);
public sealed record BusinessConsoleDeleteTelemetryConnectorConfigRequest(string OrganizationId, string EnvironmentId, string ConnectorId);
public sealed record BusinessConsoleTestTelemetryConnectorConnectionRequest(string OrganizationId, string EnvironmentId, TelemetryConnectionTestInput Connection);

[Tags("Business Console Telemetry")]
[HttpGet("/api/business-console/v1/telemetry/connector-configs")]
[BusinessGatewayOperationId("listBusinessConsoleTelemetryConnectorConfigs")]
public sealed class ListBusinessConsoleTelemetryConnectorConfigsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    HttpBusinessIndustrialTelemetryClient telemetry,
    IInternalServiceTokenProvider token)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleListTelemetryConnectorConfigsRequest, TelemetryConnectorConfigItem[]>(
        auth, BusinessGatewayPermissions.IiotTelemetryRead)
{
    protected override string OrganizationId(BusinessConsoleListTelemetryConnectorConfigsRequest request) => request.OrganizationId;
    protected override string EnvironmentId(BusinessConsoleListTelemetryConnectorConfigsRequest request) => request.EnvironmentId;

    protected override Task<TelemetryConnectorConfigItem[]> ForwardAsync(
        BusinessConsoleListTelemetryConnectorConfigsRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        telemetry.ListConnectorConfigsAsync(token.BearerToken, request.OrganizationId, request.EnvironmentId, cancellationToken);
}

[Tags("Business Console Telemetry")]
[HttpPost("/api/business-console/v1/telemetry/connector-configs")]
[BusinessGatewayOperationId("saveBusinessConsoleTelemetryConnectorConfig")]
public sealed class SaveBusinessConsoleTelemetryConnectorConfigEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    HttpBusinessIndustrialTelemetryClient telemetry,
    IInternalServiceTokenProvider token)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleSaveTelemetryConnectorConfigRequest, TelemetryConnectorConfigItem>(
        auth, BusinessGatewayPermissions.IiotTagsManage)
{
    protected override string OrganizationId(BusinessConsoleSaveTelemetryConnectorConfigRequest request) => request.OrganizationId;
    protected override string EnvironmentId(BusinessConsoleSaveTelemetryConnectorConfigRequest request) => request.EnvironmentId;

    protected override Task<TelemetryConnectorConfigItem> ForwardAsync(
        BusinessConsoleSaveTelemetryConnectorConfigRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        telemetry.SaveConnectorConfigAsync(token.BearerToken, request.OrganizationId, request.EnvironmentId, request.Config, cancellationToken);
}

[Tags("Business Console Telemetry")]
[HttpDelete("/api/business-console/v1/telemetry/connector-configs/{connectorId}")]
[BusinessGatewayOperationId("deleteBusinessConsoleTelemetryConnectorConfig")]
public sealed class DeleteBusinessConsoleTelemetryConnectorConfigEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    HttpBusinessIndustrialTelemetryClient telemetry,
    IInternalServiceTokenProvider token)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleDeleteTelemetryConnectorConfigRequest, bool>(
        auth, BusinessGatewayPermissions.IiotTagsManage)
{
    protected override string OrganizationId(BusinessConsoleDeleteTelemetryConnectorConfigRequest request) => request.OrganizationId;
    protected override string EnvironmentId(BusinessConsoleDeleteTelemetryConnectorConfigRequest request) => request.EnvironmentId;

    protected override Task<bool> ForwardAsync(
        BusinessConsoleDeleteTelemetryConnectorConfigRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        telemetry.DeleteConnectorConfigAsync(token.BearerToken, request.OrganizationId, request.EnvironmentId, request.ConnectorId, cancellationToken);
}

[Tags("Business Console Telemetry")]
[HttpPost("/api/business-console/v1/telemetry/connector-configs/test-connection")]
[BusinessGatewayOperationId("testBusinessConsoleTelemetryConnectorConnection")]
public sealed class TestBusinessConsoleTelemetryConnectorConnectionEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    HttpBusinessIndustrialTelemetryClient telemetry,
    IInternalServiceTokenProvider token)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleTestTelemetryConnectorConnectionRequest, TelemetryConnectionTestResult>(
        auth, BusinessGatewayPermissions.IiotTagsManage)
{
    protected override string OrganizationId(BusinessConsoleTestTelemetryConnectorConnectionRequest request) => request.OrganizationId;
    protected override string EnvironmentId(BusinessConsoleTestTelemetryConnectorConnectionRequest request) => request.EnvironmentId;

    protected override Task<TelemetryConnectionTestResult> ForwardAsync(
        BusinessConsoleTestTelemetryConnectorConnectionRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        telemetry.TestConnectorConnectionAsync(token.BearerToken, request.Connection, cancellationToken);
}
