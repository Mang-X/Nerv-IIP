using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.AppHub.Infrastructure;
using Nerv.IIP.AppHub.Web.Application.Connectors;
using Nerv.IIP.Contracts.ConnectorProtocol;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.AppHub.Web.Endpoints.Connectors;

/// <summary>
/// Lists collection health for every connector that has reported a collection-health fact in the
/// organization/environment scope, so a read-only status wall can enumerate connectors without a
/// per-connector round trip. Disconnected/abnormal connectors (<c>stale</c>) sort first.
/// </summary>
[HttpGet("/internal/apphub/v1/connectors/collection-health")]
[Authorize(Policy = InternalServiceAuthorizationPolicy.Name)]
public sealed class ConnectorCollectionHealthListEndpoint(IServiceProvider services, TimeProvider clock)
    : EndpointWithoutRequest<ResponseData<ConnectorCollectionHealthListResponse>>
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        var organizationId = Query<string>("organizationId")!;
        var environmentId = Query<string>("environmentId")!;
        var db = services.GetService<ApplicationDbContext>();
        if (db is null)
        {
            // The in-memory state store keeps no collection-health projection; a status wall needs the
            // persisted facts, so report an empty list rather than fabricate metrics.
            var empty = new ConnectorCollectionHealthListResponse([], 0);
            await Send.OkAsync(empty.AsResponseData(), ct);
            return;
        }

        var now = clock.GetUtcNow();
        var instances = await db.ApplicationInstances
            .AsNoTracking()
            .Include(x => x.Heartbeat)
            .Include(x => x.CollectionHealth)
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .ToListAsync(ct);

        var items = instances
            .Where(x => x.CollectionHealth is not null)
            .Select(x => ConnectorCollectionHealthEvaluator.ToListItem(x, now))
            .OrderBy(x => SeverityRank(x.Status, x.StaleReason))
            .ThenBy(x => x.ConnectorName, StringComparer.Ordinal)
            .ThenBy(x => x.ConnectorId, StringComparer.Ordinal)
            .ToList();

        var response = new ConnectorCollectionHealthListResponse(items, items.Count);
        await Send.OkAsync(response.AsResponseData(), ct);
    }

    // Real disconnects (heartbeat) sort above stalled-but-online collectors (metrics), then unknown, then healthy.
    private static int SeverityRank(string status, string? staleReason) => status switch
    {
        "stale" when staleReason == ConnectorCollectionHealthEvaluator.StaleReasonHeartbeat => 0,
        "stale" => 1,
        "unknown" => 2,
        _ => 3,
    };
}
