using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessGatewayNotificationOpenApiTests
{
    [Fact]
    public async Task Business_gateway_exports_the_PDA_notification_facade_on_the_existing_business_console_boundary()
    {
        using var document = JsonDocument.Parse(await BusinessGatewayTestHost.GetOpenApiDocumentAsync());
        var paths = document.RootElement.GetProperty("paths");

        AssertOperation(paths, "/api/business-console/v1/notifications/messages", "get", "listBusinessConsoleNotificationMessages");
        AssertOperation(paths, "/api/business-console/v1/notifications/tasks", "get", "listBusinessConsoleNotificationTasks");
        AssertOperation(paths, "/api/business-console/v1/notifications/messages/{messageId}/read", "post", "markBusinessConsoleNotificationMessageRead");
    }

    private static void AssertOperation(JsonElement paths, string path, string method, string operationId)
    {
        var operation = paths.GetProperty(path).GetProperty(method);
        Assert.Equal(operationId, operation.GetProperty("operationId").GetString());
        Assert.True(operation.GetProperty("security").GetArrayLength() > 0);
    }
}
