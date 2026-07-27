using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessGatewayLifecycleConflictOpenApiTests
{
    [Fact]
    public async Task Lifecycle_action_contracts_declare_only_real_conflict_responses()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Iam:Jwt:JwksJson", BusinessGatewayTestTokens.PublicJwksJson());
            builder.UseSetting("Iam:Jwt:Issuer", BusinessGatewayTestTokens.Issuer);
            builder.UseSetting("Iam:Jwt:Audience", BusinessGatewayTestTokens.Audience);
        });
        using var client = factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        var paths = document.RootElement.GetProperty("paths");

        foreach (var route in ConflictRoutes)
        {
            AssertResponse(paths, route, "409");
        }

        AssertResponse(paths, "/api/business-console/v1/quality/ncrs/{ncrId}/disposition", "400");
        AssertResponse(paths, "/api/business-console/v1/quality/ncrs/{ncrId}/close", "400");
        AssertNoResponse(paths, "/api/business-console/v1/equipment/alarms/{alarmEventId}/unshelve", "409");
    }

    private static readonly string[] ConflictRoutes =
    [
        "/api/business-console/v1/mes/operation-tasks/{operationTaskId}/start",
        "/api/business-console/v1/mes/operation-tasks/{operationTaskId}/pause",
        "/api/business-console/v1/mes/operation-tasks/{operationTaskId}/resume",
        "/api/business-console/v1/mes/operation-tasks/{operationTaskId}/complete",
        "/api/business-console/v1/mes/work-orders/{workOrderId}/release",
        "/api/business-console/v1/mes/work-orders/{workOrderId}/hold",
        "/api/business-console/v1/mes/work-orders/{workOrderId}/cancel",
        "/api/business-console/v1/mes/production-reports",
        "/api/business-console/v1/mes/material-issue-requests/{requestId}/line-side-receipts",
        "/api/business-console/v1/wms/inbound-orders/{inboundOrderId}/complete",
        "/api/business-console/v1/wms/outbound-orders/{outboundOrderId}/complete",
        "/api/business-console/v1/wms/count-executions/{countExecutionId}/complete",
        "/api/business-console/v1/quality/inspection-tasks/{inspectionTaskId}/inspection-record",
        "/api/business-console/v1/quality/ncrs/{ncrId}/disposition",
        "/api/business-console/v1/quality/ncrs/{ncrId}/close",
        "/api/business-console/v1/maintenance/work-orders/{workOrderId}/complete",
        "/api/business-console/v1/equipment/alarms/{alarmEventId}/acknowledge",
        "/api/business-console/v1/equipment/alarms/{alarmEventId}/shelve",
    ];

    private static void AssertResponse(JsonElement paths, string route, string statusCode) =>
        Assert.True(
            paths.GetProperty(route).GetProperty("post").GetProperty("responses").TryGetProperty(statusCode, out _),
            $"POST {route} must declare {statusCode}.");

    private static void AssertNoResponse(JsonElement paths, string route, string statusCode) =>
        Assert.False(
            paths.GetProperty(route).GetProperty("post").GetProperty("responses").TryGetProperty(statusCode, out _),
            $"POST {route} must not declare {statusCode}.");
}
