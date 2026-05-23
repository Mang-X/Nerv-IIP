using System.Net;
using System.Net.Http.Json;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Nerv.IIP.Business.Wms.Infrastructure;
using Nerv.IIP.Business.Wms.Web.Application.Auth;
using Nerv.IIP.Business.Wms.Web.Endpoints.Wms;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Wms.Web.Tests;

public sealed class WmsEndpointContractTests
{
    [Fact]
    public void Wms_endpoints_expose_issue_136_routes_permissions_policies_and_operation_ids()
    {
        var contracts = WmsEndpointContracts.All.ToArray();

        Assert.Equal(13, contracts.Length);
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/wms/inbound-orders" && x.PermissionCode == WmsPermissionCodes.ReceiptsManage && x.OperationId == "createWmsInboundOrder");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/wms/inbound-orders" && x.PermissionCode == WmsPermissionCodes.ReceiptsRead && x.OperationId == "listWmsInboundOrders");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/wms/inbound-orders/{inboundOrderId}/putaway-tasks" && x.PermissionCode == WmsPermissionCodes.ReceiptsManage && x.OperationId == "createWmsPutawayTask");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/wms/inbound-orders/{inboundOrderId}/complete" && x.PermissionCode == WmsPermissionCodes.ReceiptsManage && x.OperationId == "completeWmsInboundOrder");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/wms/outbound-orders" && x.PermissionCode == WmsPermissionCodes.ShipmentsManage && x.OperationId == "createWmsOutboundOrder");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/wms/outbound-orders" && x.PermissionCode == WmsPermissionCodes.ShipmentsRead && x.OperationId == "listWmsOutboundOrders");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/wms/outbound-orders/{outboundOrderId}/picking-tasks" && x.PermissionCode == WmsPermissionCodes.ShipmentsManage && x.OperationId == "createWmsPickingTask");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/wms/outbound-orders/{outboundOrderId}/complete" && x.PermissionCode == WmsPermissionCodes.ShipmentsManage && x.OperationId == "completeWmsOutboundOrder");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/wms/count-executions" && x.PermissionCode == WmsPermissionCodes.ReceiptsManage && x.OperationId == "createWmsCountExecution");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/wms/count-executions/{countExecutionId}/complete" && x.PermissionCode == WmsPermissionCodes.ReceiptsManage && x.OperationId == "completeWmsCountExecution");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/wms/wcs-tasks/{warehouseTaskId}/dispatch" && x.PermissionCode == WmsPermissionCodes.AutomationManage && x.OperationId == "dispatchWmsWcsTask");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/wms/wcs-tasks/{externalTaskId}/complete" && x.PermissionCode == WmsPermissionCodes.AutomationManage && x.OperationId == "completeWmsWcsTask");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/wms/wcs-tasks/{externalTaskId}/fail" && x.PermissionCode == WmsPermissionCodes.AutomationManage && x.OperationId == "failWmsWcsTask");
        Assert.All(contracts, x => Assert.Equal(InternalServiceAuthorizationPolicy.Name, x.AuthorizationPolicy));
    }

    [Theory]
    [MemberData(nameof(EndpointTypes))]
    public void Wms_endpoints_route_through_mediator(Type endpointType)
    {
        var parameterTypes = endpointType.GetConstructors().Single().GetParameters().Select(x => x.ParameterType).ToArray();

        Assert.Contains(typeof(ISender), parameterTypes);
        Assert.DoesNotContain(typeof(ApplicationDbContext), parameterTypes);
    }

    [Fact]
    public async Task Wms_http_endpoints_reject_anonymous_callers_before_persistence()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("environment", "Testing");
                builder.UseSetting("InternalService:BearerToken", "test-internal-token");
            });
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/business/v1/wms/inbound-orders", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            inboundOrderNo = "IN-001",
            sourceDocumentType = "purchase-receipt",
            sourceDocumentId = "PO-001",
            siteCode = "SITE-01",
            lines = Array.Empty<object>(),
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    public static IEnumerable<object[]> EndpointTypes()
    {
        return WmsEndpointContracts.All.Select(x => new object[] { x.EndpointType });
    }
}
