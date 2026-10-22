using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
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

        AssertResponseSchema(
            paths,
            "/api/business/v1/iiot/alarms/{alarmEventId}/acknowledge",
            "409",
            "#/components/schemas/NervIIPBusinessIndustrialTelemetryWebApplicationErrorsIndustrialTelemetryLifecycleConflictResponse");
        AssertResponseSchema(
            paths,
            "/api/business/v1/iiot/alarms/{alarmEventId}/shelve",
            "409",
            "#/components/schemas/NervIIPBusinessIndustrialTelemetryWebApplicationErrorsIndustrialTelemetryLifecycleConflictResponse");
        AssertNoResponse(paths, "/api/business/v1/iiot/alarms/{alarmEventId}/unshelve", "409");
    }

    [Fact]
    public async Task Oee_aggregate_v1_contract_exposes_dimension_and_degraded_reason_enums()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("environment", "Testing");
                builder.UseSetting("InternalService:BearerToken", "test-internal-token");
            });
        using var client = factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        var root = document.RootElement;
        var schemas = root.GetProperty("components").GetProperty("schemas");
        var operation = root.GetProperty("paths")
            .GetProperty("/api/business/v1/iiot/oee/aggregates")
            .GetProperty("get");

        var dimensionParameter = operation.GetProperty("parameters")
            .EnumerateArray()
            .Single(x => string.Equals(x.GetProperty("name").GetString(), "dimension", StringComparison.OrdinalIgnoreCase));
        AssertEnumValues(
            ResolveSchema(dimensionParameter.GetProperty("schema"), schemas),
            "device", "workCenter", "line", "workshop", "shift", "day");

        var bucketSchema = schemas.EnumerateObject()
            .Select(x => x.Value)
            .Single(x => x.TryGetProperty("properties", out var properties) &&
                properties.TryGetProperty("degradedReasons", out _) &&
                properties.TryGetProperty("bucketStartUtc", out _));
        var degradedReasonSchema = ResolveSchema(
            bucketSchema.GetProperty("properties").GetProperty("degradedReasons").GetProperty("items"),
            schemas);
        AssertEnumValues(
            degradedReasonSchema,
            "runtimeStateFactsMissing",
            "runtimeStateCoverageIncomplete",
            "productionUomAmbiguous",
            "historicalLocalTimeAmbiguous");
    }

    private static JsonElement ResolveSchema(JsonElement schema, JsonElement schemas)
    {
        if (schema.TryGetProperty("allOf", out var allOf))
        {
            return ResolveSchema(Assert.Single(allOf.EnumerateArray()), schemas);
        }

        if (!schema.TryGetProperty("$ref", out var reference))
        {
            return schema;
        }

        var schemaName = reference.GetString()!.Split('/')[^1];
        return schemas.GetProperty(schemaName);
    }

    private static void AssertEnumValues(JsonElement schema, params string[] expectedValues)
    {
        Assert.True(schema.TryGetProperty("enum", out var enumValues), schema.GetRawText());
        var actual = enumValues.EnumerateArray().Select(x => x.GetString()).ToArray();
        Assert.All(expectedValues, expected => Assert.Contains(expected, actual));
    }

    private static void AssertResponseSchema(
        JsonElement paths,
        string route,
        string statusCode,
        string expectedSchemaReference)
    {
        var schemaReference = paths.GetProperty(route)
            .GetProperty("post")
            .GetProperty("responses")
            .GetProperty(statusCode)
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("schema")
            .GetProperty("$ref")
            .GetString();

        Assert.Equal(expectedSchemaReference, schemaReference);
    }

    private static void AssertNoResponse(JsonElement paths, string route, string statusCode) =>
        Assert.False(
            paths.GetProperty(route).GetProperty("post").GetProperty("responses").TryGetProperty(statusCode, out _),
            $"POST {route} must not declare {statusCode}.");
}
