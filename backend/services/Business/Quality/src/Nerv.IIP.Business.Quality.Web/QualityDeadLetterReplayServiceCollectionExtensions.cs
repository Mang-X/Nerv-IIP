using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Erp;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Contracts.Wms;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Quality.Web;

public static class QualityDeadLetterReplayServiceCollectionExtensions
{
    public static IServiceCollection AddQualityDeadLetterReplay(this IServiceCollection services)
    {
        services.AddObservedIntegrationEventDeadLetterStore();
        Add<WmsIntegrationEvent, WmsInboundOrderCompletedIntegrationEventHandlerForCreateInspectionTasks>(services, WmsInboundOrderCompletedIntegrationEventHandlerForCreateInspectionTasks.ConsumerName,
            HasWmsTasksAsync);
        Add<PurchaseReceiptRecordedIntegrationEvent, ErpPurchaseReceiptRecordedIntegrationEventHandlerForCreateInspectionTasks>(services, ErpPurchaseReceiptRecordedIntegrationEventHandlerForCreateInspectionTasks.ConsumerName,
            async (provider, evt, token) =>
                await AllReceiptLinesCompletedAsync(provider, evt, token));
        Add<MesOperationTaskCompletedIntegrationEvent, MesOperationCompletedIntegrationEventHandlerForCreateInspectionTasks>(services, MesOperationCompletedIntegrationEventHandlerForCreateInspectionTasks.ConsumerName,
            (provider, evt, token) => evt.Payload.RequiresQualityInspection
                ? HasTaskAsync(provider, evt, evt.IdempotencyKey, token) : Task.FromResult(true));
        Add<FinishedGoodsReceiptRequestedIntegrationEvent, MesFinishedGoodsReceiptRequestedIntegrationEventHandlerForCreateInspectionTasks>(services, MesFinishedGoodsReceiptRequestedIntegrationEventHandlerForCreateInspectionTasks.ConsumerName,
            (provider, evt, token) => HasTaskAsync(provider, evt, evt.IdempotencyKey, token));
        Add<ProductionReportRecordedIntegrationEvent, MesProductionReportRecordedIntegrationEventHandlerForCreateFirstArticleInspectionTask>(services, MesProductionReportRecordedIntegrationEventHandlerForCreateFirstArticleInspectionTask.ConsumerName,
            (provider, evt, token) => evt.Payload.IsReversal ? Task.FromResult(true)
                : HasTaskAsync(provider, evt, FirstArticleInspection.TriggerIdempotencyKey(evt.OrganizationId, evt.EnvironmentId,
                    evt.Payload.WorkOrderId, evt.Payload.OperationTaskId), token));
        Add<WorkOrderReleasedIntegrationEvent, WorkOrderReleasedIntegrationEventHandlerForCreatePeriodicInspectionContexts>(services, WorkOrderReleasedIntegrationEventHandlerForCreatePeriodicInspectionContexts.ConsumerName);
        Add<WorkOrderReleaseProjectionBackfilledIntegrationEvent, WorkOrderReleaseProjectionBackfilledIntegrationEventHandlerForCreatePeriodicInspectionContexts>(services, WorkOrderReleaseProjectionBackfilledIntegrationEventHandlerForCreatePeriodicInspectionContexts.ConsumerName);
        Add<ProductionReportRecordedIntegrationEvent, ProductionReportRecordedIntegrationEventHandlerForTrackPeriodicInspection>(services, ProductionReportRecordedIntegrationEventHandlerForTrackPeriodicInspection.ConsumerName);
        Add<MesOperationTaskCompletedIntegrationEvent, MesOperationTaskCompletedIntegrationEventHandlerForClosePeriodicInspection>(services, MesOperationTaskCompletedIntegrationEventHandlerForClosePeriodicInspection.ConsumerName);
        Add<InspectionResultIntegrationEvent, InspectionResultIntegrationEventHandlerForEvaluateSpc>(services, InspectionResultIntegrationEventHandlerForEvaluateSpc.ConsumerName);
        Add<DefectRaisedIntegrationEvent, DefectRaisedIntegrationEventHandlerForOpenNcr>(services,
            DefectRaisedIntegrationEventHandlerForOpenNcr.ConsumerName,
            (provider, evt, token) => provider.GetRequiredService<ApplicationDbContext>().NonconformanceReports.AsNoTracking()
                .AnyAsync(x => x.OrganizationId == evt.OrganizationId && x.EnvironmentId == evt.EnvironmentId
                    && x.SourceType == "in-process" && x.SourceDocumentId == evt.Payload.DefectNo, token));
        Add<ReworkWorkOrderCreatedIntegrationEvent, ReworkWorkOrderCreatedIntegrationEventHandlerForBindQualityNcr>(services,
            ReworkWorkOrderCreatedIntegrationEventHandlerForBindQualityNcr.ConsumerName,
            async (provider, evt, token) => Guid.TryParse(evt.Payload.SourceNcrId, out var id)
                && await provider.GetRequiredService<ApplicationDbContext>().NonconformanceReports.AsNoTracking()
                    .AnyAsync(x => x.Id == new NonconformanceReportId(id) && x.OrganizationId == evt.OrganizationId
                        && x.EnvironmentId == evt.EnvironmentId && x.ReworkWorkOrderId == evt.Payload.ReworkWorkOrderId, token));
        Add<StockMovementPostedIntegrationEvent, StockMovementPostedIntegrationEventHandlerForCompleteQualityNcrInventoryDisposition>(services,
            StockMovementPostedIntegrationEventHandlerForCompleteQualityNcrInventoryDisposition.ConsumerName,
            HasInventoryDispositionAsync);
        return services;
    }

    private static void Add<TEvent, TConsumer>(IServiceCollection services, string consumerName,
        Func<IServiceProvider, TEvent, CancellationToken, Task<bool>>? completed = null)
        where TEvent : IIntegrationEventEnvelope
        where TConsumer : class, IIntegrationEventHandler<TEvent> =>
        services.AddIntegrationEventDeadLetterReplay<TEvent, TConsumer>(consumerName,
            (consumer, evt, token) => consumer.HandleAsync(evt, token),
            completed ?? ((provider, evt, token) => provider.GetRequiredService<ApplicationDbContext>()
                .ProcessedIntegrationEvents.AsNoTracking()
                .AnyAsync(x => x.ConsumerName == consumerName && x.EventId == evt.EventId, token)));

    private static Task<bool> HasTaskAsync(IServiceProvider provider, IIntegrationEventEnvelope evt, string key, CancellationToken token) =>
        provider.GetRequiredService<ApplicationDbContext>().InspectionTasks.AsNoTracking()
            .AnyAsync(x => x.OrganizationId == evt.OrganizationId && x.EnvironmentId == evt.EnvironmentId
                && x.TriggerIdempotencyKey == key.Trim(), token);

    private static async Task<bool> AllReceiptLinesCompletedAsync(IServiceProvider provider,
        PurchaseReceiptRecordedIntegrationEvent evt, CancellationToken token)
    {
        foreach (var line in evt.Payload.Lines ?? [])
            if (!InspectionTaskGeneration.ShouldSkipInspection(line.QualityStatus)
                && !await HasTaskAsync(provider, evt, $"{evt.IdempotencyKey}:{line.LineReference}", token)) return false;
        return true;
    }

    private static async Task<bool> HasWmsTasksAsync(IServiceProvider provider, WmsIntegrationEvent evt, CancellationToken token)
    {
        var payload = evt.Payload;
        var lines = payload.Lines?.Count > 0 ? payload.Lines
            : payload.SkuCode is null || payload.UomCode is null || payload.Quantity is null ? []
                : [new WmsIntegrationPayloadLine(payload.LineReference ?? payload.PublicReference, payload.SkuCode,
                    payload.UomCode, payload.SiteCode, payload.LocationCode, payload.Quantity.Value, payload.Status)];
        foreach (var line in lines)
            if (!InspectionTaskGeneration.ShouldSkipInspection(line.Status)
                && !await HasTaskAsync(provider, evt, $"{evt.IdempotencyKey}:{line.LineReference}", token)) return false;
        return true;
    }

    private static async Task<bool> HasInventoryDispositionAsync(IServiceProvider provider,
        StockMovementPostedIntegrationEvent evt, CancellationToken token)
    {
        var payload = evt.Payload;
        if (!string.Equals(payload.SourceService, InventoryMovementSourceServices.Quality, StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParse(payload.SourceDocumentId, out var id)) return false;
        var ncr = await provider.GetRequiredService<ApplicationDbContext>().NonconformanceReports.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == new NonconformanceReportId(id)
                && x.OrganizationId == evt.OrganizationId && x.EnvironmentId == evt.EnvironmentId, token);
        return ncr is not null && Math.Abs(payload.Quantity) == ncr.DefectQuantity
            && (ncr.DispositionType == QualityNcrDispositionTypes.Scrap && ncr.ScrapMovementId == payload.InventoryMovementId
                || ncr.DispositionType == QualityNcrDispositionTypes.ConditionalRelease && ncr.Status == "closed"
                    && payload.MovementType == InventoryMovementTypes.StatusTransferIn
                    && payload.QualityStatus == InventoryQualityStatuses.Restricted && payload.Quantity > 0);
    }
}
