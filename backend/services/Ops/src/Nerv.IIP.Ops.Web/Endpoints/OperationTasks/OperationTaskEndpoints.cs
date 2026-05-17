using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.Contracts.ConnectorProtocol;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Ops.Domain;
using Nerv.IIP.Ops.Web.Application.Commands;
using Nerv.IIP.Ops.Web.Application.Queries;

namespace Nerv.IIP.Ops.Web.Endpoints.OperationTasks;

[HttpPost("/api/ops/v1/operation-tasks")]
[AllowAnonymous]
public sealed class CreateOperationTaskEndpoint(IMediator mediator) : Endpoint<CreateOperationTaskRequest>
{
    public override async Task HandleAsync(CreateOperationTaskRequest req, CancellationToken ct)
    {
        try
        {
            var task = await mediator.Send(new CreateOperationTaskCommand(req, DateTimeOffset.UtcNow), ct);
            await HttpContext.Response.WriteAsJsonAsync(task, ct);
        }
        catch (InvalidOperationTaskRequestException ex)
        {
            await OpsEndpointResults.WriteBadRequestAsync(HttpContext, ex.Message, ct);
        }
    }
}

[HttpGet("/api/ops/v1/operation-tasks/{operationTaskId}")]
[AllowAnonymous]
public sealed class GetOperationTaskEndpoint(IMediator mediator) : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        var operationTaskId = Route<string>("operationTaskId")!;
        try
        {
            var task = await mediator.Send(new GetOperationTaskQuery(operationTaskId), ct);
            await HttpContext.Response.WriteAsJsonAsync(task, ct);
        }
        catch (OperationTaskNotFoundException ex)
        {
            await OpsEndpointResults.WriteNotFoundAsync(HttpContext, ex.Message, ct);
        }
    }
}

[HttpGet("/api/ops/v1/operation-tasks/pending")]
[AllowAnonymous]
public sealed class GetPendingOperationTasksEndpoint(IMediator mediator) : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        var connectorHostId = Query<string>("connectorHostId")!;
        var organizationId = Query<string>("organizationId")!;
        var environmentId = Query<string>("environmentId")!;
        if (!OpsConnectorEndpointResults.ConnectorHostAuthorized(HttpContext, connectorHostId, organizationId, environmentId))
        {
            await OpsConnectorEndpointResults.WriteUnauthorizedAsync(HttpContext, ct);
            return;
        }

        var pending = await mediator.Send(new DispatchPendingOperationsCommand(
            organizationId,
            environmentId,
            connectorHostId,
            Query<int>("take"),
            DateTimeOffset.UtcNow), ct);
        await HttpContext.Response.WriteAsJsonAsync(pending, ct);
    }
}

[HttpPost("/api/ops/v1/operation-results")]
[AllowAnonymous]
public sealed class RecordOperationResultEndpoint(IMediator mediator) : Endpoint<OperationResult>
{
    public override async Task HandleAsync(OperationResult req, CancellationToken ct)
    {
        if (!OpsConnectorEndpointResults.ConnectorHostAuthorized(HttpContext, req.Context.ConnectorHostId, req.Context.OrganizationId, req.Context.EnvironmentId))
        {
            await OpsConnectorEndpointResults.WriteUnauthorizedAsync(HttpContext, ct);
            return;
        }

        try
        {
            var task = await mediator.Send(new RecordOperationResultCommand(req), ct);
            await HttpContext.Response.WriteAsJsonAsync(task, ct);
        }
        catch (InvalidOperationResultException ex)
        {
            await OpsEndpointResults.WriteBadRequestAsync(HttpContext, ex.Message, ct);
        }
        catch (OperationTaskNotFoundException ex)
        {
            await OpsEndpointResults.WriteBadRequestAsync(HttpContext, ex.Message, ct);
        }
    }
}

internal static class OpsEndpointResults
{
    public static async Task WriteNotFoundAsync(HttpContext context, string detail, CancellationToken cancellationToken)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        await context.Response.WriteAsJsonAsync(new { title = "Not Found", detail, status = StatusCodes.Status404NotFound }, cancellationToken);
    }

    public static async Task WriteBadRequestAsync(HttpContext context, string detail, CancellationToken cancellationToken)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { title = "Bad Request", detail, status = StatusCodes.Status400BadRequest }, cancellationToken);
    }
}

internal static class OpsConnectorEndpointResults
{
    public static bool ConnectorHostAuthorized(HttpContext context, string connectorHostId, string organizationId, string environmentId)
    {
        return context.Request.Headers.TryGetValue("X-Connector-Host-Id", out var hostId)
            && context.Request.Headers.TryGetValue("X-Connector-Secret", out var secret)
            && context.Request.Headers.TryGetValue("X-Organization-Id", out var headerOrganizationId)
            && context.Request.Headers.TryGetValue("X-Environment-Id", out var headerEnvironmentId)
            && hostId == connectorHostId
            && secret == "local-connector-secret"
            && headerOrganizationId == organizationId
            && headerEnvironmentId == environmentId;
    }

    public static async Task WriteUnauthorizedAsync(HttpContext context, CancellationToken cancellationToken)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { title = "Unauthorized", detail = "Invalid Connector Host credential.", status = StatusCodes.Status401Unauthorized }, cancellationToken);
    }
}
