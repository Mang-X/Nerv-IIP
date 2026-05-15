using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.AppHub.Domain;
using Nerv.IIP.Contracts.ConnectorProtocol;

namespace Nerv.IIP.AppHub.Web.Endpoints.Connectors;

[HttpPost("/api/connectors/v1/registrations")]
[AllowAnonymous]
public sealed class RegisterApplicationEndpoint(InMemoryAppHubStateStore store) : Endpoint<ApplicationRegistration>
{
    public override async Task HandleAsync(ApplicationRegistration req, CancellationToken ct)
    {
        if (!ConnectorEndpointResults.ConnectorHostAuthorized(HttpContext, req.Context.ConnectorHostId))
        {
            await ConnectorEndpointResults.WriteUnauthorizedAsync(HttpContext, ct);
            return;
        }

        await HttpContext.Response.WriteAsJsonAsync(store.Register(req), ct);
    }
}

[HttpPost("/api/connectors/v1/heartbeats")]
[AllowAnonymous]
public sealed class RecordHeartbeatEndpoint(InMemoryAppHubStateStore store) : Endpoint<ApplicationHeartbeat>
{
    public override async Task HandleAsync(ApplicationHeartbeat req, CancellationToken ct)
    {
        if (!ConnectorEndpointResults.ConnectorHostAuthorized(HttpContext, req.Context.ConnectorHostId))
        {
            await ConnectorEndpointResults.WriteUnauthorizedAsync(HttpContext, ct);
            return;
        }

        store.RecordHeartbeat(req);
        HttpContext.Response.StatusCode = StatusCodes.Status204NoContent;
        await Task.CompletedTask;
    }
}

[HttpPost("/api/connectors/v1/state-snapshots")]
[AllowAnonymous]
public sealed class RecordStateSnapshotEndpoint(InMemoryAppHubStateStore store) : Endpoint<InstanceStateSnapshot>
{
    public override async Task HandleAsync(InstanceStateSnapshot req, CancellationToken ct)
    {
        if (!ConnectorEndpointResults.ConnectorHostAuthorized(HttpContext, req.Context.ConnectorHostId))
        {
            await ConnectorEndpointResults.WriteUnauthorizedAsync(HttpContext, ct);
            return;
        }

        store.RecordStateSnapshot(req);
        HttpContext.Response.StatusCode = StatusCodes.Status204NoContent;
        await Task.CompletedTask;
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
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { title = "Unauthorized", detail = "Invalid Connector Host credential.", status = StatusCodes.Status401Unauthorized }, cancellationToken);
    }
}
