using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockCountAdjustmentAggregate;
using Nerv.IIP.Business.Inventory.Infrastructure;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockStatusTransfers;
using Nerv.IIP.Business.Inventory.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Approval;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Inventory.Web;

public static class InventoryDeadLetterReplayServiceCollectionExtensions
{
    public static IServiceCollection AddInventoryDeadLetterReplay(this IServiceCollection services)
    {
        services.AddObservedIntegrationEventDeadLetterStore();
        services.AddIntegrationEventDeadLetterReplay<InventoryMovementRequestedIntegrationEvent, InventoryMovementReplayConsumer>(
            InventoryMovementRequestedIntegrationEventHandlerForPostingMovement.ConsumerName,
            (consumer, integrationEvent, token) => consumer.HandleAsync(integrationEvent, token),
            async (provider, integrationEvent, token) =>
            {
                var payload = integrationEvent.Payload;
                var movements = provider.GetRequiredService<ApplicationDbContext>().StockMovements.AsNoTracking().Where(x =>
                    x.OrganizationId == integrationEvent.OrganizationId && x.EnvironmentId == integrationEvent.EnvironmentId
                    && x.SourceService == payload.SourceService && x.SourceDocumentId == payload.SourceDocumentId);
                if (string.Equals(payload.MovementType, InventoryMovementRequestTypes.StatusTransfer, StringComparison.OrdinalIgnoreCase))
                    return await HasStatusLegsAsync(provider, integrationEvent.OrganizationId, integrationEvent.EnvironmentId,
                        payload.SourceService, payload.SourceDocumentId, payload.IdempotencyKey, token);
                return await movements.AnyAsync(x => x.IdempotencyKey == payload.IdempotencyKey, token);
            });
        services.AddIntegrationEventDeadLetterReplay<InventoryReservationReleaseRequestedIntegrationEvent, InventoryReservationReleaseRequestedIntegrationEventHandlerForReleaseReservations>(
            InventoryReservationReleaseRequestedIntegrationEventHandlerForReleaseReservations.ConsumerName,
            (consumer, integrationEvent, token) => consumer.HandleAsync(integrationEvent, token),
            async (provider, integrationEvent, token) =>
            {
                var payload = integrationEvent.Payload;
                // Match the original command's MES alias and document/line selection.
                var source = payload.ReservationSourceService.Trim();
                var sources = string.Equals(source, InventoryIntegrationEventSources.BusinessMes, StringComparison.OrdinalIgnoreCase)
                    ? new[] { InventoryIntegrationEventSources.BusinessMes, InventoryMovementSourceServices.Mes } : [source];
                var documents = new[] { payload.SourceDocumentId }.Concat(payload.SourceDocumentLineIds)
                    .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                return !await provider.GetRequiredService<ApplicationDbContext>().StockReservations.AsNoTracking().AnyAsync(x =>
                    x.OrganizationId == integrationEvent.OrganizationId && x.EnvironmentId == integrationEvent.EnvironmentId
                    && sources.Contains(x.SourceService) && x.OpenQuantity > 0m
                    && (documents.Contains(x.SourceDocumentId) || x.SourceDocumentLineId != null && documents.Contains(x.SourceDocumentLineId)), token);
            });
        services.AddIntegrationEventDeadLetterReplay<ApprovalCompletedIntegrationEvent, ApprovalCompletedIntegrationEventHandlerForStockCountAdjustment>(
            ApprovalCompletedIntegrationEventHandlerForStockCountAdjustment.ConsumerName,
            (consumer, integrationEvent, token) => consumer.HandleAsync(integrationEvent, token),
            async (provider, integrationEvent, token) =>
            {
                var payload = integrationEvent.Payload;
                var document = payload.DocumentReference;
                if (!string.Equals(document.SourceService, ApprovalSourceServices.Inventory, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(document.DocumentType, ApprovalDocumentTypes.StockCountVariance, StringComparison.OrdinalIgnoreCase)) return false;
                var status = string.Equals(payload.Result, ApprovalResults.Approved, StringComparison.OrdinalIgnoreCase)
                    ? StockCountAdjustmentStatuses.Posted
                    : string.Equals(payload.Result, ApprovalResults.Rejected, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(payload.Result, ApprovalResults.Returned, StringComparison.OrdinalIgnoreCase)
                            ? StockCountAdjustmentStatuses.Voided : null;
                return status is not null && await provider.GetRequiredService<ApplicationDbContext>().StockCountAdjustments.AsNoTracking().AnyAsync(x =>
                    x.OrganizationId == integrationEvent.OrganizationId && x.EnvironmentId == integrationEvent.EnvironmentId
                    && x.CountTaskCode == document.DocumentId && x.ApprovalChainId == payload.ChainId && x.Status == status, token);
            });
        services.AddIntegrationEventDeadLetterReplay<InspectionResultIntegrationEvent, QualityInspectionResultIntegrationEventHandlerForStockStatusTransfer>(
            QualityInspectionResultIntegrationEventHandlerForStockStatusTransfer.ConsumerName,
            (consumer, integrationEvent, token) => consumer.HandleAsync(integrationEvent, token),
            (provider, integrationEvent, token) => HasStatusLegsAsync(provider, integrationEvent.OrganizationId, integrationEvent.EnvironmentId,
                InventoryMovementSourceServices.Quality, integrationEvent.Payload.SourceDocumentId, integrationEvent.IdempotencyKey, token));
        return services;
    }

    private static async Task<bool> HasStatusLegsAsync(IServiceProvider provider, string organization, string environment,
        string source, string document, string key, CancellationToken token)
    {
        var outbound = InventoryIdempotencyKeyPolicy.Compose(key, PostStockStatusTransferCommandHandler.OutboundLegSuffix);
        var inbound = InventoryIdempotencyKeyPolicy.Compose(key, PostStockStatusTransferCommandHandler.InboundLegSuffix);
        var movements = provider.GetRequiredService<ApplicationDbContext>().StockMovements.AsNoTracking().Where(x =>
            x.OrganizationId == organization && x.EnvironmentId == environment && x.SourceService == source && x.SourceDocumentId == document);
        return await movements.AnyAsync(x => x.IdempotencyKey == outbound, token)
            && await movements.AnyAsync(x => x.IdempotencyKey == inbound, token);
    }
}
