using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenancePlanAggregate;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;

namespace Nerv.IIP.Business.Maintenance.Web.Application.Queries;

public sealed record ListMaintenanceWorkOrdersQuery(string? OrganizationId, string? EnvironmentId) : IQuery<IReadOnlyCollection<MaintenanceWorkOrderListItem>>;

public sealed record MaintenanceWorkOrderListItem(MaintenanceWorkOrderId WorkOrderId, string DeviceAssetId, string Priority, string Status, string? SourceAlarmId, DateTimeOffset OpenedAtUtc);

public sealed class ListMaintenanceWorkOrdersQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListMaintenanceWorkOrdersQuery, IReadOnlyCollection<MaintenanceWorkOrderListItem>>
{
    public async Task<IReadOnlyCollection<MaintenanceWorkOrderListItem>> Handle(ListMaintenanceWorkOrdersQuery request, CancellationToken cancellationToken)
    {
        return await dbContext.MaintenanceWorkOrders
            .Where(x => request.OrganizationId == null || x.OrganizationId == request.OrganizationId)
            .Where(x => request.EnvironmentId == null || x.EnvironmentId == request.EnvironmentId)
            .OrderByDescending(x => x.OpenedAtUtc)
            .Select(x => new MaintenanceWorkOrderListItem(x.Id, x.DeviceAssetId, x.Priority, x.Status.ToString(), x.SourceAlarmId, x.OpenedAtUtc))
            .Take(100)
            .ToArrayAsync(cancellationToken);
    }
}

public sealed record ListMaintenancePlansQuery(string? OrganizationId, string? EnvironmentId) : IQuery<IReadOnlyCollection<MaintenancePlanListItem>>;

public sealed record MaintenancePlanListItem(MaintenancePlanId PlanId, string DeviceAssetId, string PlanCode, string Interval, DateOnly StartsOn);

public sealed class ListMaintenancePlansQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListMaintenancePlansQuery, IReadOnlyCollection<MaintenancePlanListItem>>
{
    public async Task<IReadOnlyCollection<MaintenancePlanListItem>> Handle(ListMaintenancePlansQuery request, CancellationToken cancellationToken)
    {
        return await dbContext.MaintenancePlans
            .Where(x => request.OrganizationId == null || x.OrganizationId == request.OrganizationId)
            .Where(x => request.EnvironmentId == null || x.EnvironmentId == request.EnvironmentId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new MaintenancePlanListItem(x.Id, x.DeviceAssetId, x.PlanCode, x.Interval, x.StartsOn))
            .Take(100)
            .ToArrayAsync(cancellationToken);
    }
}
