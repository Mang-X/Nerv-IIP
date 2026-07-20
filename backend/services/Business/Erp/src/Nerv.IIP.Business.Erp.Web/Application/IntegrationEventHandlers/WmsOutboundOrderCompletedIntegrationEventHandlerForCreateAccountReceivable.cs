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
    private const int MaxConcurrencyAttempts = 3;
    // SalesOrder has no currency snapshot yet; keep the existing ERP finance default until that source fact exists.
    private const string DefaultDeliveryAccrualCurrencyCode = "CNY";

    private static readonly IntegrationEventConsumerOptions ConsumerOptions = new(
        ConsumerName,
        WmsIntegrationEventTypes.OutboundOrderCompleted,
        WmsIntegrationEventVersions.V1)
    {
        IgnoreUnsupportedEventTypes = true
    };

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

    [CapSubscribe(nameof(WmsIntegrationEvent), Group = ConsumerName)]
    public async Task HandleCapAsync(
        WmsIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await HandleAsync(integrationEvent, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateConcurrencyException exception)
                when (attempt < MaxConcurrencyAttempts)
            {
                logger.LogInformation(
                    exception,
                    "Retrying WMS outbound completion {IdempotencyKey} after concurrent ERP delivery projection update (attempt {Attempt}/{MaxAttempts}).",
                    integrationEvent.IdempotencyKey,
                    attempt + 1,
                    MaxConcurrencyAttempts);
                dbContext.ChangeTracker.Clear();
            }
        }
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

        if (await dbContext.ProcessedIntegrationEvents.AnyAsync(x =>
            x.ConsumerName == ConsumerName
            && x.IdempotencyKey == integrationEvent.IdempotencyKey,
            cancellationToken))
        {
            return;
        }

        if (delivery.Status is "cancelled" or "completed")
        {
            await DeadLetterAsync(
                integrationEvent,
                "stale-delivery-state",
                $"ERP delivery order '{delivery.DeliveryOrderNo}' in status '{delivery.Status}' cannot accept WMS shipment completion facts.",
                cancellationToken);
            return;
        }

        if (!TryBuildShipment(
            delivery,
            integrationEvent,
            out var shipmentLines,
            out var shipmentFailureCode,
            out var shipmentFailureMessage))
        {
            await DeadLetterAsync(
                integrationEvent,
                shipmentFailureCode,
                shipmentFailureMessage,
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

        var becameCompleted = delivery.ApplyShipment(shipmentLines, integrationEvent.OccurredAtUtc.UtcDateTime);
        if (!becameCompleted)
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
        var accrualIdempotencyKey = StableIdempotencyKey(
            "delivery-ar",
            delivery.OrganizationId,
            delivery.EnvironmentId,
            delivery.DeliveryOrderNo,
            lineSignature,
            integrationEvent.IdempotencyKey);
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
                accrualIdempotencyKey),
            cancellationToken);
    }

    private static bool TryBuildShipment(
        DeliveryOrder delivery,
        WmsIntegrationEvent integrationEvent,
        out DeliveryOrderShipmentLine[] shipmentLines,
        out string failureCode,
        out string failureMessage)
    {
        shipmentLines = [];
        failureCode = string.Empty;
        failureMessage = string.Empty;
        var payloadLines = integrationEvent.Payload.Lines?.ToArray();
        if (payloadLines is null || payloadLines.Length == 0)
        {
            failureCode = "invalid-shipment-lines";
            failureMessage = "WMS outbound completion payload must include all emitted shipment lines.";
            return false;
        }

        var invalidReference = payloadLines.FirstOrDefault(x => string.IsNullOrWhiteSpace(x.LineReference));
        if (invalidReference is not null)
        {
            failureCode = "invalid-shipment-lines";
            failureMessage = "WMS outbound completion payload contains a blank line reference.";
            return false;
        }

        var duplicateLine = payloadLines
            .GroupBy(x => x.LineReference, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateLine is not null)
        {
            failureCode = "invalid-shipment-lines";
            failureMessage = $"WMS outbound completion payload contains duplicate line '{duplicateLine.Key}'.";
            return false;
        }

        var deliveryLines = delivery.Lines.ToDictionary(x => x.SalesOrderLineNo, StringComparer.Ordinal);
        foreach (var payloadLine in payloadLines)
        {
            if (!deliveryLines.TryGetValue(payloadLine.LineReference, out var deliveryLine))
            {
                failureCode = "missing-source-facts";
                failureMessage = $"Delivery order '{delivery.DeliveryOrderNo}' does not contain completed WMS line '{payloadLine.LineReference}'.";
                return false;
            }

            if (!string.Equals(deliveryLine.SkuCode, payloadLine.SkuCode, StringComparison.Ordinal)
                || !string.Equals(deliveryLine.UomCode, payloadLine.UomCode, StringComparison.OrdinalIgnoreCase))
            {
                failureCode = "missing-source-facts";
                failureMessage = $"WMS outbound completion line '{payloadLine.LineReference}' does not match ERP delivery SKU/UOM facts.";
                return false;
            }

            if (payloadLine.Quantity < 0m || payloadLine.Quantity > deliveryLine.Quantity - deliveryLine.ShippedQuantity)
            {
                failureCode = "invalid-shipment-lines";
                failureMessage = $"WMS outbound completion quantity for line '{payloadLine.LineReference}' exceeds the remaining ERP delivery quantity.";
                return false;
            }
        }

        if (payloadLines.All(x => x.Quantity == 0m))
        {
            failureCode = "invalid-shipment-lines";
            failureMessage = "WMS outbound completion payload must contain at least one positive shipment quantity.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(integrationEvent.Payload.LineReference)
            && !deliveryLines.ContainsKey(integrationEvent.Payload.LineReference))
        {
            failureCode = "missing-source-facts";
            failureMessage = $"Delivery order '{delivery.DeliveryOrderNo}' does not contain completed WMS line '{integrationEvent.Payload.LineReference}'.";
            return false;
        }

        shipmentLines = payloadLines
            .Select(x => new DeliveryOrderShipmentLine(x.LineReference, x.Quantity))
            .ToArray();
        return true;
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

    private static string StableIdempotencyKey(string prefix, params object?[] parts)
    {
        var raw = ErpCodingService.Fingerprint(parts);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw)))[..32].ToLowerInvariant();
        return $"{prefix}:{hash}";
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
