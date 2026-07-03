using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Nerv.IIP.Notification.Web.Tests;

public sealed class NotificationOpenApiTests
{
    [Fact]
    public async Task OpenApi_document_exposes_endpoint_operation_ids()
    {
        await using var factory = CreateFactory();
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
        ("/api/notifications/v1/intents", "post", "submitNotificationIntent"),
        ("/api/notifications/v1/messages", "get", "listNotificationMessages"),
        ("/api/notifications/v1/messages/{messageId}/read", "post", "markNotificationMessageRead"),
        ("/api/notifications/v1/messages/read-batch", "post", "markNotificationMessagesRead"),
        ("/api/notifications/v1/tasks", "get", "listNotificationTasks"),
        ("/api/notifications/v1/delivery/recipient-channel-bindings", "post", "upsertNotificationRecipientChannelBinding"),
        ("/api/notifications/v1/delivery/preferences", "post", "upsertNotificationPreference"),
        ("/api/notifications/v1/delivery/subscriptions", "post", "upsertNotificationSubscription")
    ];

    private static WebApplicationFactory<Program> CreateFactory()
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Persistence:Provider"] = "InMemory",
                        ["Persistence:InMemoryDatabaseName"] = Guid.NewGuid().ToString("N"),
                    }));
            });
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
