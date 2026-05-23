using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Nerv.IIP.Business.Quality.Web.Endpoints.InspectionPlans;
using Nerv.IIP.Business.Quality.Web.Endpoints.NonconformanceReports;

namespace Nerv.IIP.Business.Quality.Web.Tests;

public sealed class QualityOpenApiTests
{
    [Fact]
    public async Task OpenApi_document_exposes_ncr_and_inspection_operation_ids()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:Redis"] = "localhost:6379",
                        // Program.cs reads this key. Swagger generation builds EF metadata but does not open a DB connection.
                        ["ConnectionStrings:PostgreSQL"] = "Host=unused;Database=nerv_iip_quality_openapi;Username=nerv;Password=nerv",
                    }));
            });
        using var client = factory.CreateClient();

        using var document = await GetOpenApiDocumentAsync(client);
        AssertOperationIdsAreUnique(document);

        foreach (var contract in QualityEndpointContracts.All)
        {
            Assert.Equal(
                contract.OperationId,
                GetOperationId(document, contract.Route, contract.HttpMethod.ToLowerInvariant()));
        }

        foreach (var contract in QualityInspectionEndpointContracts.All)
        {
            Assert.Equal(
                contract.OperationId,
                GetOperationId(document, contract.Route, contract.HttpMethod.ToLowerInvariant()));
        }
    }

    private static async Task<JsonDocument> GetOpenApiDocumentAsync(HttpClient client)
    {
        await using var stream = await client.GetStreamAsync("/swagger/v1/swagger.json");
        return await JsonDocument.ParseAsync(stream);
    }

    private static void AssertOperationIdsAreUnique(JsonDocument document)
    {
        var operations = document.RootElement
            .GetProperty("paths")
            .EnumerateObject()
            .SelectMany(path => path.Value
                .EnumerateObject()
                .Where(operation => IsHttpMethod(operation.Name))
                .Select(operation => (
                    Name: $"{operation.Name.ToUpperInvariant()} {path.Name}",
                    OperationId: operation.Value.TryGetProperty("operationId", out var operationId)
                        ? operationId.GetString()
                        : null)))
            .ToArray();

        Assert.Empty(operations.Where(operation => string.IsNullOrWhiteSpace(operation.OperationId)).Select(operation => operation.Name));
        Assert.Empty(operations
            .Where(operation => !string.IsNullOrWhiteSpace(operation.OperationId))
            .GroupBy(operation => operation.OperationId!, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(", ", group.Select(operation => operation.Name))}"));
    }

    private static bool IsHttpMethod(string method) =>
        method is "get" or "post" or "put" or "patch" or "delete" or "head" or "options" or "trace";

    private static string GetOperationId(JsonDocument document, string route, string method)
    {
        var paths = document.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty(route, out var path), $"OpenAPI path '{route}' was not found.");
        Assert.True(path.TryGetProperty(method, out var operation), $"OpenAPI operation '{method} {route}' was not found.");
        return operation.GetProperty("operationId").GetString()!;
    }
}
