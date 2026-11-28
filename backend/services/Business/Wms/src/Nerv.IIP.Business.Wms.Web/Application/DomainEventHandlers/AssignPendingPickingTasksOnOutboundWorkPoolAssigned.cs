using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;
using Nerv.IIP.Business.Wms.Domain.DomainEvents;
using Nerv.IIP.Business.Wms.Infrastructure;

namespace Nerv.IIP.Business.Wms.Web.Application.DomainEventHandlers;

public sealed class AssignPendingPickingTasksOnOutboundWorkPoolAssigned(ApplicationDbContext dbContext)
    : INotificationHandler<OutboundOrderWorkPoolAssignedDomainEvent>
{
    public async Task Handle(
        OutboundOrderWorkPoolAssignedDomainEvent notification,
        CancellationToken cancellationToken)
    {
        var tasks = await dbContext.WarehouseTasks
            .Where(task => task.OrganizationId == notification.OrganizationId
                && task.EnvironmentId == notification.EnvironmentId
                && task.SiteCode == notification.SiteCode
                && task.SourceOrderNo == notification.OutboundOrderNo
                && task.TaskType == WarehouseTaskType.Picking
                && task.Status == WarehouseTaskStatus.Open
                && task.AssignedPoolCode == null
                && task.AssignedOperatorUserId == null)
            .ToListAsync(cancellationToken);

        foreach (var task in tasks)
        {
            task.InheritPickingAssignment(notification.PoolCode, notification.OperatorPrincipalId);
        }

        // NetCorePal dispatches domain events after its save. Persist the inherited assignment
        // before the enclosing command commits the same transaction.
        if (tasks.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
