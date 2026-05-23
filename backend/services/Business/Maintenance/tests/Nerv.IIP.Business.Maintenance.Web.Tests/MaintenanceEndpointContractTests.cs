using System.Net;
using System.Net.Http.Json;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Nerv.IIP.Business.Maintenance.Infrastructure;
using Nerv.IIP.Business.Maintenance.Web.Application.Auth;
using Nerv.IIP.Business.Maintenance.Web.Endpoints.Maintenance;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Maintenance.Web.Tests;

public sealed class MaintenanceEndpointContractTests
{
    [Fact]
    public void Maintenance_endpoints_expose_issue_130_routes_permissions_policies_and_operation_ids()
    {
        var contracts = MaintenanceEndpointContracts.All.ToArray();

        Assert.Equal(6, contracts.Length);
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/maintenance/work-orders" && x.PermissionCode == MaintenancePermissionCodes.WorkOrdersManage && x.OperationId == "createMaintenanceWorkOrder");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/maintenance/work-orders/{workOrderId}/complete" && x.PermissionCode == MaintenancePermissionCodes.WorkOrdersManage && x.OperationId == "completeMaintenanceWorkOrder");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/maintenance/work-orders" && x.PermissionCode == MaintenancePermissionCodes.WorkOrdersRead && x.OperationId == "listMaintenanceWorkOrders");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/maintenance/plans" && x.PermissionCode == MaintenancePermissionCodes.PlansManage && x.OperationId == "createMaintenancePlan");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/maintenance/plans" && x.PermissionCode == MaintenancePermissionCodes.PlansRead && x.OperationId == "listMaintenancePlans");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/maintenance/inspections" && x.PermissionCode == MaintenancePermissionCodes.PlansManage && x.OperationId == "recordMaintenanceInspection");
        Assert.All(contracts, x => Assert.Equal(InternalServiceAuthorizationPolicy.Name, x.AuthorizationPolicy));
    }

    [Theory]
    [MemberData(nameof(EndpointTypes))]
    public void Maintenance_endpoints_route_through_mediator(Type endpointType)
    {
        var parameterTypes = endpointType.GetConstructors().Single().GetParameters().Select(x => x.ParameterType).ToArray();

        Assert.Contains(typeof(ISender), parameterTypes);
        Assert.DoesNotContain(typeof(ApplicationDbContext), parameterTypes);
    }

    [Fact]
    public async Task Maintenance_http_endpoints_reject_anonymous_callers_before_persistence()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("environment", "Testing");
                builder.UseSetting("InternalService:BearerToken", "test-internal-token");
            });
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/business/v1/maintenance/work-orders", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            deviceAssetId = "DEV-CNC-01",
            priority = "critical",
            openedBy = "operator-001",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    public static IEnumerable<object[]> EndpointTypes()
    {
        return MaintenanceEndpointContracts.All.Select(x => new object[] { x.EndpointType });
    }
}
