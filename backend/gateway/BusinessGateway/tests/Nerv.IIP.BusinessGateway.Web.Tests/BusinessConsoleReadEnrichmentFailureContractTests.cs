using System.Net;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Endpoints.Maintenance;
using Nerv.IIP.BusinessGateway.Web.Endpoints.Mes;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessConsoleReadEnrichmentFailureContractTests
{
    [Theory]
    [InlineData("mes", HttpStatusCode.Unauthorized)]
    [InlineData("mes", HttpStatusCode.Forbidden)]
    [InlineData("maintenance", HttpStatusCode.Unauthorized)]
    [InlineData("maintenance", HttpStatusCode.Forbidden)]
    public async Task Read_enrichers_propagate_authorization_failures(
        string enricher,
        HttpStatusCode statusCode)
    {
        var failure = new BusinessServiceProxyException(statusCode, "internal-service-authorization-failed");

        var thrown = enricher switch
        {
            "mes" => await Assert.ThrowsAsync<BusinessServiceProxyException>(
                () => MesDowntimeReasonNameEnricher.EnrichAsync(
                    new BusinessConsoleMesDowntimeEventListResponse([], 0, []),
                    new RecordingMaintenanceFacadeClient { DowntimeReasonDirectoryFailure = failure },
                    "internal-token",
                    "org-001",
                    "env-dev",
                    CancellationToken.None)),
            "maintenance" => await Assert.ThrowsAsync<BusinessServiceProxyException>(
                () => MaintenanceDeviceAssetWarrantyEnricher.EnrichAsync(
                    [new BusinessConsoleMaintenanceWorkOrderItem(
                        "wo-001",
                        "device-001",
                        "high",
                        "Open",
                        null,
                        null,
                        DateTimeOffset.Parse("2026-08-28T08:00:00Z"))],
                    new RecordingMasterDataClient { DetailFailure = failure },
                    "internal-token",
                    "org-001",
                    "env-dev",
                    CancellationToken.None)),
            _ => throw new InvalidOperationException($"Unsupported enricher '{enricher}'."),
        };

        Assert.Same(failure, thrown);
    }
}
