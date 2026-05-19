using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.PlatformGateway.Web.Application.Auth;
using Nerv.IIP.PlatformGateway.Web.Application.OpsClient;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.PlatformGateway.Web.Endpoints.Operations;

public sealed record RestartInstanceRequest(string OrganizationId, string EnvironmentId, string Reason, string IdempotencyKey);

[HttpPost("/api/console/v1/instances/{instanceKey}/operations/restart")]
[Authorize(Policy = GatewayPolicies.ConsoleAuthenticated)]
public sealed class RestartInstanceEndpoint(
    IGatewayOpsClient opsClient,
    IGatewayAuthorizationClient auth) : Endpoint<RestartInstanceRequest, ResponseData<OperationTaskResponse>>
{
    public override async Task HandleAsync(RestartInstanceRequest req, CancellationToken ct)
    {
        var principal = await GatewayAuthorization.RequirePermissionAsync(
            HttpContext,
            auth,
            new GatewayPermissionRequirement(
                GatewayPermissions.OpsTasksCreate,
                req.OrganizationId,
                req.EnvironmentId,
                "application-instance",
                Route<string>("instanceKey")),
            ct);
        if (principal is null)
        {
            return;
        }

        var operationRequest = new CreateOperationTaskRequest(
            req.OrganizationId,
            req.EnvironmentId,
            Route<string>("instanceKey")!,
            "lifecycle.restart",
            req.IdempotencyKey,
            principal.PrincipalId ?? "unknown",
            req.Reason,
            HttpContext.TraceIdentifier,
            new Dictionary<string, string>());

        try
        {
            var response = await opsClient.CreateTaskAsync(operationRequest, ct);
            await Send.OkAsync(response.AsResponseData(), ct);
        }
        catch (HttpRequestException ex)
        {
            await GatewayOpsEndpointResults.WriteBadGatewayAsync(HttpContext, ex, ct);
        }
    }
}

public sealed class GetConsoleOperationTaskRequest
{
    public string OperationTaskId { get; set; } = string.Empty;
    public string OrganizationId { get; set; } = string.Empty;
    public string EnvironmentId { get; set; } = string.Empty;
}

[HttpGet("/api/console/v1/operation-tasks/{operationTaskId}")]
[Authorize(Policy = GatewayPolicies.ConsoleAuthenticated)]
public sealed class GetConsoleOperationTaskEndpoint(
    IGatewayOpsClient opsClient,
    IGatewayAuthorizationClient auth) : Endpoint<GetConsoleOperationTaskRequest, ResponseData<OperationTaskResponse>>
{
    public override async Task HandleAsync(GetConsoleOperationTaskRequest req, CancellationToken ct)
    {
        var principal = await GatewayAuthorization.RequirePermissionAsync(
            HttpContext,
            auth,
            new GatewayPermissionRequirement(
                GatewayPermissions.OpsTasksRead,
                req.OrganizationId,
                req.EnvironmentId,
                "operation-task",
                req.OperationTaskId),
            ct);
        if (principal is null)
        {
            return;
        }

        try
        {
            var response = await opsClient.GetTaskAsync(req.OperationTaskId, ct);
            await Send.OkAsync(response.AsResponseData(), ct);
        }
        catch (HttpRequestException ex)
        {
            await GatewayOpsEndpointResults.WriteBadGatewayAsync(HttpContext, ex, ct);
        }
    }
}

internal static class GatewayOpsEndpointResults
{
    public static async Task WriteBadGatewayAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        await ResponseDataEndpointResults.WriteErrorAsync(
            context,
            StatusCodes.Status502BadGateway,
            $"Ops unavailable: {exception.Message}",
            cancellationToken);
    }
}
