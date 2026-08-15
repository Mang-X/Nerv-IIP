using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Nerv.IIP.Business.Maintenance.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class MaintenanceLifecycleConflictOpenApiTests
{
    [Fact]
    public async Task Complete_work_order_contract_declares_conflict_response()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("environment", "Testing");
                builder.UseSetting("IndustrialTelemetry:BaseUrl", "http://industrial-telemetry.local");
                builder.UseSetting("InternalService:BearerToken", "test-internal-token");
            });
        using var client = factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        var responses = document.RootElement.GetProperty("paths")
            .GetProperty("/api/business/v1/maintenance/work-orders/{workOrderId}/complete")
            .GetProperty("post")
            .GetProperty("responses");

        var schemaReference = responses.GetProperty("409")
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("schema")
            .GetProperty("$ref")
            .GetString();

        Assert.Equal(
            "#/components/schemas/NervIIPBusinessMaintenanceWebApplicationErrorsMaintenanceLifecycleConflictResponse",
            schemaReference);
    }
}
