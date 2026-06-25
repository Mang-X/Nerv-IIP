using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.DeliveryOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SalesOrderAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Commands;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Finance;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Contracts.Wms;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Erp.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Wms.WmsIntegrationEvent", ConsumerName)]
public sealed class WmsOutboundOrderCompletedIntegrationEventHandlerForCreateAccountReceivable(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore,
    ErpCodingService codingService,
    ILogger<WmsOutboundOrderCompletedIntegrationEventHandlerForCreateAccountReceivable> logger)
    : IIntegrationEventHandler<WmsIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-erp.wms-outbound-completed-ar-accrual";
    // SalesOrder has no currency snapshot yet; keep the existing ERP finance default until that source fact exists.
    private const string DefaultDeliveryAccrualCurrencyCode = "CNY";

    private static readonly IntegrationEventConsumerOptions ConsumerOptions = new(
        ConsumerName,
        WmsIntegrationEventTypes.OutboundOrderCompleted,
        WmsIntegrationEventVersions.V1);

    private readonly IntegrationEventConsumerGuard<WmsIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        ConsumerOptions);

    public Task HandleAsync(
        WmsIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        return consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe("Nerv.IIP.Contracts.Wms.WmsIntegrationEvent", Group = ConsumerName)]
    public Task HandleCapAsync(
        WmsIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(
        WmsIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
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
                "WMS outbound completion payload must include PublicReference.",
                cancellationToken);
            return;
        }

        var delivery = await dbContext.DeliveryOrders
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x =>
                x.OrganizationId == integrationEvent.OrganizationId
                && x.EnvironmentId == integrationEvent.EnvironmentId
                && x.DeliveryOrderNo == integrationEvent.Payload.PublicReference,
                cancellationToken);
        if (delivery is null)
        {
            logger.LogDebug(
                "Ignoring WMS outbound completion {PublicReference} because no ERP delivery order matched org {OrganizationId} env {EnvironmentId}.",
                integrationEvent.Payload.PublicReference,
                integrationEvent.OrganizationId,
                integrationEvent.EnvironmentId);
            return;
        }

        // WMS completion is currently order-level; LineReference carries the first completed line only as a sanity check.
        if (!string.IsNullOrWhiteSpace(integrationEvent.Payload.LineReference)
            && !delivery.Lines.Any(x => string.Equals(x.SalesOrderLineNo, integrationEvent.Payload.LineReference, StringComparison.Ordinal)))
        {
            await DeadLetterAsync(
                integrationEvent,
                "missing-source-facts",
                $"Delivery order '{delivery.DeliveryOrderNo}' does not contain completed WMS line '{integrationEvent.Payload.LineReference}'.",
                cancellationToken);
            return;
        }

        var salesOrder = await dbContext.SalesOrders
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x =>
                x.OrganizationId == delivery.OrganizationId
                && x.EnvironmentId == delivery.EnvironmentId
                && x.SalesOrderNo == delivery.SalesOrderNo,
                cancellationToken);
        if (salesOrder is null)
        {
            await DeadLetterAsync(
                integrationEvent,
                "missing-source-facts",
                $"Sales order '{delivery.SalesOrderNo}' was not found for delivery order '{delivery.DeliveryOrderNo}'.",
                cancellationToken);
            return;
        }

        var decision = TryCalculateDeliveryAmount(delivery, salesOrder, out var amount, out var failureCode, out var failureMessage);
        if (decision == DeliveryAccrualDecision.Failed)
        {
            await DeadLetterAsync(
                integrationEvent,
                failureCode,
                failureMessage,
                cancellationToken);
            return;
        }

        if (!await ErpProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken))
        {
            return;
        }

        if (await dbContext.AccountReceivables.AnyAsync(x =>
            x.OrganizationId == delivery.OrganizationId
            && x.EnvironmentId == delivery.EnvironmentId
            && x.SourceDocumentNo == delivery.DeliveryOrderNo,
            cancellationToken))
        {
            return;
        }

        var lineSignature = BuildLineSignature(delivery);
        await new CreateAccountReceivableCommandHandler(dbContext, codingService).Handle(
            new CreateAccountReceivableCommand(
                delivery.OrganizationId,
                delivery.EnvironmentId,
                null,
                delivery.DeliveryOrderNo,
                delivery.CustomerCode,
                amount,
                DefaultDeliveryAccrualCurrencyCode,
                DateOnly.FromDateTime(integrationEvent.OccurredAtUtc.UtcDateTime),
                null,
                "DELIVERY-AR",
                $"{ConsumerName}:{delivery.OrganizationId}:{delivery.EnvironmentId}:{delivery.DeliveryOrderNo}:{lineSignature}:{integrationEvent.IdempotencyKey}"),
            cancellationToken);
    }

    private static DeliveryAccrualDecision TryCalculateDeliveryAmount(
        DeliveryOrder delivery,
        SalesOrder salesOrder,
        out decimal amount,
        out string failureCode,
        out string failureMessage)
    {
        amount = 0m;
        failureCode = string.Empty;
        failureMessage = string.Empty;
        var salesLines = salesOrder.Lines.ToDictionary(x => x.LineNo, StringComparer.Ordinal);
        foreach (var deliveryLine in delivery.Lines)
        {
            if (!salesLines.TryGetValue(deliveryLine.SalesOrderLineNo, out var salesLine))
            {
                failureCode = "missing-source-facts";
                failureMessage = $"Sales order line '{deliveryLine.SalesOrderLineNo}' was not found for delivery order '{delivery.DeliveryOrderNo}'.";
                return DeliveryAccrualDecision.Failed;
            }

            amount += deliveryLine.Quantity * salesLine.UnitPrice;
        }

        if (amount <= 0m)
        {
            failureCode = "non-positive-accrual-amount";
            failureMessage = $"Delivery order '{delivery.DeliveryOrderNo}' does not have a positive AR accrual amount.";
            return DeliveryAccrualDecision.Failed;
        }

        return DeliveryAccrualDecision.Accrue;
    }

    private static string BuildLineSignature(DeliveryOrder delivery)
    {
        return string.Join(
            "|",
            delivery.Lines
                .OrderBy(x => x.SalesOrderLineNo, StringComparer.Ordinal)
                .Select(x => $"{x.SalesOrderLineNo}:{x.Quantity}"));
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

    private enum DeliveryAccrualDecision
    {
        Accrue,
        Failed,
    }
}
