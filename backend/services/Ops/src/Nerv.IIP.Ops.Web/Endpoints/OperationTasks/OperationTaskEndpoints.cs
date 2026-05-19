using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.Contracts.ConnectorProtocol;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Ops.Domain;
using Nerv.IIP.Ops.Web.Application.Auth;
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
public sealed class GetPendingOperationTasksEndpoint(
    IMediator mediator,
    IOpsConnectorCredentialValidator connectorCredentialValidator,
    ILogger<GetPendingOperationTasksEndpoint> logger) : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        var connectorHostId = Query<string>("connectorHostId")!;
        var organizationId = Query<string>("organizationId")!;
        var environmentId = Query<string>("environmentId")!;
        if (!await OpsConnectorEndpointResults.ConnectorHostAuthorizedAsync(
                HttpContext,
                connectorCredentialValidator,
                logger,
                connectorHostId,
                organizationId,
                environmentId,
                "ops.tasks.read",
                ct))
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
public sealed class RecordOperationResultEndpoint(
    IMediator mediator,
    IOpsConnectorCredentialValidator connectorCredentialValidator,
    ILogger<RecordOperationResultEndpoint> logger) : Endpoint<OperationResult>
{
    public override async Task HandleAsync(OperationResult req, CancellationToken ct)
    {
        if (!await OpsConnectorEndpointResults.ConnectorHostAuthorizedAsync(
                HttpContext,
                connectorCredentialValidator,
                logger,
                req.Context.ConnectorHostId,
                req.Context.OrganizationId,
                req.Context.EnvironmentId,
                "ops.results.write",
                ct))
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
    public static async Task<bool> ConnectorHostAuthorizedAsync(
        HttpContext context,
        IOpsConnectorCredentialValidator connectorCredentialValidator,
        ILogger logger,
        string connectorHostId,
        string organizationId,
        string environmentId,
        string requiredPermission,
        CancellationToken cancellationToken)
    {
        var headerHostId = context.Request.Headers["X-Connector-Host-Id"].ToString();
        var secret = context.Request.Headers["X-Connector-Secret"].ToString();
        var headerOrganizationId = context.Request.Headers["X-Organization-Id"].ToString();
        var headerEnvironmentId = context.Request.Headers["X-Environment-Id"].ToString();

        if (string.IsNullOrWhiteSpace(headerHostId)
            || string.IsNullOrWhiteSpace(secret)
            || string.IsNullOrWhiteSpace(headerOrganizationId)
            || string.IsNullOrWhiteSpace(headerEnvironmentId))
        {
            LogCredentialRejected(
                logger,
                context,
                connectorHostId,
                organizationId,
                environmentId,
                requiredPermission,
                "missing-connector-headers");
            return false;
        }

        if (!string.Equals(headerHostId, connectorHostId, StringComparison.Ordinal)
            || !string.Equals(headerOrganizationId, organizationId, StringComparison.Ordinal)
            || !string.Equals(headerEnvironmentId, environmentId, StringComparison.Ordinal))
        {
            LogCredentialRejected(
                logger,
                context,
                connectorHostId,
                organizationId,
                environmentId,
                requiredPermission,
                "connector-scope-mismatch");
            return false;
        }

        var result = await connectorCredentialValidator.ValidateAsync(
            new OpsConnectorCredentialValidationRequest(
                connectorHostId,
                secret,
                organizationId,
                environmentId,
                requiredPermission),
            cancellationToken);
        if (!result.IsAuthorized)
        {
            LogCredentialRejected(
                logger,
                context,
                connectorHostId,
                organizationId,
                environmentId,
                requiredPermission,
                result.Reason);
            return false;
        }

        var principalMatchesScope = string.Equals(result.PrincipalType, "connector-host", StringComparison.Ordinal)
            && string.Equals(result.ConnectorHostId, connectorHostId, StringComparison.Ordinal)
            && string.Equals(result.OrganizationId, organizationId, StringComparison.Ordinal)
            && string.Equals(result.EnvironmentId, environmentId, StringComparison.Ordinal);
        if (!principalMatchesScope)
        {
            LogCredentialRejected(
                logger,
                context,
                connectorHostId,
                organizationId,
                environmentId,
                requiredPermission,
                "connector-principal-scope-mismatch");
            return false;
        }

        return true;
    }

    public static async Task WriteUnauthorizedAsync(HttpContext context, CancellationToken cancellationToken)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { title = "Unauthorized", detail = "Invalid Connector Host credential.", status = StatusCodes.Status401Unauthorized }, cancellationToken);
    }

    private static void LogCredentialRejected(
        ILogger logger,
        HttpContext context,
        string connectorHostId,
        string organizationId,
        string environmentId,
        string requiredPermission,
        string reason)
    {
        logger.LogWarning(
            "ConnectorCredentialRejected ConnectorHostId={ConnectorHostId} OrganizationId={OrganizationId} EnvironmentId={EnvironmentId} RequiredPermission={RequiredPermission} Reason={Reason} Path={Path}",
            connectorHostId,
            organizationId,
            environmentId,
            requiredPermission,
            reason,
            context.Request.Path.ToString());
    }
}
