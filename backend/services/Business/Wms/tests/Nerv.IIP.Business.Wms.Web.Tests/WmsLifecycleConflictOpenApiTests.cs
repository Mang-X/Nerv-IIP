using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Nerv.IIP.Business.Wms.Web.Tests;

public sealed class WmsLifecycleConflictOpenApiTests
{
    [Fact]
    public async Task Completion_contracts_declare_conflict_responses()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
                builder.UseSetting("InternalService:BearerToken", "test-internal-service-token"));
        using var client = factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        var paths = document.RootElement.GetProperty("paths");

        foreach (var route in ConflictRoutes)
        {
            var schemaReference = paths.GetProperty(route)
                .GetProperty("post")
                .GetProperty("responses")
                .GetProperty("409")
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema")
                .GetProperty("$ref")
                .GetString();

            Assert.Equal(
                "#/components/schemas/NervIIPBusinessWmsWebApplicationErrorsWmsLifecycleConflictResponse",
                schemaReference);
        }
    }

    private static readonly string[] ConflictRoutes =
    [
        "/api/business/v1/wms/inbound-orders/{inboundOrderId}/complete",
        "/api/business/v1/wms/outbound-orders/{outboundOrderId}/complete",
        "/api/business/v1/wms/count-executions/{countExecutionId}/complete",
    ];
}
