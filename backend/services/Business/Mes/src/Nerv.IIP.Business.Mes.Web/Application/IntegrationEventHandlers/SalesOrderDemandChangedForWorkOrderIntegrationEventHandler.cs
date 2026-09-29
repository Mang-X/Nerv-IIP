using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderDemandChangeAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Contracts.DemandPlanning;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer(TopicName, ConsumerName)]
public sealed class SalesOrderDemandChangedForWorkOrderIntegrationEventHandler(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<SalesOrderDemandChangedForWorkOrderIntegrationEvent>, ICapSubscribe
{
    public const string TopicName = "SalesOrderDemandChangedForWorkOrderIntegrationEvent";
    public const string ConsumerName = "business-mes.sales-order-demand-changed-for-work-order";

    private readonly IntegrationEventConsumerGuard<SalesOrderDemandChangedForWorkOrderIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            DemandPlanningIntegrationEventTypes.SalesOrderDemandChangedForWorkOrder,
            DemandPlanningIntegrationEventVersions.V1));

    public Task HandleAsync(
        SalesOrderDemandChangedForWorkOrderIntegrationEvent integrationEvent,
        CancellationToken cancellationToken) =>
        consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);

    [CapSubscribe(TopicName, Group = ConsumerName)]
    public Task HandleCapAsync(
        SalesOrderDemandChangedForWorkOrderIntegrationEvent integrationEvent,
        CancellationToken cancellationToken) =>
        HandleAsync(integrationEvent, cancellationToken);

    private async Task HandleValidEventAsync(
        SalesOrderDemandChangedForWorkOrderIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        var payload = integrationEvent.Payload;
        var workOrder = await dbContext.WorkOrders.SingleOrDefaultAsync(x =>
            x.OrganizationId == integrationEvent.OrganizationId &&
            x.EnvironmentId == integrationEvent.EnvironmentId &&
            x.WorkOrderIdValue == payload.WorkOrderId,
            cancellationToken);
        if (workOrder is null)
        {
            throw new InvalidOperationException(
                $"MES work order '{payload.WorkOrderId}' for accepted suggestion '{payload.SuggestionId}' is not available yet.");
        }

        var source = workOrder.SourcePlanReference;
        if (source is null ||
            source.SourceSystem != DemandPlanningSourceReferences.DemandPlanning ||
            source.SourceDocumentType != DemandPlanningSourceReferences.PlanningSuggestion ||
            source.SourceDocumentId != payload.SuggestionId)
        {
            return;
        }

        if (source.SourceDemandReferences is { } references
            ? !references.Contains(payload.DemandSourceReference, StringComparer.Ordinal)
            : !string.Equals(source.SourceDemandReference, payload.DemandSourceReference, StringComparison.Ordinal))
        {
            return;
        }

        if (!await MesProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken))
        {
            return;
        }

        var marker = await dbContext.WorkOrderDemandChanges.SingleOrDefaultAsync(x =>
            x.OrganizationId == integrationEvent.OrganizationId &&
            x.EnvironmentId == integrationEvent.EnvironmentId &&
            x.WorkOrderId == payload.WorkOrderId &&
            x.DemandSourceReference == payload.DemandSourceReference,
            cancellationToken);
        if (marker is null)
        {
            dbContext.WorkOrderDemandChanges.Add(new WorkOrderDemandChange(
                integrationEvent.OrganizationId,
                integrationEvent.EnvironmentId,
                payload.WorkOrderId,
                payload.SuggestionId,
                payload.DemandSourceReference,
                payload.SalesOrderId,
                payload.OrderVersion,
                payload.Cancelled));
        }
        else
        {
            marker.ApplyNewerVersion(payload.OrderVersion, payload.Cancelled);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
