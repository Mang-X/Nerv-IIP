using System.Security.Claims;
using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.Contracts.ConnectorProtocol;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Ops.Domain;
using Nerv.IIP.Ops.Web.Application.Auth;
using Nerv.IIP.Ops.Web.Application.Commands;
using Nerv.IIP.Ops.Web.Application.Queries;
using Nerv.IIP.Ops.Web.Endpoints;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.Ops.Web.Endpoints.OperationTasks;

public sealed record ListOperationTasksRequest(
    string OrganizationId,
    string EnvironmentId,
    int? Page,
    int? PageSize);

[HttpGet("/api/ops/v1/operation-tasks")]
[Authorize(Policy = InternalServiceAuthorizationPolicy.Name)]
public sealed class ListOperationTasksEndpoint(IMediator mediator)
    : Endpoint<ListOperationTasksRequest, ResponseData<PagedOperationTaskListResponse>>
{
    public override async Task HandleAsync(ListOperationTasksRequest req, CancellationToken ct)
    {
        var tasks = await mediator.Send(new ListOperationTasksQuery(
            req.OrganizationId,
            req.EnvironmentId,
            req.Page,
            req.PageSize), ct);
        await Send.OkAsync(tasks.AsResponseData(), ct);
    }
}

[HttpPost("/api/ops/v1/operation-tasks")]
[Authorize(Policy = InternalServiceAuthorizationPolicy.Name)]
public sealed class CreateOperationTaskEndpoint(IMediator mediator) : Endpoint<CreateOperationTaskRequest, ResponseData<OperationTaskResponse>>
{
    public override async Task HandleAsync(CreateOperationTaskRequest req, CancellationToken ct)
    {
        try
        {
            var task = await mediator.Send(new CreateOperationTaskCommand(req, DateTimeOffset.UtcNow), ct);
            await Send.OkAsync(task.AsResponseData(), ct);
        }
        catch (InvalidOperationTaskRequestException ex)
        {
            await OpsEndpointResults.WriteBadRequestAsync(HttpContext, ex.Message, ct);
        }
    }
}

[HttpGet("/api/ops/v1/operation-tasks/{operationTaskId}")]
[Authorize(Policy = InternalServiceAuthorizationPolicy.Name)]
public sealed class GetOperationTaskEndpoint(IMediator mediator) : EndpointWithoutRequest<ResponseData<OperationTaskResponse>>
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        var operationTaskId = Route<string>("operationTaskId")!;
        try
        {
            var task = await mediator.Send(new GetOperationTaskQuery(operationTaskId), ct);
            await Send.OkAsync(task.AsResponseData(), ct);
        }
        catch (OperationTaskNotFoundException ex)
        {
            await OpsEndpointResults.WriteNotFoundAsync(HttpContext, ex.Message, ct);
        }
    }
}

[HttpPost("/api/ops/v1/operation-tasks/{operationTaskId}/approval/approve")]
[Authorize(Policy = InternalServiceAuthorizationPolicy.Name)]
public sealed class ApproveOperationApprovalEndpoint(IMediator mediator)
    : Endpoint<DecideOperationApprovalRequest, ResponseData<OperationTaskResponse>>
{
    public override async Task HandleAsync(DecideOperationApprovalRequest req, CancellationToken ct)
    {
        try
        {
            var operationTaskId = Route<string>("operationTaskId")!;
            var trustedRequest = req with { Actor = OpsApprovalActorResolver.ResolveActor(HttpContext) };
            var task = await mediator.Send(new ApproveOperationApprovalCommand(operationTaskId, trustedRequest, DateTimeOffset.UtcNow), ct);
            await Send.OkAsync(task.AsResponseData(), ct);
        }
        catch (InvalidOperationTaskRequestException ex)
        {
            await OpsEndpointResults.WriteBadRequestAsync(HttpContext, ex.Message, ct);
        }
        catch (OperationTaskNotFoundException ex)
        {
            await OpsEndpointResults.WriteNotFoundAsync(HttpContext, ex.Message, ct);
        }
    }
}

[HttpPost("/api/ops/v1/operation-tasks/{operationTaskId}/approval/reject")]
[Authorize(Policy = InternalServiceAuthorizationPolicy.Name)]
public sealed class RejectOperationApprovalEndpoint(IMediator mediator)
    : Endpoint<DecideOperationApprovalRequest, ResponseData<OperationTaskResponse>>
{
    public override async Task HandleAsync(DecideOperationApprovalRequest req, CancellationToken ct)
    {
        try
        {
            var operationTaskId = Route<string>("operationTaskId")!;
            var trustedRequest = req with { Actor = OpsApprovalActorResolver.ResolveActor(HttpContext) };
            var task = await mediator.Send(new RejectOperationApprovalCommand(operationTaskId, trustedRequest, DateTimeOffset.UtcNow), ct);
            await Send.OkAsync(task.AsResponseData(), ct);
        }
        catch (InvalidOperationTaskRequestException ex)
        {
            await OpsEndpointResults.WriteBadRequestAsync(HttpContext, ex.Message, ct);
        }
        catch (OperationTaskNotFoundException ex)
        {
            await OpsEndpointResults.WriteNotFoundAsync(HttpContext, ex.Message, ct);
        }
    }
}

[HttpGet("/api/ops/v1/operation-tasks/pending")]
[Authorize(Policy = InternalServiceAuthorizationPolicy.Name)]
public sealed class GetPendingOperationTasksEndpoint(
    IMediator mediator,
    IOpsConnectorCredentialValidator connectorCredentialValidator,
    ILogger<GetPendingOperationTasksEndpoint> logger) : EndpointWithoutRequest<ResponseData<PendingOperationTasksResponse>>
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
            300,
            3,
            DateTimeOffset.UtcNow), ct);
        await Send.OkAsync(pending.AsResponseData(), ct);
    }
}

[HttpPost("/api/ops/v1/operation-tasks/claims")]
[Authorize(Policy = InternalServiceAuthorizationPolicy.Name)]
public sealed class ClaimOperationTasksEndpoint(
    IMediator mediator,
    IOpsConnectorCredentialValidator connectorCredentialValidator,
    ILogger<ClaimOperationTasksEndpoint> logger) : Endpoint<ClaimOperationTasksRequest, ResponseData<PendingOperationTasksResponse>>
{
    public override async Task HandleAsync(ClaimOperationTasksRequest req, CancellationToken ct)
    {
        if (!await OpsConnectorEndpointResults.ConnectorHostAuthorizedAsync(
                HttpContext,
                connectorCredentialValidator,
                logger,
                req.ConnectorHostId,
                req.OrganizationId,
                req.EnvironmentId,
                "ops.tasks.claim",
                ct))
        {
            await OpsConnectorEndpointResults.WriteUnauthorizedAsync(HttpContext, ct);
            return;
        }

        var pending = await mediator.Send(new DispatchPendingOperationsCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.ConnectorHostId,
            req.Take,
            req.LeaseDurationSeconds,
            req.MaxAttempts,
            DateTimeOffset.UtcNow), ct);
        await Send.OkAsync(pending.AsResponseData(), ct);
    }
}

[HttpPost("/api/ops/v1/operation-tasks/{operationTaskId}/lease/abandon")]
[Authorize(Policy = InternalServiceAuthorizationPolicy.Name)]
public sealed class AbandonOperationTaskLeaseEndpoint(
    IMediator mediator,
    IOpsConnectorCredentialValidator connectorCredentialValidator,
    ILogger<AbandonOperationTaskLeaseEndpoint> logger) : Endpoint<AbandonOperationTaskLeaseRequest, ResponseData<OperationTaskResponse>>
{
    public override async Task HandleAsync(AbandonOperationTaskLeaseRequest req, CancellationToken ct)
    {
        if (!await OpsConnectorEndpointResults.ConnectorHostAuthorizedAsync(
                HttpContext,
                connectorCredentialValidator,
                logger,
                req.ConnectorHostId,
                req.OrganizationId,
                req.EnvironmentId,
                "ops.tasks.claim",
                ct))
        {
            await OpsConnectorEndpointResults.WriteUnauthorizedAsync(HttpContext, ct);
            return;
        }

        try
        {
            var operationTaskId = Route<string>("operationTaskId")!;
            var task = await mediator.Send(new AbandonOperationTaskLeaseCommand(operationTaskId, req, DateTimeOffset.UtcNow), ct);
            await Send.OkAsync(task.AsResponseData(), ct);
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

[HttpPost("/api/ops/v1/operation-tasks/{operationTaskId}/lease/heartbeat")]
[Authorize(Policy = InternalServiceAuthorizationPolicy.Name)]
public sealed class HeartbeatOperationTaskLeaseEndpoint(
    IMediator mediator,
    IOpsConnectorCredentialValidator connectorCredentialValidator,
    ILogger<HeartbeatOperationTaskLeaseEndpoint> logger) : Endpoint<HeartbeatOperationTaskLeaseRequest, ResponseData<OperationTaskResponse>>
{
    public override async Task HandleAsync(HeartbeatOperationTaskLeaseRequest req, CancellationToken ct)
    {
        if (!await OpsConnectorEndpointResults.ConnectorHostAuthorizedAsync(
                HttpContext,
                connectorCredentialValidator,
                logger,
                req.ConnectorHostId,
                req.OrganizationId,
                req.EnvironmentId,
                "ops.tasks.claim",
                ct))
        {
            await OpsConnectorEndpointResults.WriteUnauthorizedAsync(HttpContext, ct);
            return;
        }

        try
        {
            var operationTaskId = Route<string>("operationTaskId")!;
            var task = await mediator.Send(new HeartbeatOperationTaskLeaseCommand(operationTaskId, req, DateTimeOffset.UtcNow), ct);
            await Send.OkAsync(task.AsResponseData(), ct);
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

[HttpPost("/api/ops/v1/operation-results")]
[Authorize(Policy = InternalServiceAuthorizationPolicy.Name)]
public sealed class RecordOperationResultEndpoint(
    IMediator mediator,
    IOpsConnectorCredentialValidator connectorCredentialValidator,
    ILogger<RecordOperationResultEndpoint> logger) : Endpoint<OperationResult, ResponseData<OperationTaskResponse>>
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
            await Send.OkAsync(task.AsResponseData(), ct);
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
        await ResponseDataEndpointResults.WriteErrorAsync(context, StatusCodes.Status404NotFound, detail, cancellationToken);
    }

    public static async Task WriteBadRequestAsync(HttpContext context, string detail, CancellationToken cancellationToken)
    {
        await ResponseDataEndpointResults.WriteErrorAsync(context, StatusCodes.Status400BadRequest, detail, cancellationToken);
    }
}

internal static class OpsApprovalActorResolver
{
    public static string ResolveActor(HttpContext context)
    {
        var subject = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.User.FindFirstValue("sub");
        var name = context.User.Identity?.Name;
        if (!IsInternalService(subject) && !string.IsNullOrWhiteSpace(subject))
        {
            return $"user:{subject}";
        }

        if (!IsInternalService(name) && !string.IsNullOrWhiteSpace(name))
        {
            return $"user:{name}";
        }

        var forwardedActor = ReadHeader(context, "X-Actor");
        if (!string.IsNullOrWhiteSpace(forwardedActor))
        {
            return forwardedActor;
        }

        return string.IsNullOrWhiteSpace(name) ? "system:ops" : $"system:{name}";
    }

    private static bool IsInternalService(string? value)
    {
        return string.Equals(value, "internal-service", StringComparison.Ordinal);
    }

    private static string? ReadHeader(HttpContext context, string name)
    {
        return context.Request.Headers.TryGetValue(name, out var values)
            ? values.FirstOrDefault()
            : null;
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
        await ResponseDataEndpointResults.WriteErrorAsync(
            context,
            StatusCodes.Status401Unauthorized,
            "Invalid Connector Host credential.",
            cancellationToken);
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
