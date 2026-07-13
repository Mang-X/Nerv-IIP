using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.AppHub.Infrastructure;
using Nerv.IIP.Contracts.ConnectorProtocol;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.AppHub.Web.Endpoints.Connectors;

[HttpGet("/internal/apphub/v1/connectors/{connectorId}/collection-health")]
[Authorize(Policy = InternalServiceAuthorizationPolicy.Name)]
public sealed class ConnectorCollectionHealthQueryEndpoint(IServiceProvider services, TimeProvider clock)
    : EndpointWithoutRequest<ResponseData<ConnectorCollectionHealthResponse>>
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        var connectorId = Route<string>("connectorId")!;
        var organizationId = Query<string>("organizationId")!;
        var environmentId = Query<string>("environmentId")!;
        var db = services.GetService<ApplicationDbContext>();
        if (db is null)
        {
            Nerv.IIP.AppHub.Domain.InstanceDetailFact detail;
            try
            {
                detail = services.GetRequiredService<Nerv.IIP.AppHub.Domain.IAppHubStateStore>().GetInstanceDetail(organizationId, environmentId, connectorId);
            }
            catch (InvalidOperationException)
            {
                var missing = new ConnectorCollectionHealthResponse(connectorId, "unknown", null, null, null, null, null, null, null);
                await Send.OkAsync(missing.AsResponseData(), ct);
                return;
            }
            var unknown = new ConnectorCollectionHealthResponse(connectorId, "unknown", detail.LastHeartbeatAtUtc, null, null, null, null, null, null);
            await Send.OkAsync(unknown.AsResponseData(), ct);
            return;
        }

        var instance = await db.ApplicationInstances
            .AsNoTracking()
            .Include(x => x.Heartbeat)
            .Include(x => x.CollectionHealth)
            .SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.InstanceKey == connectorId, ct);
        if (instance is null)
        {
            var missing = new ConnectorCollectionHealthResponse(connectorId, "unknown", null, null, null, null, null, null, null);
            await Send.OkAsync(missing.AsResponseData(), ct);
            return;
        }
        var health = instance.CollectionHealth;
        var now = clock.GetUtcNow();
        var heartbeatStale = instance.Heartbeat is null
            || !instance.Heartbeat.Reachable
            || instance.Heartbeat.LastHeartbeatAtUtc.AddMinutes(2) <= now;
        var metricsStale = health is not null && health.ReportedAtUtc.AddMinutes(2) <= now;
        var status = health is null ? "unknown" : heartbeatStale || metricsStale ? "stale" : "current";
        var response = new ConnectorCollectionHealthResponse(
            connectorId,
            status,
            instance.Heartbeat?.LastHeartbeatAtUtc,
            health?.ReportedAtUtc,
            health?.LastSampleAtUtc,
            health?.ReceivedCount,
            health?.DroppedCount,
            health?.ErrorCount,
            health?.SourceSystem);
        await Send.OkAsync(response.AsResponseData(), ct);
    }
}
