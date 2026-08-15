using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Nerv.IIP.Business.Quality.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class QualityLifecycleConflictOpenApiTests
{
    [Fact]
    public async Task Lifecycle_action_contracts_declare_validation_and_conflict_responses()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:Redis"] = "localhost:6379",
                        ["ConnectionStrings:PostgreSQL"] = "Host=unused;Database=nerv_iip_quality_openapi;Username=nerv;Password=nerv",
                        ["InternalService:BearerToken"] = "test-internal-service-token",
                    }));
            });
        using var client = factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        var paths = document.RootElement.GetProperty("paths");

        AssertResponseSchema(
            paths,
            "/api/business/v1/quality/inspection-tasks/{inspectionTaskId}/inspection-record",
            "409",
            "#/components/schemas/NervIIPBusinessQualityWebApplicationErrorsQualityLifecycleConflictResponse");
        AssertResponseDeclared(paths, "/api/business/v1/quality/ncrs/{ncrId}/disposition", "400");
        AssertResponseSchema(
            paths,
            "/api/business/v1/quality/ncrs/{ncrId}/disposition",
            "409",
            "#/components/schemas/NervIIPBusinessQualityWebApplicationErrorsQualityLifecycleConflictResponse");
        AssertResponseDeclared(paths, "/api/business/v1/quality/ncrs/{ncrId}/close", "400");
        AssertResponseSchema(
            paths,
            "/api/business/v1/quality/ncrs/{ncrId}/close",
            "409",
            "#/components/schemas/NervIIPBusinessQualityWebApplicationErrorsQualityLifecycleConflictResponse");
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

    private static void AssertResponseDeclared(JsonElement paths, string route, string statusCode) =>
        Assert.True(
            paths.GetProperty(route).GetProperty("post").GetProperty("responses").TryGetProperty(statusCode, out _),
            $"POST {route} must declare {statusCode}.");
}
