using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.AppHub.Domain;
using Nerv.IIP.AppHub.Web.Application.Commands;
using Nerv.IIP.Contracts.ConnectorProtocol;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.AppHub.Web.Endpoints.Connectors;

[HttpPost("/api/connectors/v1/registrations")]
[AllowAnonymous]
public sealed class RegisterApplicationEndpoint(IMediator mediator) : Endpoint<ApplicationRegistration, ResponseData<RegistrationResult>>
{
    public override async Task HandleAsync(ApplicationRegistration req, CancellationToken ct)
    {
        if (!ConnectorEndpointResults.ConnectorHostAuthorized(HttpContext, req.Context.ConnectorHostId))
        {
            await ConnectorEndpointResults.WriteUnauthorizedAsync(HttpContext, ct);
            return;
        }

        var result = await mediator.Send(new RegisterApplicationCommand(req), ct);
        await Send.OkAsync(result.AsResponseData(), ct);
    }
}

[HttpPost("/api/connectors/v1/heartbeats")]
[AllowAnonymous]
public sealed class RecordHeartbeatEndpoint(IMediator mediator) : Endpoint<ApplicationHeartbeat>
{
    public override async Task HandleAsync(ApplicationHeartbeat req, CancellationToken ct)
    {
        if (!ConnectorEndpointResults.ConnectorHostAuthorized(HttpContext, req.Context.ConnectorHostId))
        {
            await ConnectorEndpointResults.WriteUnauthorizedAsync(HttpContext, ct);
            return;
        }

        await mediator.Send(new RecordApplicationHeartbeatCommand(req), ct);
        HttpContext.Response.StatusCode = StatusCodes.Status204NoContent;
    }
}

[HttpPost("/api/connectors/v1/state-snapshots")]
[AllowAnonymous]
public sealed class RecordStateSnapshotEndpoint(IMediator mediator) : Endpoint<InstanceStateSnapshot>
{
    public override async Task HandleAsync(InstanceStateSnapshot req, CancellationToken ct)
    {
        if (!ConnectorEndpointResults.ConnectorHostAuthorized(HttpContext, req.Context.ConnectorHostId))
        {
            await ConnectorEndpointResults.WriteUnauthorizedAsync(HttpContext, ct);
            return;
        }

        await mediator.Send(new RecordInstanceStateSnapshotCommand(req), ct);
        HttpContext.Response.StatusCode = StatusCodes.Status204NoContent;
    }
}

internal static class ConnectorEndpointResults
{
    public static bool ConnectorHostAuthorized(HttpContext context, string connectorHostId)
    {
        return context.Request.Headers.TryGetValue("X-Connector-Host-Id", out var hostId)
            && context.Request.Headers.TryGetValue("X-Connector-Secret", out var secret)
            && hostId == connectorHostId
            && secret == "local-connector-secret";
    }

    public static async Task WriteUnauthorizedAsync(HttpContext context, CancellationToken cancellationToken)
    {
        await ResponseDataEndpointResults.WriteErrorAsync(
            context,
            StatusCodes.Status401Unauthorized,
            "Invalid Connector Host credential.",
            cancellationToken);
    }
}
