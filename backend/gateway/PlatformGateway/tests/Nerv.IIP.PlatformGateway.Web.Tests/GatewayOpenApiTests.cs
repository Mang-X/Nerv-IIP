using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Nerv.IIP.PlatformGateway.Web.Tests;

public sealed class GatewayOpenApiTests
{
    [Fact]
    public async Task Gateway_exports_console_openapi_document_with_stable_operation_ids()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        var json = await client.GetStringAsync("/swagger/v1/swagger.json");
        using var document = JsonDocument.Parse(json);
        var paths = document.RootElement.GetProperty("paths");

        Assert.True(paths.TryGetProperty("/api/console/v1/instances", out var instances));
        var list = instances.GetProperty("get");
        Assert.True(list.TryGetProperty("operationId", out var listOperation));
        Assert.Equal("listConsoleInstances", listOperation.GetString());
        AssertJsonResponseSchema(list, "200", "NervIIPContractsAppHubQueriesInstanceListResponse");
        AssertParameterNames(list, "organizationId", "environmentId", "pageNumber", "pageSize", "search");
        AssertParameterRequired(list, "organizationId", true);
        AssertParameterRequired(list, "environmentId", true);
        AssertParameterRequired(list, "pageNumber", false);
        AssertParameterRequired(list, "pageSize", false);
        AssertParameterRequired(list, "search", false);

        var detail = paths.GetProperty("/api/console/v1/instances/{instanceKey}");
        var detailGet = detail.GetProperty("get");
        Assert.Equal("getConsoleInstanceDetail", detailGet.GetProperty("operationId").GetString());
        AssertJsonResponseSchema(detailGet, "200", "NervIIPContractsAppHubQueriesInstanceDetailResponse");
        AssertParameterNames(detailGet, "organizationId", "environmentId", "instanceKey");

        var restart = paths.GetProperty("/api/console/v1/instances/{instanceKey}/operations/restart");
        Assert.Equal("restartConsoleInstance", restart.GetProperty("post").GetProperty("operationId").GetString());

        var operationDetail = paths.GetProperty("/api/console/v1/operation-tasks/{operationTaskId}");
        Assert.Equal("getConsoleOperationTask", operationDetail.GetProperty("get").GetProperty("operationId").GetString());

        var login = paths.GetProperty("/api/console/v1/auth/login");
        Assert.Equal("loginConsoleUser", login.GetProperty("post").GetProperty("operationId").GetString());

        var refresh = paths.GetProperty("/api/console/v1/auth/refresh");
        Assert.Equal("refreshConsoleSession", refresh.GetProperty("post").GetProperty("operationId").GetString());

        var logout = paths.GetProperty("/api/console/v1/auth/logout");
        Assert.Equal("logoutConsoleSession", logout.GetProperty("post").GetProperty("operationId").GetString());

        var me = paths.GetProperty("/api/console/v1/auth/me");
        Assert.Equal("getConsolePrincipal", me.GetProperty("get").GetProperty("operationId").GetString());
    }

    private static void AssertJsonResponseSchema(JsonElement operation, string statusCode, string schemaName)
    {
        var response = operation
            .GetProperty("responses")
            .GetProperty(statusCode)
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("schema")
            .GetProperty("$ref")
            .GetString();

        Assert.Equal($"#/components/schemas/{schemaName}", response);
    }

    private static void AssertParameterNames(JsonElement operation, params string[] names)
    {
        var actual = operation
            .GetProperty("parameters")
            .EnumerateArray()
            .Select(parameter => parameter.GetProperty("name").GetString())
            .ToHashSet();

        foreach (var name in names)
        {
            Assert.Contains(name, actual);
        }
    }

    private static void AssertParameterRequired(JsonElement operation, string name, bool expected)
    {
        var parameter = operation
            .GetProperty("parameters")
            .EnumerateArray()
            .Single(parameter => parameter.GetProperty("name").GetString() == name);

        var actual = parameter.TryGetProperty("required", out var required) && required.GetBoolean();
        Assert.Equal(expected, actual);
    }
}
