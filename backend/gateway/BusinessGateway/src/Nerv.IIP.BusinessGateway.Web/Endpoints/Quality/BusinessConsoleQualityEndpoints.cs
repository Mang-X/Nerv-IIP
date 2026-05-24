using FastEndpoints;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Quality;

public sealed class SubmitBusinessConsoleQualityNcrDispositionRequest
{
    public string NcrId { get; set; } = string.Empty;
}

public sealed class CloseBusinessConsoleQualityNcrRequest
{
    public string NcrId { get; set; } = string.Empty;
}

[Tags("Business Console Quality")]
[HttpGet("/api/business-console/v1/quality/inspection-plans")]
[BusinessGatewayOperationId("listBusinessConsoleQualityInspectionPlans")]
public sealed class ListBusinessConsoleQualityInspectionPlansEndpoint(IBusinessGatewayAuthorizationClient auth)
    : AuthorizedBusinessStubEndpoint(auth, BusinessGatewayPermissions.QualityInspectionRecordsRead);

[Tags("Business Console Quality")]
[HttpPost("/api/business-console/v1/quality/inspection-records")]
[BusinessGatewayOperationId("createBusinessConsoleQualityInspectionRecord")]
public sealed class CreateBusinessConsoleQualityInspectionRecordEndpoint(IBusinessGatewayAuthorizationClient auth)
    : AuthorizedBusinessStubEndpoint(auth, BusinessGatewayPermissions.QualityInspectionRecordsCreate);

[Tags("Business Console Quality")]
[HttpGet("/api/business-console/v1/quality/ncrs")]
[BusinessGatewayOperationId("listBusinessConsoleQualityNcrs")]
public sealed class ListBusinessConsoleQualityNcrsEndpoint(IBusinessGatewayAuthorizationClient auth)
    : AuthorizedBusinessStubEndpoint(auth, BusinessGatewayPermissions.QualityNcrRead);

[Tags("Business Console Quality")]
[HttpPost("/api/business-console/v1/quality/ncrs/{ncrId}/disposition")]
[BusinessGatewayOperationId("submitBusinessConsoleQualityNcrDisposition")]
public sealed class SubmitBusinessConsoleQualityNcrDispositionEndpoint
    (IBusinessGatewayAuthorizationClient auth)
    : AuthorizedBusinessStubEndpoint<SubmitBusinessConsoleQualityNcrDispositionRequest>(
        auth,
        BusinessGatewayPermissions.QualityNcrManage);

[Tags("Business Console Quality")]
[HttpPost("/api/business-console/v1/quality/ncrs/{ncrId}/close")]
[BusinessGatewayOperationId("closeBusinessConsoleQualityNcr")]
public sealed class CloseBusinessConsoleQualityNcrEndpoint(IBusinessGatewayAuthorizationClient auth)
    : AuthorizedBusinessStubEndpoint<CloseBusinessConsoleQualityNcrRequest>(
        auth,
        BusinessGatewayPermissions.QualityNcrManage);
