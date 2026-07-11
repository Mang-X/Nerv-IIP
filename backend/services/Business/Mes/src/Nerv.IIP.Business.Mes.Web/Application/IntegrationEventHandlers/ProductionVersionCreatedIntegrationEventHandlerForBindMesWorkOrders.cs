using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Infrastructure.IntegrationEvents;
using Nerv.IIP.Contracts.ProductEngineering;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer(TopicName, ConsumerName)]
public sealed class ProductionVersionCreatedIntegrationEventHandlerForBindMesWorkOrders(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<ProductionVersionCreatedIntegrationEvent>, ICapSubscribe
{
    public const string TopicName = ProductionVersionCreatedIntegrationEventTopic.TopicName;
    public const string ConsumerName = "business-mes.product-engineering-production-version-created";

    private readonly IntegrationEventConsumerGuard<ProductionVersionCreatedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            ProductEngineeringIntegrationEventTypes.ProductionVersionCreated,
            ProductEngineeringIntegrationEventVersions.V1));

    public async Task HandleAsync(
        ProductionVersionCreatedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        await consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe(TopicName, Group = ConsumerName)]
    public Task HandleCapAsync(
        ProductionVersionCreatedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(
        ProductionVersionCreatedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (!await MesProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken))
        {
            return;
        }

        var payload = integrationEvent.Payload;
        var workOrders = await dbContext.WorkOrders
            .Where(x =>
                x.OrganizationId == integrationEvent.OrganizationId &&
                x.EnvironmentId == integrationEvent.EnvironmentId &&
                x.SkuId == payload.SkuCode &&
                x.ProductionVersionId == null &&
                x.Status == Domain.AggregatesModel.WorkOrderAggregate.WorkOrder.CreatedStatus)
            .ToListAsync(cancellationToken);
        foreach (var workOrder in workOrders)
        {
            workOrder.BindProductionVersion(payload.ProductionVersionId);
        }
    }
}

public static class ProductionVersionCreatedIntegrationEventTopic
{
    public const string TopicName = "ProductionVersionCreatedIntegrationEvent";
}
