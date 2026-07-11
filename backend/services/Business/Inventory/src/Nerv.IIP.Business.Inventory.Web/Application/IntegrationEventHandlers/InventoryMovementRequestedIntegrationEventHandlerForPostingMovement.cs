using DotNetCore.CAP;
using Microsoft.Extensions.Logging;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockReservationAggregate;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockMovements;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockStatusTransfers;
using Nerv.IIP.Business.Inventory.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Inventory.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Inventory.InventoryMovementRequestedIntegrationEvent", ConsumerName)]
public sealed class InventoryMovementRequestedIntegrationEventHandlerForPostingMovement(
    ILogger<InventoryMovementRequestedIntegrationEventHandlerForPostingMovement> logger,
    ISender sender,
    IIntegrationEventDeadLetterStore deadLetterStore,
    IIntegrationEventPublisher integrationEventPublisher)
    : IIntegrationEventHandler<InventoryMovementRequestedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-inventory.movement-requested";

    private readonly IntegrationEventConsumerGuard<InventoryMovementRequestedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            InventoryIntegrationEventTypes.InventoryMovementRequested,
            InventoryIntegrationEventVersions.V1));

    public async Task HandleAsync(InventoryMovementRequestedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe(nameof(InventoryMovementRequestedIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(InventoryMovementRequestedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(InventoryMovementRequestedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var payload = integrationEvent.Payload;
        try
        {
            if (string.Equals(payload.MovementType, InventoryMovementRequestTypes.StatusTransfer, StringComparison.OrdinalIgnoreCase))
            {
                await SendStatusTransferAsync(integrationEvent, cancellationToken);
                return;
            }

            await sender.Send(
                new PostStockMovementCommand(
                    integrationEvent.OrganizationId,
                    integrationEvent.EnvironmentId,
                    payload.MovementType,
                    payload.SourceService,
                    payload.SourceDocumentId,
                    payload.SourceDocumentLineId,
                    payload.IdempotencyKey,
                    payload.SkuCode,
                    payload.UomCode,
                    payload.SiteCode,
                    payload.LocationCode,
                    payload.LotNo,
                    payload.SerialNo,
                    payload.QualityStatus,
                    payload.OwnerType,
                    payload.OwnerId,
                    payload.Quantity,
                    payload.UnitCost,
                    ReservationId: ParseReservationId(payload.InventoryReservationId),
                    ProductionDate: payload.ProductionDate,
                    ExpiryDate: payload.ExpiryDate,
                    ShelfLifeDays: payload.ShelfLifeDays),
                cancellationToken);
        }
        catch (InventoryPostingRejectedException ex)
        {
            logger.LogWarning(
                ex,
                "Inventory movement request was rejected. SourceService={SourceService}, SourceDocumentId={SourceDocumentId}, IdempotencyKey={IdempotencyKey}, MovementType={MovementType}, QualityStatus={QualityStatus}, FailureCode={FailureCode}",
                payload.SourceService,
                payload.SourceDocumentId,
                payload.IdempotencyKey,
                payload.MovementType,
                payload.QualityStatus,
                ex.FailureCode);
            await PublishPostingFailedAsync(integrationEvent, ex.FailureCode, ex.FailureMessage, cancellationToken);
        }
        catch (KnownException ex)
        {
            logger.LogWarning(
                ex,
                "Inventory movement request was rejected. SourceService={SourceService}, SourceDocumentId={SourceDocumentId}, IdempotencyKey={IdempotencyKey}, MovementType={MovementType}, QualityStatus={QualityStatus}",
                payload.SourceService,
                payload.SourceDocumentId,
                payload.IdempotencyKey,
                payload.MovementType,
                payload.QualityStatus);
            await PublishPostingFailedAsync(integrationEvent, InventoryPostingFailureCodes.PostingRejected, ex.Message, cancellationToken);
        }
    }

    private Task SendStatusTransferAsync(InventoryMovementRequestedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var payload = integrationEvent.Payload;
        if (string.IsNullOrWhiteSpace(payload.TargetQualityStatus))
        {
            throw new InventoryPostingRejectedException(
                InventoryPostingFailureCodes.PostingRejected,
                "Inventory status-transfer movement request requires a target quality status.");
        }

        return sender.Send(
            new PostStockStatusTransferCommand(
                integrationEvent.OrganizationId,
                integrationEvent.EnvironmentId,
                payload.QualityStatus,
                payload.TargetQualityStatus,
                payload.SourceService,
                payload.SourceDocumentId,
                payload.SourceDocumentLineId,
                payload.IdempotencyKey,
                payload.SkuCode,
                payload.UomCode,
                payload.SiteCode,
                payload.LocationCode,
                payload.LotNo,
                payload.SerialNo,
                payload.OwnerType,
                payload.OwnerId,
                Math.Abs(payload.Quantity),
                payload.ProductionDate,
                payload.ExpiryDate),
            cancellationToken);
    }

    private Task PublishPostingFailedAsync(
        InventoryMovementRequestedIntegrationEvent integrationEvent,
        string failureCode,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        var payload = integrationEvent.Payload;
        var failedAtUtc = DateTimeOffset.UtcNow;
        var failedEvent = new StockMovementPostingFailedIntegrationEvent(
            $"evt-{Guid.CreateVersion7():N}",
            InventoryIntegrationEventTypes.StockMovementPostingFailed,
            InventoryIntegrationEventVersions.V1,
            failedAtUtc,
            InventoryIntegrationEventSources.BusinessInventory,
            integrationEvent.CorrelationId,
            integrationEvent.EventId,
            integrationEvent.OrganizationId,
            integrationEvent.EnvironmentId,
            "system:business-inventory",
            EventIds.Idempotency(
                "stock-movement-posting-failed",
                integrationEvent.OrganizationId,
                integrationEvent.EnvironmentId,
                payload.SourceService,
                payload.SourceDocumentId,
                payload.IdempotencyKey),
            new StockMovementPostingFailedPayload(
                payload.MovementType,
                payload.SourceService,
                payload.SourceDocumentId,
                payload.SourceDocumentLineId,
                payload.IdempotencyKey,
                payload.SkuCode,
                payload.UomCode,
                payload.SiteCode,
                payload.LocationCode,
                payload.LotNo,
                payload.SerialNo,
                payload.QualityStatus,
                payload.OwnerType,
                payload.OwnerId,
                payload.Quantity,
                failureCode,
                failureMessage,
                failedAtUtc,
                payload.ProductionDate,
                payload.ExpiryDate));
        return integrationEventPublisher.PublishAsync(failedEvent, cancellationToken);
    }

    private static StockReservationId? ParseReservationId(string? reservationId)
    {
        if (string.IsNullOrWhiteSpace(reservationId))
        {
            return null;
        }

        if (Guid.TryParse(reservationId, out var parsed))
        {
            return new StockReservationId(parsed);
        }

        throw new InventoryPostingRejectedException(
            InventoryPostingFailureCodes.InvalidReservationId,
            "Inventory movement request carried an invalid stock reservation id.");
    }
}
