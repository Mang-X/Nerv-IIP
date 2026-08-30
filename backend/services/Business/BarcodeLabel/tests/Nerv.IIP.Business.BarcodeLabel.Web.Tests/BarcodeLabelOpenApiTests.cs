using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class BarcodeLabelOpenApiTests
{
    [Fact]
    public async Task OpenApi_document_keeps_barcode_list_queries_flat_and_defaulted()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:PostgreSQL"] = "Host=unused;Database=nerv_iip_barcode_openapi;Username=nerv;Password=nerv",
                        ["InternalService:BearerToken"] = "barcode-label-openapi-test-token",
                    }));
            });
        using var client = factory.CreateClient();

        using var document = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        var paths = document.RootElement.GetProperty("paths");

        AssertListQuery(
            paths,
            "/api/business/v1/barcodes/rules",
            "listBusinessBarcodeRules",
            ["organizationId", "environmentId", "status", "keyword", "skip", "take"]);
        AssertListQuery(
            paths,
            "/api/business/v1/barcodes/templates",
            "listBusinessBarcodeTemplates",
            ["organizationId", "environmentId", "status", "skip", "take"]);
        AssertListQuery(
            paths,
            "/api/business/v1/barcodes/print-batches",
            "listBusinessBarcodePrintBatches",
            ["organizationId", "environmentId", "sourceDocumentType", "sourceDocumentId", "status", "skip", "take"]);
        AssertListQuery(
            paths,
            "/api/business/v1/barcodes/scans",
            "listBusinessBarcodeScans",
            ["organizationId", "environmentId", "deviceCode", "scannedValue", "sourceWorkflow", "sourceDocumentId", "skip", "take"]);
    }

    private static void AssertListQuery(
        JsonElement paths,
        string route,
        string operationId,
        string[] expectedParameterNames)
    {
        var operation = paths.GetProperty(route).GetProperty("get");
        Assert.Equal(operationId, operation.GetProperty("operationId").GetString());
        var parameters = operation.GetProperty("parameters").EnumerateArray().ToArray();
        Assert.Equal(expectedParameterNames, parameters.Select(parameter => parameter.GetProperty("name").GetString()));
        Assert.Equal(0, GetDefault(parameters, "skip"));
        Assert.Equal(100, GetDefault(parameters, "take"));
    }

    private static int GetDefault(IEnumerable<JsonElement> parameters, string name) =>
        parameters
            .Single(parameter => parameter.GetProperty("name").GetString() == name)
            .GetProperty("schema")
            .GetProperty("default")
            .GetInt32();
}
