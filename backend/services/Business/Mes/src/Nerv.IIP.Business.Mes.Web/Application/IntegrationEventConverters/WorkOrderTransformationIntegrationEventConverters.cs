using System.Collections.Immutable;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderTransformationAggregate;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;
using Nerv.IIP.Contracts.Mes;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Mes.Web.Application.IntegrationEventConverters;

public sealed class WorkOrderSplitIntegrationEventConverter(IMesIntegrationEventContextAccessor contextAccessor)
    : IIntegrationEventConverter<WorkOrderSplitDomainEvent, WorkOrderSplitIntegrationEvent>
{
    public WorkOrderSplitIntegrationEvent Convert(WorkOrderSplitDomainEvent domainEvent)
    {
        var fact = domainEvent.Transformation;
        var context = contextAccessor.GetContext();
        return new WorkOrderSplitIntegrationEvent(
            $"evt-{Guid.CreateVersion7():N}", MesIntegrationEventTypes.WorkOrderSplit,
            MesIntegrationEventVersions.V1, fact.OccurredAtUtc, MesIntegrationEventSources.BusinessMes,
            context.CorrelationId, context.CausationId, fact.OrganizationId, fact.EnvironmentId, fact.ActorId,
            EventIds.Idempotency("work-order-split", fact.OrganizationId, fact.EnvironmentId, fact.Id.Id.ToString("N")),
            WorkOrderTransformationIntegrationEventPayload.From(fact));
    }
}

public sealed class WorkOrderMergedIntegrationEventConverter(IMesIntegrationEventContextAccessor contextAccessor)
    : IIntegrationEventConverter<WorkOrderMergedDomainEvent, WorkOrderMergedIntegrationEvent>
{
    public WorkOrderMergedIntegrationEvent Convert(WorkOrderMergedDomainEvent domainEvent)
    {
        var fact = domainEvent.Transformation;
        var context = contextAccessor.GetContext();
        return new WorkOrderMergedIntegrationEvent(
            $"evt-{Guid.CreateVersion7():N}", MesIntegrationEventTypes.WorkOrderMerged,
            MesIntegrationEventVersions.V1, fact.OccurredAtUtc, MesIntegrationEventSources.BusinessMes,
            context.CorrelationId, context.CausationId, fact.OrganizationId, fact.EnvironmentId, fact.ActorId,
            EventIds.Idempotency("work-order-merged", fact.OrganizationId, fact.EnvironmentId, fact.Id.Id.ToString("N")),
            WorkOrderTransformationIntegrationEventPayload.From(fact));
    }
}

internal static class WorkOrderTransformationIntegrationEventPayload
{
    public static WorkOrderTransformationPayload From(WorkOrderTransformation fact) =>
        new(fact.Id.Id, fact.Reason, fact.Lines.Select(line => new WorkOrderTransformationLinePayload(
            line.SourceWorkOrderId, line.TargetWorkOrderId, line.Quantity,
            line.SourceQuantity, line.TargetQuantity, line.UomCode,
            line.SourceStatus, line.TargetStatus, line.SourceVersion, line.TargetVersion)).ToImmutableArray());
}
