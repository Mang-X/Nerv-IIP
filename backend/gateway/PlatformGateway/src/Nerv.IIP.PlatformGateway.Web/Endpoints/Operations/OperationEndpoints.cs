using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.PlatformGateway.Web.Application.OpsClient;

namespace Nerv.IIP.PlatformGateway.Web.Endpoints.Operations;

public sealed record RestartInstanceRequest(string OrganizationId, string EnvironmentId, string Reason, string IdempotencyKey);

[HttpPost("/api/console/v1/instances/{instanceKey}/operations/restart")]
[AllowAnonymous]
public sealed class RestartInstanceEndpoint(IGatewayOpsClient opsClient) : Endpoint<RestartInstanceRequest, OperationTaskResponse>
{
    public override async Task HandleAsync(RestartInstanceRequest req, CancellationToken ct)
    {
        var operationRequest = new CreateOperationTaskRequest(
            req.OrganizationId,
            req.EnvironmentId,
            Route<string>("instanceKey")!,
            "lifecycle.restart",
            req.IdempotencyKey,
            HttpContext.Request.Headers.TryGetValue("X-User-Id", out var userId) ? userId.ToString() : "local-admin",
            req.Reason,
            HttpContext.TraceIdentifier,
            new Dictionary<string, string>());

        try
        {
            await HttpContext.Response.WriteAsJsonAsync(await opsClient.CreateTaskAsync(operationRequest, ct), ct);
        }
        catch (HttpRequestException ex)
        {
            await GatewayOpsEndpointResults.WriteBadGatewayAsync(HttpContext, ex, ct);
        }
    }
}

[HttpGet("/api/console/v1/operation-tasks/{operationTaskId}")]
[AllowAnonymous]
public sealed class GetConsoleOperationTaskEndpoint(IGatewayOpsClient opsClient) : EndpointWithoutRequest<OperationTaskResponse>
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        try
        {
            await HttpContext.Response.WriteAsJsonAsync(await opsClient.GetTaskAsync(Route<string>("operationTaskId")!, ct), ct);
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
        context.Response.StatusCode = StatusCodes.Status502BadGateway;
        await context.Response.WriteAsJsonAsync(new { title = "Bad Gateway", detail = $"Ops unavailable: {exception.Message}", status = StatusCodes.Status502BadGateway }, cancellationToken);
    }
}
