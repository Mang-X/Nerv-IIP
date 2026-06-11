using Microsoft.EntityFrameworkCore;
using Nerv.IIP.AppHub.Domain;
using Nerv.IIP.AppHub.Infrastructure;
using Nerv.IIP.Contracts.AppHubQueries;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.AppHub.Web.Application.Queries;

public record GetApplicationInstanceDetailQuery(string OrganizationId, string EnvironmentId, string InstanceKey) : IQuery<InstanceDetailResponse>;

public class GetApplicationInstanceDetailQueryHandler(IServiceProvider services)
    : IQueryHandler<GetApplicationInstanceDetailQuery, InstanceDetailResponse>
{
    public async Task<InstanceDetailResponse> Handle(GetApplicationInstanceDetailQuery request, CancellationToken cancellationToken)
    {
        var db = services.GetService<ApplicationDbContext>();
        if (db is null)
        {
            return services.GetRequiredService<IAppHubStateStore>()
                .GetInstanceDetail(request.OrganizationId, request.EnvironmentId, request.InstanceKey)
                .ToContract();
        }

        var instance = await db.ApplicationInstances
            .AsNoTracking()
            .Include(x => x.Heartbeat)
            .Include(x => x.StateHistory)
            .SingleAsync(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.InstanceKey == request.InstanceKey,
                cancellationToken);
        var application = await db.Applications.AsNoTracking().SingleAsync(x =>
            x.OrganizationId == instance.OrganizationId
            && x.EnvironmentId == instance.EnvironmentId
            && x.ApplicationKey == instance.ApplicationKey,
            cancellationToken);
        var node = await db.ManagedNodes.AsNoTracking().SingleAsync(x =>
            x.OrganizationId == instance.OrganizationId
            && x.EnvironmentId == instance.EnvironmentId
            && x.NodeKey == instance.NodeKey,
            cancellationToken);
        var state = instance.StateHistory.OrderBy(x => x.ObservedAtUtc).LastOrDefault();
        var capabilities = instance.Capabilities
            .Select(x => new CapabilitySummary(x.CapabilityCode, x.CapabilityVersion, x.Category, x.SupportedOperations))
            .ToList();

        return new InstanceDetailResponse(
            application.ApplicationKey,
            application.ApplicationName,
            instance.Version,
            node.NodeKey,
            node.NodeName,
            instance.InstanceKey,
            instance.InstanceName,
            instance.ReportedStatus,
            instance.HealthStatus,
            instance.Heartbeat?.LastHeartbeatAtUtc,
            state?.ObservedAtUtc,
            capabilities,
            instance.Metadata);
    }
}
