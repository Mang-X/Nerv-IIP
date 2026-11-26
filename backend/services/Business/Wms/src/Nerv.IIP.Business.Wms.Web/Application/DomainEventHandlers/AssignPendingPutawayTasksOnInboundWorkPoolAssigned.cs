using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;
using Nerv.IIP.Business.Wms.Domain.DomainEvents;
using Nerv.IIP.Business.Wms.Infrastructure;

namespace Nerv.IIP.Business.Wms.Web.Application.DomainEventHandlers;

public sealed class AssignPendingPutawayTasksOnInboundWorkPoolAssigned(ApplicationDbContext dbContext)
    : INotificationHandler<InboundOrderWorkPoolAssignedDomainEvent>
{
    public async Task Handle(
        InboundOrderWorkPoolAssignedDomainEvent notification,
        CancellationToken cancellationToken)
    {
        var tasks = await dbContext.WarehouseTasks
            .Where(task => task.OrganizationId == notification.OrganizationId
                && task.EnvironmentId == notification.EnvironmentId
                && task.SiteCode == notification.SiteCode
                && task.SourceOrderNo == notification.InboundOrderNo
                && task.TaskType == WarehouseTaskType.Putaway
                && task.Status == WarehouseTaskStatus.Open
                && task.AssignedPoolCode == null
                && task.AssignedOperatorUserId == null)
            .ToListAsync(cancellationToken);

        foreach (var task in tasks)
        {
            task.InheritPutawayAssignment(notification.PoolCode, notification.OperatorPrincipalId);
        }
    }
}
