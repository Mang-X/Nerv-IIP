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

    [Fact]
    public async Task OpenApi_document_exposes_scoped_lifecycle_query_and_body_contracts()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:PostgreSQL"] = "Host=unused;Database=nerv_iip_barcode_scoped_openapi;Username=nerv;Password=nerv",
                        ["InternalService:BearerToken"] = "barcode-label-scoped-openapi-test-token",
                    }));
            });
        using var client = factory.CreateClient();

        using var document = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        AssertScopedLifecycleOperation(
            document.RootElement,
            "/api/business/internal/v1/barcodes/print-batches/{printBatchId}/activate",
            "activateBusinessBarcodePrintBatch",
            ["printBatchId"],
            ["productionReportId", "productionReportNo"]);
        AssertScopedLifecycleOperation(
            document.RootElement,
            "/api/business/internal/v1/barcodes/print-batches/{printBatchId}/dispatch",
            "dispatchScopedBusinessBarcodePrintBatch",
            ["printBatchId"],
            ["printBatchId", "printerId"]);
        AssertScopedLifecycleOperation(
            document.RootElement,
            "/api/business/internal/v1/barcodes/print-batches/{printBatchId}/items/{sequenceNo}/reprint",
            "reprintScopedBusinessBarcodeLabel",
            ["printBatchId", "sequenceNo"],
            ["printBatchId", "sequenceNo", "printerId"]);
        AssertScopedLifecycleOperation(
            document.RootElement,
            "/api/business/internal/v1/barcodes/print-batches/{printBatchId}/items/{sequenceNo}/void",
            "voidScopedBusinessBarcodeLabel",
            ["printBatchId", "sequenceNo"],
            ["printBatchId", "sequenceNo", "reason"]);
    }

    [Fact]
    public async Task OpenApi_document_exposes_scoped_v2_print_batch_detail()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:PostgreSQL"] = "Host=unused;Database=nerv_iip_barcode_scoped_detail_openapi;Username=nerv;Password=nerv",
                        ["InternalService:BearerToken"] = "barcode-label-scoped-detail-openapi-test-token",
                    }));
            });
        using var client = factory.CreateClient();

        using var document = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        var operation = document.RootElement
            .GetProperty("paths")
            .GetProperty("/api/business/v2/barcodes/print-batches/{printBatchId}")
            .GetProperty("get");

        Assert.Equal("getScopedBusinessBarcodePrintBatch", operation.GetProperty("operationId").GetString());
        var parameters = operation.GetProperty("parameters").EnumerateArray().ToArray();
        foreach (var parameterName in new[] { "printBatchId", "organizationId", "environmentId" })
        {
            var parameter = Assert.Single(parameters, item => item.GetProperty("name").GetString() == parameterName);
            Assert.True(parameter.GetProperty("required").GetBoolean());
        }

        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var createRequest = Assert.Single(
            schemas.EnumerateObject(),
            schema => schema.Name.EndsWith("CreateLabelPrintBatchRequest", StringComparison.Ordinal));
        Assert.True(
            createRequest.Value.GetProperty("properties").TryGetProperty("reportIntentFingerprint", out var createFingerprint),
            createRequest.Value.GetRawText());
        Assert.Equal(1, createFingerprint.GetProperty("minLength").GetInt32());
        Assert.Equal(256, createFingerprint.GetProperty("maxLength").GetInt32());
        if (createRequest.Value.TryGetProperty("required", out var createRequired))
        {
            Assert.DoesNotContain(
                "reportIntentFingerprint",
                createRequired.EnumerateArray().Select(item => item.GetString()));
        }

        var scopedDetail = Assert.Single(
            schemas.EnumerateObject(),
            schema => schema.Name.EndsWith("ScopedLabelPrintBatchDetail", StringComparison.Ordinal));
        Assert.True(
            scopedDetail.Value.GetProperty("properties").TryGetProperty("reportIntentFingerprint", out var scopedFingerprint),
            scopedDetail.Value.GetRawText());
        Assert.True(
            scopedFingerprint.TryGetProperty("nullable", out var nullable) && nullable.GetBoolean(),
            scopedFingerprint.GetRawText());
        Assert.Contains(
            "reportIntentFingerprint",
            scopedDetail.Value.GetProperty("required").EnumerateArray().Select(item => item.GetString()));
    }

    private static void AssertScopedLifecycleOperation(
        JsonElement root,
        string route,
        string operationId,
        string[] routeParameters,
        string[] bodyProperties)
    {
        var operation = root.GetProperty("paths").GetProperty(route).GetProperty("post");
        Assert.Equal(operationId, operation.GetProperty("operationId").GetString());
        var parameters = operation.GetProperty("parameters").EnumerateArray().ToArray();
        foreach (var routeParameter in routeParameters)
        {
            var parameter = Assert.Single(parameters, item =>
                item.GetProperty("name").GetString() == routeParameter
                && item.GetProperty("in").GetString() == "path");
            Assert.True(parameter.GetProperty("required").GetBoolean());
        }

        foreach (var queryParameter in new[] { "organizationId", "environmentId" })
        {
            var parameter = Assert.Single(parameters, item =>
                item.GetProperty("name").GetString() == queryParameter
                && item.GetProperty("in").GetString() == "query");
            Assert.True(parameter.GetProperty("required").GetBoolean());
        }

        Assert.True(operation.TryGetProperty("requestBody", out var requestBody), operation.GetRawText());
        Assert.True(requestBody.TryGetProperty("content", out var content), requestBody.GetRawText());
        Assert.True(content.TryGetProperty("application/json", out var jsonContent), content.GetRawText());
        var schema = jsonContent.GetProperty("schema");
        var bodySchema = schema;
        if (schema.TryGetProperty("$ref", out var reference))
        {
            var schemaReference = reference.GetString()!;
            var schemaName = schemaReference[(schemaReference.LastIndexOf('/') + 1)..];
            bodySchema = root
                .GetProperty("components")
                .GetProperty("schemas")
                .GetProperty(schemaName);
        }

        Assert.True(bodySchema.TryGetProperty("properties", out var properties), bodySchema.GetRawText());
        var actualBodyProperties = properties
            .EnumerateObject()
            .Select(property => property.Name)
            .ToArray();
        Assert.Equal(bodyProperties, actualBodyProperties);
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
