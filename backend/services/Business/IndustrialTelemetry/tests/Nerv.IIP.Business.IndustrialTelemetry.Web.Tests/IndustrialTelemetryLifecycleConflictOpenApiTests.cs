using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Tests;

public sealed class IndustrialTelemetryLifecycleConflictOpenApiTests
{
    [Fact]
    public async Task Alarm_action_contracts_declare_only_real_conflict_responses()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("environment", "Testing");
                builder.UseSetting("InternalService:BearerToken", "test-internal-token");
            });
        using var client = factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        var paths = document.RootElement.GetProperty("paths");

        AssertResponse(paths, "/api/business/v1/iiot/alarms/{alarmEventId}/acknowledge", "409");
        AssertResponse(paths, "/api/business/v1/iiot/alarms/{alarmEventId}/shelve", "409");
        AssertNoResponse(paths, "/api/business/v1/iiot/alarms/{alarmEventId}/unshelve", "409");
    }

    private static void AssertResponse(JsonElement paths, string route, string statusCode) =>
        Assert.True(
            paths.GetProperty(route).GetProperty("post").GetProperty("responses").TryGetProperty(statusCode, out _),
            $"POST {route} must declare {statusCode}.");

    private static void AssertNoResponse(JsonElement paths, string route, string statusCode) =>
        Assert.False(
            paths.GetProperty(route).GetProperty("post").GetProperty("responses").TryGetProperty(statusCode, out _),
            $"POST {route} must not declare {statusCode}.");
}
