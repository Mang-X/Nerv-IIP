using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.ScheduleOperationOverrideAggregate;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Contracts.Mes;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Commands;

public sealed record ApplyWorkOrderSplitCommand(WorkOrderSplitIntegrationEvent Event) : ICommand;

public sealed class ApplyWorkOrderSplitCommandHandler(
    ApplicationDbContext dbContext,
    ISchedulingWorkbenchSourceProvider sourceProvider,
    TimeProvider timeProvider) : ICommandHandler<ApplyWorkOrderSplitCommand>
{
    public async Task Handle(ApplyWorkOrderSplitCommand request, CancellationToken cancellationToken)
    {
        var integrationEvent = request.Event;
        var invalidations = new RecordSchedulePlanInvalidationsCommandHandler(dbContext, timeProvider);
        foreach (var group in integrationEvent.Payload.Lines.GroupBy(x => x.SourceWorkOrderId))
        {
            // MES Split cancels source operations at the same fact time as this event.
            // Its clear consumer may arrive first; earlier cancellations and device clears stay revoked.
            var parentLocks = await dbContext.ScheduleOperationOverrides.Where(x =>
                x.OrganizationId == integrationEvent.OrganizationId &&
                x.EnvironmentId == integrationEvent.EnvironmentId &&
                x.WorkOrderId == group.Key &&
                (x.IsActive ||
                    (x.SourceType == ScheduleOperationOverrideSourceTypes.MesDispatch &&
                     x.ClearedReasonCode == MesManualDispatchClearReasonCodes.OperationCancelled &&
                     x.ClearedAtUtc == integrationEvent.OccurredAtUtc))).ToArrayAsync(cancellationToken);
            if (parentLocks.Length > 0)
            {
                var lines = group.OrderBy(x => x.TargetWorkOrderId, StringComparer.Ordinal).ToArray();
                var children = await sourceProvider.ResolveOrdersAsync(
                    integrationEvent.OrganizationId, integrationEvent.EnvironmentId, integrationEvent.OccurredAtUtc,
                    lines.Select(x => new SchedulingWorkbenchOrderSelection(x.TargetWorkOrderId, 0, false)).ToArray(),
                    cancellationToken);
                var byId = children.ToDictionary(x => x.Order.OrderId, StringComparer.Ordinal);
                foreach (var parentLock in parentLocks)
                {
                    var start = parentLock.StartUtc;
                    for (var index = 0; index < lines.Length; index++)
                    {
                        var line = lines[index];
                        var operation = byId[line.TargetWorkOrderId].Operations.Single(x =>
                            x.OperationSequence == parentLock.OperationSequence);
                        var end = index == lines.Length - 1 ? parentLock.EndUtc : start.AddTicks(
                            (long)((parentLock.EndUtc - parentLock.StartUtc).Ticks * line.Quantity / line.SourceQuantity));
                        dbContext.ScheduleOperationOverrides.Add(ScheduleOperationOverride.Create(
                            integrationEvent.OrganizationId, integrationEvent.EnvironmentId,
                            line.TargetWorkOrderId, operation.OperationTaskId, operation.OperationSequence,
                            parentLock.ResourceId, parentLock.WorkCenterId, start, end,
                            parentLock.LockReasonCode, parentLock.SourceType, integrationEvent.EventId,
                            integrationEvent.Actor, integrationEvent.OccurredAtUtc, timeProvider.GetUtcNow()));
                        start = end;
                    }
                    parentLock.ClearForWorkOrderSplit(integrationEvent.EventId, integrationEvent.Actor,
                        integrationEvent.OccurredAtUtc);
                }
            }

            await invalidations.Handle(new RecordSchedulePlanInvalidationsCommand(
                integrationEvent.OrganizationId, integrationEvent.EnvironmentId, integrationEvent.EventId,
                integrationEvent.EventType, integrationEvent.SourceService, integrationEvent.OccurredAtUtc,
                "work-order-split", SchedulePlanInvalidationScope.WorkOrder, group.Key, group.Key, null),
                cancellationToken);
        }
    }
}
