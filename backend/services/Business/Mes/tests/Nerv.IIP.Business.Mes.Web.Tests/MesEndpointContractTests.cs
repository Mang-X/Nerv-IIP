using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Nerv.IIP.Business.Mes.Web.Application.Auth;
using Nerv.IIP.Business.Mes.Web.Endpoints.Mes;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MesEndpointContractTests
{
    [Fact]
    public void MesEndpointContracts_ExposeRescheduleAndRushOrderRoutes()
    {
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/mes/schedules/run"
            && x.PermissionCode == MesPermissionCodes.SchedulesManage
            && x.OperationId == "runBusinessMesSchedule");

        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/mes/work-orders/rush"
            && x.PermissionCode == MesPermissionCodes.WorkOrdersManage
            && x.OperationId == "createBusinessMesRushWorkOrder");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/mes/work-orders"
            && x.PermissionCode == MesPermissionCodes.WorkOrdersManage
            && x.OperationId == "listBusinessMesWorkOrders");

        Assert.All(MesEndpointContracts.All, contract =>
            Assert.Contains(contract.PermissionCode, MesPermissionCodes.All));
    }

    [Theory]
    [InlineData("/api/business/v1/mes/schedules/run")]
    [InlineData("/api/business/v1/mes/work-orders/rush")]
    public async Task Mes_write_endpoints_require_internal_service_authentication(string route)
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(route, new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            trigger = "Manual",
            workOrderId = "WO-RUSH",
            skuId = "SKU-R",
            productionVersionId = "PV-001",
            quantity = 1,
            dueUtc = DateTimeOffset.Parse("2026-05-22T12:00:00Z"),
            workCenterId = "WC-A",
            durationMinutes = 60
        });

        Assert.True(
            response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"Expected auth failure but received {(int)response.StatusCode}.");
    }

    [Fact]
    public async Task Mes_work_order_query_endpoint_requires_internal_service_authentication()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/business/v1/mes/work-orders?organizationId=org-001&environmentId=env-dev");

        Assert.True(
            response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"Expected auth failure but received {(int)response.StatusCode}.");
    }
}
