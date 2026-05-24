using FastEndpoints;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Quality;

[Tags("Business Console Quality")]
[HttpGet("/api/business-console/v1/quality/inspection-plans")]
[BusinessGatewayOperationId("listBusinessConsoleQualityInspectionPlans")]
public sealed class ListBusinessConsoleQualityInspectionPlansEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessQualityClient quality,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleQualityListRequest, BusinessConsoleQualityListResponse>(
        auth,
        BusinessGatewayPermissions.QualityInspectionRecordsRead)
{
    protected override string OrganizationId(BusinessConsoleQualityListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleQualityListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleQualityListResponse> ForwardAsync(
        BusinessConsoleQualityListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        quality.ListInspectionPlansAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Quality")]
[HttpPost("/api/business-console/v1/quality/inspection-records")]
[BusinessGatewayOperationId("createBusinessConsoleQualityInspectionRecord")]
public sealed class CreateBusinessConsoleQualityInspectionRecordEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessQualityClient quality,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateInspectionRecordRequest, BusinessConsoleCreateInspectionRecordResponse>(
        auth,
        BusinessGatewayPermissions.QualityInspectionRecordsCreate)
{
    protected override string OrganizationId(BusinessConsoleCreateInspectionRecordRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateInspectionRecordRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleCreateInspectionRecordResponse> ForwardAsync(
        BusinessConsoleCreateInspectionRecordRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        quality.CreateInspectionRecordAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Quality")]
[HttpGet("/api/business-console/v1/quality/ncrs")]
[BusinessGatewayOperationId("listBusinessConsoleQualityNcrs")]
public sealed class ListBusinessConsoleQualityNcrsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessQualityClient quality,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleQualityListRequest, BusinessConsoleQualityListResponse>(
        auth,
        BusinessGatewayPermissions.QualityNcrRead)
{
    protected override string OrganizationId(BusinessConsoleQualityListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleQualityListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleQualityListResponse> ForwardAsync(
        BusinessConsoleQualityListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        quality.ListNcrsAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Quality")]
[HttpPost("/api/business-console/v1/quality/ncrs/{ncrId}/disposition")]
[BusinessGatewayOperationId("submitBusinessConsoleQualityNcrDisposition")]
public sealed class SubmitBusinessConsoleQualityNcrDispositionEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessQualityClient quality,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleNcrDispositionRequest, BusinessConsoleAcceptedResponse>(
        auth,
        BusinessGatewayPermissions.QualityNcrManage)
{
    protected override string OrganizationId(BusinessConsoleNcrDispositionRequest request) =>
        HttpContext.Request.Query["organizationId"].ToString();

    protected override string EnvironmentId(BusinessConsoleNcrDispositionRequest request) =>
        HttpContext.Request.Query["environmentId"].ToString();

    protected override string? ResourceType(BusinessConsoleNcrDispositionRequest request) => "ncr";

    protected override string? ResourceId(BusinessConsoleNcrDispositionRequest request) =>
        Route<string>("ncrId") ?? request.NcrId;

    protected override Task<BusinessConsoleAcceptedResponse> ForwardAsync(
        BusinessConsoleNcrDispositionRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var ncrId = Route<string>("ncrId") ?? request.NcrId;
        var downstreamRequest = request with { NcrId = ncrId };
        return quality.SubmitNcrDispositionAsync(
            tokenProvider.BearerToken,
            ncrId,
            downstreamRequest,
            cancellationToken);
    }
}

[Tags("Business Console Quality")]
[HttpPost("/api/business-console/v1/quality/ncrs/{ncrId}/close")]
[BusinessGatewayOperationId("closeBusinessConsoleQualityNcr")]
public sealed class CloseBusinessConsoleQualityNcrEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessQualityClient quality,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleNcrCloseRequest, BusinessConsoleAcceptedResponse>(
        auth,
        BusinessGatewayPermissions.QualityNcrManage)
{
    protected override string OrganizationId(BusinessConsoleNcrCloseRequest request) =>
        HttpContext.Request.Query["organizationId"].ToString();

    protected override string EnvironmentId(BusinessConsoleNcrCloseRequest request) =>
        HttpContext.Request.Query["environmentId"].ToString();

    protected override string? ResourceType(BusinessConsoleNcrCloseRequest request) => "ncr";

    protected override string? ResourceId(BusinessConsoleNcrCloseRequest request) =>
        Route<string>("ncrId") ?? request.NcrId;

    protected override Task<BusinessConsoleAcceptedResponse> ForwardAsync(
        BusinessConsoleNcrCloseRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var ncrId = Route<string>("ncrId") ?? request.NcrId;
        var downstreamRequest = request with { NcrId = ncrId };
        return quality.CloseNcrAsync(
            tokenProvider.BearerToken,
            ncrId,
            downstreamRequest,
            cancellationToken);
    }
}
