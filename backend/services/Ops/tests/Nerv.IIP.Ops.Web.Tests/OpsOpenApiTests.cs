using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Nerv.IIP.Ops.Web.Tests;

public sealed class OpsOpenApiTests
{
    [Fact]
    public async Task OpenApi_document_exposes_endpoint_operation_ids()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        using var document = await GetOpenApiDocumentAsync(client);
        AssertOperationIdsAreUnique(document);

        foreach (var (route, method, operationId) in ExpectedOperations)
        {
            Assert.Equal(operationId, GetOperationId(document, route, method));
        }
    }

    private static readonly (string Route, string Method, string OperationId)[] ExpectedOperations =
    [
        ("/api/ops/v1/operation-tasks", "get", "listOperationTasks"),
        ("/api/ops/v1/operation-tasks", "post", "createOperationTask"),
        ("/api/ops/v1/operation-tasks/{operationTaskId}", "get", "getOperationTask"),
        ("/api/ops/v1/operation-tasks/pending", "get", "getPendingOperationTasks"),
        ("/api/ops/v1/operation-tasks/claims", "post", "claimOperationTasks"),
        ("/api/ops/v1/operation-tasks/{operationTaskId}/lease/abandon", "post", "abandonOperationTaskLease"),
        ("/api/ops/v1/operation-tasks/{operationTaskId}/lease/heartbeat", "post", "heartbeatOperationTaskLease"),
        ("/api/ops/v1/operation-results", "post", "recordOperationResult"),
        ("/api/ops/v1/audit-records", "get", "listAuditRecords"),
        ("/api/ops/v1/audit-intents", "post", "submitAuditIntent"),
        ("/api/ops/v1/operation-templates", "post", "createOperationTemplate"),
        ("/api/ops/v1/operation-templates", "get", "listOperationTemplates"),
        ("/api/ops/v1/operation-templates/{operationCode}", "get", "getOperationTemplate")
    ];

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

        var missingOperationIds = operations
            .Where(operation => string.IsNullOrWhiteSpace(operation.OperationId))
            .Select(operation => operation.Name)
            .ToArray();
        Assert.Empty(missingOperationIds);

        var duplicateOperationIds = operations
            .Where(operation => !string.IsNullOrWhiteSpace(operation.OperationId))
            .GroupBy(operation => operation.OperationId!, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(", ", group.Select(operation => operation.Name))}")
            .ToArray();
        Assert.Empty(duplicateOperationIds);
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
