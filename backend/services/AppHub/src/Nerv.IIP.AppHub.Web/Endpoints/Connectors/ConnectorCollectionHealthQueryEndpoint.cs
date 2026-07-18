using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.AppHub.Infrastructure;
using Nerv.IIP.AppHub.Web.Application.Connectors;
using Nerv.IIP.Contracts.ConnectorProtocol;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.AppHub.Web.Endpoints.Connectors;

[HttpGet("/internal/apphub/v1/connectors/{connectorId}/collection-health")]
[Authorize(Policy = InternalServiceAuthorizationPolicy.Name)]
public sealed class ConnectorCollectionHealthQueryEndpoint(
    IServiceProvider services,
    TimeProvider clock,
    ConnectorCollectionHealthEvaluator evaluator)
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
        var response = evaluator.ToResponse(instance, clock.GetUtcNow());
        await Send.OkAsync(response.AsResponseData(), ct);
    }
}
