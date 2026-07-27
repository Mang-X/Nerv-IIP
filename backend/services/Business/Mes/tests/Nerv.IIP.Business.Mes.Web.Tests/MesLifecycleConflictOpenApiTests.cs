using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MesLifecycleConflictOpenApiTests
{
    [Fact]
    public async Task Lifecycle_action_contracts_declare_conflict_responses()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
                builder.UseSetting("InternalService:BearerToken", "test-internal-service-token"));
        using var client = factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        var paths = document.RootElement.GetProperty("paths");

        foreach (var route in ConflictRoutes)
        {
            Assert.True(
                paths.GetProperty(route).GetProperty("post").GetProperty("responses").TryGetProperty("409", out _),
                $"POST {route} must declare 409.");
        }
    }

    private static readonly string[] ConflictRoutes =
    [
        "/api/business/v1/mes/operation-tasks/{operationTaskId}/start",
        "/api/business/v1/mes/operation-tasks/{operationTaskId}/pause",
        "/api/business/v1/mes/operation-tasks/{operationTaskId}/resume",
        "/api/business/v1/mes/operation-tasks/{operationTaskId}/complete",
        "/api/business/v1/mes/work-orders/{workOrderId}/release",
        "/api/business/v1/mes/work-orders/{workOrderId}/hold",
        "/api/business/v1/mes/work-orders/{workOrderId}/cancel",
        "/api/business/v1/mes/production-reports",
        "/api/business/v1/mes/material-issue-requests/{requestId}/line-side-receipts",
    ];
}
