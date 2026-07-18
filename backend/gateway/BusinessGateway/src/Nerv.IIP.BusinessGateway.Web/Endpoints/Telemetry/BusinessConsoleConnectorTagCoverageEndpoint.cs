using FastEndpoints;
using FluentValidation;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Telemetry;

[Tags("Business Console Telemetry")]
[HttpGet("/api/business-console/v1/telemetry/connectors/{connectorId}/tag-coverage")]
[BusinessGatewayOperationId("getBusinessConsoleTelemetryConnectorTagCoverage")]
public sealed class GetBusinessConsoleTelemetryConnectorTagCoverageEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessIndustrialTelemetryClient telemetry,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleConnectorTagCoverageRequest, BusinessConsoleConnectorTagCoverageResponse>(
        auth,
        BusinessGatewayPermissions.IiotTelemetryRead)
{
    protected override string OrganizationId(BusinessConsoleConnectorTagCoverageRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleConnectorTagCoverageRequest request) => request.EnvironmentId;

    protected override string ResourceType(BusinessConsoleConnectorTagCoverageRequest request) => "connector";

    protected override string ResourceId(BusinessConsoleConnectorTagCoverageRequest request) => request.ConnectorId;

    protected override Task<BusinessConsoleConnectorTagCoverageResponse> ForwardAsync(
        BusinessConsoleConnectorTagCoverageRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        telemetry.GetConnectorTagCoverageAsync(tokenProvider.BearerToken, request, cancellationToken);
}

public sealed class BusinessConsoleConnectorTagCoverageRequestValidator
    : Validator<BusinessConsoleConnectorTagCoverageRequest>
{
    public BusinessConsoleConnectorTagCoverageRequestValidator()
    {
        RuleFor(x => x.ConnectorId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
    }
}
