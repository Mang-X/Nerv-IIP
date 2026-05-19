using Microsoft.EntityFrameworkCore;
using Nerv.IIP.AppHub.Domain;
using Nerv.IIP.AppHub.Domain.AggregatesModel.ApplicationAggregate;
using Nerv.IIP.AppHub.Domain.AggregatesModel.ApplicationInstanceAggregate;
using Nerv.IIP.AppHub.Domain.AggregatesModel.ManagedNodeAggregate;
using Nerv.IIP.AppHub.Infrastructure;
using Nerv.IIP.Contracts.AppHubQueries;
using NetCorePal.Extensions.Primitives;
using AppHubApplication = Nerv.IIP.AppHub.Domain.AggregatesModel.ApplicationAggregate.Application;

namespace Nerv.IIP.AppHub.Web.Application.Queries;

public record ListApplicationInstancesQuery(InstanceListQuery Query) : IQuery<InstanceListResponse>;

public class ListApplicationInstancesQueryHandler(IServiceProvider services)
    : IQueryHandler<ListApplicationInstancesQuery, InstanceListResponse>
{
    public async Task<InstanceListResponse> Handle(ListApplicationInstancesQuery request, CancellationToken cancellationToken)
    {
        var db = services.GetService<ApplicationDbContext>();
        if (db is null)
        {
            return services.GetRequiredService<IAppHubStateStore>().QueryInstances(request.Query);
        }

        var query = request.Query;
        var instances = await db.ApplicationInstances
            .AsNoTracking()
            .Include(x => x.Heartbeat)
            .Include(x => x.StateHistory)
            .Where(x => x.OrganizationId == query.OrganizationId && x.EnvironmentId == query.EnvironmentId)
            .ToListAsync(cancellationToken);
        var applications = await db.Applications
            .AsNoTracking()
            .Where(x => x.OrganizationId == query.OrganizationId && x.EnvironmentId == query.EnvironmentId)
            .ToListAsync(cancellationToken);
        var nodes = await db.ManagedNodes
            .AsNoTracking()
            .Where(x => x.OrganizationId == query.OrganizationId && x.EnvironmentId == query.EnvironmentId)
            .ToListAsync(cancellationToken);

        var rows = instances
            .Select(instance => ToListProjection(instance, applications, nodes))
            .Where(item =>
                string.IsNullOrWhiteSpace(query.FilterSearch)
                || item.ApplicationName.Contains(query.FilterSearch, StringComparison.OrdinalIgnoreCase)
                || item.InstanceName.Contains(query.FilterSearch, StringComparison.OrdinalIgnoreCase))
            .ApplyInstanceListSort(query)
            .ToList();

        var pageIndex = Math.Max(query.PageIndex, 1);
        var pageSize = Math.Max(query.PageSize, 1);
        var items = rows.Skip((pageIndex - 1) * pageSize).Take(pageSize).ToList();
        return new InstanceListResponse(query.PageIndex, query.PageSize, rows.Count, items);
    }

    private static InstanceListItem ToListProjection(ApplicationInstance instance, List<AppHubApplication> applications, List<ManagedNode> nodes)
    {
        var application = applications.Single(x => x.ApplicationKey == instance.ApplicationKey);
        var node = nodes.Single(x => x.NodeKey == instance.NodeKey);
        var state = instance.StateHistory.OrderBy(x => x.ObservedAtUtc).LastOrDefault();
        return new InstanceListItem(
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
            state?.ObservedAtUtc);
    }
}
