using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Nerv.IIP.Business.Mes.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class MesLifecycleConflictOpenApiTests
{
    [Fact]
    public async Task Production_report_intent_contract_is_optional_on_post_and_nullable_on_exact_read()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
                builder.UseSetting("InternalService:BearerToken", "test-internal-service-token"));
        using var client = factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        var paths = document.RootElement.GetProperty("paths");
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");

        var postSchema = ResolveSchema(
            schemas,
            paths.GetProperty("/api/business/v1/mes/production-reports")
                .GetProperty("post")
                .GetProperty("requestBody")
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema"));
        var postFingerprint = postSchema.GetProperty("properties").GetProperty("reportIntentFingerprint");
        Assert.Equal(256, postFingerprint.GetProperty("maxLength").GetInt32());
        Assert.True(postFingerprint.GetProperty("nullable").GetBoolean());
        Assert.DoesNotContain(
            "reportIntentFingerprint",
            postSchema.GetProperty("required").EnumerateArray().Select(x => x.GetString()));

        var exactOperation = paths.GetProperty("/api/business/v1/mes/production-reports/by-idempotency-key")
            .GetProperty("get");
        Assert.Equal(
            "getBusinessMesProductionReportByIdempotencyKey",
            exactOperation.GetProperty("operationId").GetString());
        var responseSchema = ResolveSchema(
            schemas,
            exactOperation.GetProperty("responses")
                .GetProperty("200")
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema"));
        Assert.True(responseSchema.GetProperty("properties")
            .GetProperty("reportIntentFingerprint")
            .GetProperty("nullable")
            .GetBoolean());
        Assert.Contains(
            "reportIntentFingerprint",
            responseSchema.GetProperty("required").EnumerateArray().Select(x => x.GetString()));
    }

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
                "#/components/schemas/NervIIPBusinessMesWebApplicationErrorsMesLifecycleConflictResponse",
                schemaReference);
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

    private static JsonElement ResolveSchema(JsonElement schemas, JsonElement schema)
    {
        var reference = schema.GetProperty("$ref").GetString();
        Assert.NotNull(reference);
        return schemas.GetProperty(reference.Split('/')[^1]);
    }
}
