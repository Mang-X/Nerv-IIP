using Nerv.IIP.Business.Quality.Web.Application.Auth;
using Nerv.IIP.Business.Quality.Web.Endpoints.NonconformanceReports;

namespace Nerv.IIP.Business.Quality.Web.Tests;

public sealed class QualityEndpointContractTests
{
    [Fact]
    public void Ncr_endpoints_expose_issue_97_routes_permissions_and_operation_ids()
    {
        var contracts = QualityEndpointContracts.All.ToArray();

        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/quality/ncrs"
            && x.PermissionCode == BusinessPermissionCodes.QualityNcrManage
            && x.OperationId == "createBusinessQualityNcr");
        Assert.Contains(contracts, x => x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/quality/ncrs"
            && x.PermissionCode == BusinessPermissionCodes.QualityNcrRead
            && x.OperationId == "listBusinessQualityNcrs");
        Assert.Contains(contracts, x => x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/quality/ncrs/{ncrId}"
            && x.PermissionCode == BusinessPermissionCodes.QualityNcrRead
            && x.OperationId == "getBusinessQualityNcr");
        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/quality/ncrs/{ncrId}/disposition"
            && x.PermissionCode == BusinessPermissionCodes.QualityNcrManage
            && x.OperationId == "submitBusinessQualityNcrDisposition");
        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/quality/ncrs/{ncrId}/close"
            && x.PermissionCode == BusinessPermissionCodes.QualityNcrManage
            && x.OperationId == "closeBusinessQualityNcr");
    }
}
