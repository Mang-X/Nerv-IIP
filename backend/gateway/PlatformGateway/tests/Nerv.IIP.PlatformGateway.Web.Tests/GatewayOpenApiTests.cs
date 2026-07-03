using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Nerv.IIP.PlatformGateway.Web.Tests;

public sealed class GatewayOpenApiTests
{
    [Fact]
    public void Gateway_openapi_operation_ids_are_not_defined_by_program_endpoint_switch()
    {
        var program = File.ReadAllText(FindGatewayProgramPath());

        Assert.DoesNotContain("ctx.EndpointType.Name switch", program, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Gateway_exports_console_openapi_document_with_stable_operation_ids()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        var json = await client.GetStringAsync("/swagger/v1/swagger.json");
        using var document = JsonDocument.Parse(json);
        var paths = document.RootElement.GetProperty("paths");
        AssertOperationIdsAreUnique(document);

        Assert.True(paths.TryGetProperty("/api/console/v1/instances", out var instances));
        var list = instances.GetProperty("get");
        Assert.True(list.TryGetProperty("operationId", out var listOperation));
        Assert.Equal("listConsoleInstances", listOperation.GetString());
        AssertJsonResponseSchema(list, "200", "NervIIPContractsAppHubQueriesInstanceListResponse");
        AssertParameterNames(list, "organizationId", "environmentId", "pageIndex", "pageSize", "sortBy", "sortOrder", "filterSearch");
        AssertParameterRequired(list, "organizationId", true);
        AssertParameterRequired(list, "environmentId", true);
        AssertParameterRequired(list, "pageIndex", false);
        AssertParameterRequired(list, "pageSize", false);
        AssertParameterRequired(list, "sortBy", false);
        AssertParameterRequired(list, "sortOrder", false);
        AssertParameterRequired(list, "filterSearch", false);

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

        Assert.Equal("listConsoleIamUsers", paths.GetProperty("/api/console/v1/iam/users").GetProperty("get").GetProperty("operationId").GetString());
        var createIamUser = paths.GetProperty("/api/console/v1/iam/users").GetProperty("post");
        Assert.Equal("createConsoleIamUser", createIamUser.GetProperty("operationId").GetString());
        AssertResponseStatus(createIamUser, "201");
        AssertNoResponseStatus(createIamUser, "200");
        Assert.Equal("updateConsoleIamUser", paths.GetProperty("/api/console/v1/iam/users/{userId}").GetProperty("patch").GetProperty("operationId").GetString());
        Assert.Equal("disableConsoleIamUser", paths.GetProperty("/api/console/v1/iam/users/{userId}/disable").GetProperty("post").GetProperty("operationId").GetString());
        Assert.Equal("resetConsoleIamUserPassword", paths.GetProperty("/api/console/v1/iam/users/{userId}/reset-password").GetProperty("post").GetProperty("operationId").GetString());
        Assert.Equal("listConsoleIamRoles", paths.GetProperty("/api/console/v1/iam/roles").GetProperty("get").GetProperty("operationId").GetString());
        var createIamRole = paths.GetProperty("/api/console/v1/iam/roles").GetProperty("post");
        Assert.Equal("createConsoleIamRole", createIamRole.GetProperty("operationId").GetString());
        AssertResponseStatus(createIamRole, "201");
        AssertNoResponseStatus(createIamRole, "200");
        Assert.Equal("updateConsoleIamRolePermissions", paths.GetProperty("/api/console/v1/iam/roles/{roleId}/permissions").GetProperty("patch").GetProperty("operationId").GetString());
        Assert.Equal("listConsoleIamPermissions", paths.GetProperty("/api/console/v1/iam/permissions").GetProperty("get").GetProperty("operationId").GetString());
        Assert.Equal("listConsoleIamSessions", paths.GetProperty("/api/console/v1/iam/sessions").GetProperty("get").GetProperty("operationId").GetString());
        Assert.Equal("revokeConsoleIamSession", paths.GetProperty("/api/console/v1/iam/sessions/{sessionId}/revoke").GetProperty("post").GetProperty("operationId").GetString());

        Assert.Equal("listConsoleNotificationMessages", paths.GetProperty("/api/console/v1/notifications/messages").GetProperty("get").GetProperty("operationId").GetString());
        Assert.Equal("listConsoleNotificationTasks", paths.GetProperty("/api/console/v1/notifications/tasks").GetProperty("get").GetProperty("operationId").GetString());
        Assert.Equal("submitConsoleNotificationIntent", paths.GetProperty("/api/console/v1/notifications/intents").GetProperty("post").GetProperty("operationId").GetString());
        Assert.Equal("markConsoleNotificationMessageRead", paths.GetProperty("/api/console/v1/notifications/messages/{messageId}/read").GetProperty("post").GetProperty("operationId").GetString());
        Assert.Equal("markConsoleNotificationMessagesRead", paths.GetProperty("/api/console/v1/notifications/messages/read-batch").GetProperty("post").GetProperty("operationId").GetString());
        Assert.Equal("listConsoleNotificationDeadLetters", paths.GetProperty("/api/console/v1/notifications/dlq").GetProperty("get").GetProperty("operationId").GetString());
        Assert.Equal("getConsoleNotificationDeadLetter", paths.GetProperty("/api/console/v1/notifications/dlq/{deadLetterId}").GetProperty("get").GetProperty("operationId").GetString());
        Assert.Equal("replayConsoleNotificationDeadLetter", paths.GetProperty("/api/console/v1/notifications/dlq/{deadLetterId}/replay").GetProperty("post").GetProperty("operationId").GetString());
        Assert.Equal("replayConsoleNotificationDeadLetters", paths.GetProperty("/api/console/v1/notifications/dlq/replay-batch").GetProperty("post").GetProperty("operationId").GetString());
        Assert.Equal("ignoreConsoleNotificationDeadLetter", paths.GetProperty("/api/console/v1/notifications/dlq/{deadLetterId}/ignore").GetProperty("post").GetProperty("operationId").GetString());

        var listFiles = paths.GetProperty("/api/console/v1/files").GetProperty("get");
        Assert.Equal("listConsoleFiles", listFiles.GetProperty("operationId").GetString());
        AssertParameterNames(listFiles, "filePurpose", "uploaderId", "createdFromUtc", "createdToUtc", "status", "skip", "take");
        Assert.Equal("createConsoleFileUploadSession", paths.GetProperty("/api/console/v1/files/upload-sessions").GetProperty("post").GetProperty("operationId").GetString());
        Assert.Equal("completeConsoleFileUploadSession", paths.GetProperty("/api/console/v1/files/upload-sessions/{uploadSessionId}/complete").GetProperty("post").GetProperty("operationId").GetString());
        Assert.Equal("getConsoleFileMetadata", paths.GetProperty("/api/console/v1/files/{fileId}").GetProperty("get").GetProperty("operationId").GetString());
        Assert.Equal("createConsoleFileDownloadGrant", paths.GetProperty("/api/console/v1/files/{fileId}/download-grants").GetProperty("post").GetProperty("operationId").GetString());
        Assert.Equal("getConsoleTusUploadOffset", paths.GetProperty("/api/console/v1/files/tus/{uploadSessionId}").GetProperty("head").GetProperty("operationId").GetString());
        Assert.Equal("patchConsoleTusUpload", paths.GetProperty("/api/console/v1/files/tus/{uploadSessionId}").GetProperty("patch").GetProperty("operationId").GetString());
        Assert.Equal("downloadConsoleFileGrantContent", paths.GetProperty("/api/console/v1/files/download-grants/{downloadGrantId}/content").GetProperty("get").GetProperty("operationId").GetString());

        var queryLogs = paths.GetProperty("/api/console/v1/logs/query").GetProperty("post");
        Assert.Equal("queryConsoleLogs", queryLogs.GetProperty("operationId").GetString());
        AssertJsonResponseSchema(queryLogs, "200", "NervIIPPlatformGatewayWebApplicationLogsConsoleLogQueryResponse");

        Assert.Equal("HealthEndpoint", paths.GetProperty("/health").GetProperty("get").GetProperty("operationId").GetString());
        Assert.Equal("GetBuildInfoEndpoint", paths.GetProperty("/internal/gateway/v1/build-info").GetProperty("get").GetProperty("operationId").GetString());
        Assert.Equal("InvalidateGatewayCacheEndpoint", paths.GetProperty("/internal/gateway/cache/invalidate").GetProperty("post").GetProperty("operationId").GetString());
    }

    private static string FindGatewayProgramPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "backend",
                "gateway",
                "PlatformGateway",
                "src",
                "Nerv.IIP.PlatformGateway.Web",
                "Program.cs");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not find PlatformGateway Program.cs.");
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

        var responseTypeName = schemaName
            .Replace("NervIIPContractsAppHubQueries", string.Empty)
            .Replace("NervIIPContractsOps", string.Empty)
            .Replace("NervIIPPlatformGatewayWebApplicationAuth", string.Empty)
            .Replace("NervIIPPlatformGatewayWebApplicationLogs", string.Empty);

        Assert.StartsWith("#/components/schemas/NetCorePalExtensionsDtoResponseDataOf", response);
        Assert.Contains(responseTypeName, response);
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

    private static void AssertResponseStatus(JsonElement operation, string statusCode)
    {
        Assert.True(
            operation.GetProperty("responses").TryGetProperty(statusCode, out _),
            $"Expected response status {statusCode}.");
    }

    private static void AssertNoResponseStatus(JsonElement operation, string statusCode)
    {
        Assert.False(
            operation.GetProperty("responses").TryGetProperty(statusCode, out _),
            $"Did not expect response status {statusCode}.");
    }
}
