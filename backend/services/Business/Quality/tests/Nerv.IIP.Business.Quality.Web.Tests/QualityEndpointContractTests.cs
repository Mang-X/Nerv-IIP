using Nerv.IIP.Business.Quality.Domain;
using Nerv.IIP.Business.Quality.Web.Application.Commands.NonconformanceReports;
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

    [Fact]
    public async Task Ncr_code_generator_uses_non_probabilistic_guid_v7_token()
    {
        var generator = new NonconformanceReportCodeGenerator();

        var code = await generator.NextAsync("org-001", "env-dev", CancellationToken.None);

        Assert.StartsWith("NCR-", code, StringComparison.Ordinal);
        Assert.True(Guid.TryParseExact(code["NCR-".Length..], "N", out var id));
        Assert.Equal(7, id.Version);
    }

    [Fact]
    public void Quality_facts_expose_service_name_for_multienv_configuration()
    {
        Assert.Equal("BusinessQuality", QualityFacts.ServiceName);
    }
}
