using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Contracts.Wms;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Erp.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Wms.WmsIntegrationEvent", ConsumerName)]
public sealed class WmsOutboundOrderCancelledIntegrationEventHandlerForCancelDeliveryProjection(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore,
    ILogger<WmsOutboundOrderCancelledIntegrationEventHandlerForCancelDeliveryProjection> logger)
    : IIntegrationEventHandler<WmsIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-erp.wms-outbound-cancelled-delivery-projection";

    private static readonly IntegrationEventConsumerOptions ConsumerOptions = new(
        ConsumerName,
        WmsIntegrationEventTypes.OutboundOrderCancelled,
        WmsIntegrationEventVersions.V1)
    {
        IgnoreUnsupportedEventTypes = true
    };

    private readonly IntegrationEventConsumerGuard<WmsIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        ConsumerOptions);

    public Task HandleAsync(WmsIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe(nameof(WmsIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(WmsIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(WmsIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        if (!string.Equals(integrationEvent.SourceService, WmsIntegrationEventSources.BusinessWms, StringComparison.OrdinalIgnoreCase))
        {
            await DeadLetterAsync(
                integrationEvent,
                "unexpected-source-service",
                $"Integration event source service '{integrationEvent.SourceService}' is not supported by consumer '{ConsumerName}'.",
                cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(integrationEvent.Payload.PublicReference))
        {
            await DeadLetterAsync(
                integrationEvent,
                "missing-payload-field",
                "WMS outbound cancellation payload must include PublicReference.",
                cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(integrationEvent.Payload.DiagnosticMessage))
        {
            await DeadLetterAsync(
                integrationEvent,
                "missing-payload-field",
                "WMS outbound cancellation payload must include cancellation reason in DiagnosticMessage.",
                cancellationToken);
            return;
        }

        var delivery = await dbContext.DeliveryOrders.Include(x => x.Lines).SingleOrDefaultAsync(x =>
            x.OrganizationId == integrationEvent.OrganizationId
            && x.EnvironmentId == integrationEvent.EnvironmentId
            && x.DeliveryOrderNo == integrationEvent.Payload.PublicReference,
            cancellationToken);
        if (delivery is null)
        {
            logger.LogDebug(
                "Ignoring WMS outbound cancellation {PublicReference} because no ERP delivery order matched org {OrganizationId} env {EnvironmentId}.",
                integrationEvent.Payload.PublicReference,
                integrationEvent.OrganizationId,
                integrationEvent.EnvironmentId);
            return;
        }

        if (await dbContext.ProcessedIntegrationEvents.AnyAsync(x =>
            x.ConsumerName == ConsumerName
            && x.IdempotencyKey == integrationEvent.IdempotencyKey,
            cancellationToken))
        {
            return;
        }

        var hasAccountReceivable = await dbContext.AccountReceivables.AnyAsync(x =>
            x.OrganizationId == integrationEvent.OrganizationId
            && x.EnvironmentId == integrationEvent.EnvironmentId
            && x.SourceDocumentNo == delivery.DeliveryOrderNo,
            cancellationToken);
        if (hasAccountReceivable)
        {
            await DeadLetterAsync(
                integrationEvent,
                "delivery-already-accrued",
                $"ERP delivery order '{delivery.DeliveryOrderNo}' already has account receivable; WMS cancellation cannot project delivery cancellation after AR accrual.",
                cancellationToken);
            return;
        }

        if (!string.Equals(delivery.Status, "released", StringComparison.Ordinal))
        {
            await DeadLetterAsync(
                integrationEvent,
                "stale-delivery-state",
                $"ERP delivery order '{delivery.DeliveryOrderNo}' in status '{delivery.Status}' cannot accept WMS cancellation facts.",
                cancellationToken);
            return;
        }

        if (!await ErpProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken))
        {
            return;
        }

        if (delivery.Cancel(integrationEvent.Payload.DiagnosticMessage, integrationEvent.OccurredAtUtc.UtcDateTime))
        {
            var order = await dbContext.SalesOrders.Include(x => x.Lines).SingleOrDefaultAsync(x =>
                x.OrganizationId == integrationEvent.OrganizationId
                && x.EnvironmentId == integrationEvent.EnvironmentId
                && x.SalesOrderNo == delivery.SalesOrderNo,
                cancellationToken);
            if (order is not null)
            {
                foreach (var line in delivery.Lines)
                {
                    order.ReleaseDelivery(line.SalesOrderLineNo, line.Quantity);
                }
            }
        }
    }

    private Task DeadLetterAsync(
        WmsIntegrationEvent integrationEvent,
        string failureCode,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        return deadLetterStore.AddAsync(
            IntegrationEventDeadLetterMessage.Create(
                ConsumerName,
                integrationEvent,
                failureCode,
                failureMessage),
            cancellationToken);
    }
}
