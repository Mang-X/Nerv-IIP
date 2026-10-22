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
    internal const string BackfillNoticeStoreKey = "quality-backfill-handled-notices";

    public static IServiceCollection AddQualityDeadLetterReplay(this IServiceCollection services)
    {
        // Keep handled backfill notices in the same store without reporting them as replay rejection.
        var original = services.Last(x => x.ServiceType == typeof(IIntegrationEventDeadLetterStore));
        services.Remove(original);
        services.Add(original.ImplementationInstance is { } instance
            ? ServiceDescriptor.KeyedSingleton(typeof(IIntegrationEventDeadLetterStore), BackfillNoticeStoreKey, instance)
            : original.ImplementationFactory is { } factory
                ? ServiceDescriptor.DescribeKeyed(typeof(IIntegrationEventDeadLetterStore), BackfillNoticeStoreKey, (provider, _) => factory(provider), original.Lifetime)
                : ServiceDescriptor.DescribeKeyed(typeof(IIntegrationEventDeadLetterStore), BackfillNoticeStoreKey, original.ImplementationType!, original.Lifetime));
        services.Add(ServiceDescriptor.Describe(typeof(IIntegrationEventDeadLetterStore),
            provider => provider.GetRequiredKeyedService<IIntegrationEventDeadLetterStore>(BackfillNoticeStoreKey), original.Lifetime));
        services.AddObservedIntegrationEventDeadLetterStore();
        Add<WmsIntegrationEvent, WmsInboundOrderCompletedIntegrationEventHandlerForCreateInspectionTasks>(services, WmsInboundOrderCompletedIntegrationEventHandlerForCreateInspectionTasks.ConsumerName,
            HasWmsTasksAsync);
        Add<PurchaseReceiptRecordedIntegrationEvent, ErpPurchaseReceiptRecordedIntegrationEventHandlerForCreateInspectionTasks>(services, ErpPurchaseReceiptRecordedIntegrationEventHandlerForCreateInspectionTasks.ConsumerName,
            async (provider, evt, token) =>
                await AllReceiptLinesCompletedAsync(provider, evt, token));
        Add<MesOperationTaskCompletedIntegrationEvent, MesOperationCompletedIntegrationEventHandlerForCreateInspectionTasks>(services, MesOperationCompletedIntegrationEventHandlerForCreateInspectionTasks.ConsumerName,
            (provider, evt, token) => evt.Payload.RequiresQualityInspection
                ? HasSourceTaskAsync(provider, evt, QualityInspectionSourceTypes.Operation, QualityInspectionSourceServices.Mes,
                    evt.Payload.WorkOrderId, evt.Payload.OperationTaskId, evt.Payload.SkuCode, evt.IdempotencyKey, token) : Task.FromResult(true));
        Add<FinishedGoodsReceiptRequestedIntegrationEvent, MesFinishedGoodsReceiptRequestedIntegrationEventHandlerForCreateInspectionTasks>(services, MesFinishedGoodsReceiptRequestedIntegrationEventHandlerForCreateInspectionTasks.ConsumerName,
            (provider, evt, token) => HasSourceTaskAsync(provider, evt, QualityInspectionSourceTypes.Final, QualityInspectionSourceServices.Mes,
                evt.Payload.RequestNo, evt.Payload.WorkOrderId, evt.Payload.SkuCode, evt.IdempotencyKey, token));
        Add<ProductionReportRecordedIntegrationEvent, MesProductionReportRecordedIntegrationEventHandlerForCreateFirstArticleInspectionTask>(services, MesProductionReportRecordedIntegrationEventHandlerForCreateFirstArticleInspectionTask.ConsumerName,
            (provider, evt, token) => evt.Payload.IsReversal ? Task.FromResult(true)
                : HasTaskAsync(provider, evt, FirstArticleInspection.TriggerIdempotencyKey(evt.OrganizationId, evt.EnvironmentId,
                    evt.Payload.WorkOrderId, evt.Payload.OperationTaskId), token));
        Add<WorkOrderReleasedIntegrationEvent, WorkOrderReleasedIntegrationEventHandlerForCreatePeriodicInspectionContexts>(services, WorkOrderReleasedIntegrationEventHandlerForCreatePeriodicInspectionContexts.ConsumerName);
        Add<WorkOrderReleaseProjectionBackfilledIntegrationEvent, WorkOrderReleaseProjectionBackfilledIntegrationEventHandlerForCreatePeriodicInspectionContexts>(services, WorkOrderReleaseProjectionBackfilledIntegrationEventHandlerForCreatePeriodicInspectionContexts.ConsumerName,
            HasBackfillProjectionAsync);
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

    private static Task<bool> HasSourceTaskAsync(IServiceProvider provider, IIntegrationEventEnvelope evt,
        string sourceType, string sourceService, string documentId, string? lineId, string sku, string key, CancellationToken token) =>
        InspectionTaskGeneration.TaskExistsAsync(provider.GetRequiredService<ApplicationDbContext>(), evt.OrganizationId,
            evt.EnvironmentId, sourceType, sourceService, documentId, lineId, sku, key, token);

    private static async Task<bool> HasBackfillProjectionAsync(IServiceProvider provider,
        WorkOrderReleaseProjectionBackfilledIntegrationEvent evt, CancellationToken token)
    {
        var db = provider.GetRequiredService<ApplicationDbContext>();
        if (!await db.ProcessedIntegrationEvents.AnyAsync(x => x.ConsumerName == WorkOrderReleaseProjectionBackfilledIntegrationEventHandlerForCreatePeriodicInspectionContexts.ConsumerName
            && x.EventId == evt.EventId, token)) return false;
        var operations = evt.Payload.Operations!.Select(x => x.OperationId.Trim()).ToArray();
        var workOrderId = evt.Payload.WorkOrderId.Trim();
        return await db.PeriodicInspectionOperations.CountAsync(x => x.OrganizationId == evt.OrganizationId
            && x.EnvironmentId == evt.EnvironmentId && x.WorkOrderId == workOrderId
            && operations.Contains(x.OperationId) && x.ReleasedAtUtc != null, token) == operations.Length;
    }

    private static Task<bool> HasTaskAsync(IServiceProvider provider, IIntegrationEventEnvelope evt, string key, CancellationToken token) =>
        provider.GetRequiredService<ApplicationDbContext>().InspectionTasks.AsNoTracking()
            .AnyAsync(x => x.OrganizationId == evt.OrganizationId && x.EnvironmentId == evt.EnvironmentId
                && x.TriggerIdempotencyKey == key.Trim(), token);

    private static async Task<bool> AllReceiptLinesCompletedAsync(IServiceProvider provider,
        PurchaseReceiptRecordedIntegrationEvent evt, CancellationToken token)
    {
        foreach (var line in evt.Payload.Lines ?? [])
            if (!InspectionTaskGeneration.ShouldSkipInspection(line.QualityStatus)
                && !await HasSourceTaskAsync(provider, evt, QualityInspectionSourceTypes.Receiving, QualityInspectionSourceServices.Erp,
                    evt.Payload.PurchaseReceiptNo, line.LineReference, line.SkuCode, $"{evt.IdempotencyKey}:{line.LineReference}", token)) return false;
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
                && !await HasSourceTaskAsync(provider, evt, QualityInspectionSourceTypes.Receiving, QualityInspectionSourceTypes.Wms,
                    payload.PublicReference, line.LineReference, line.SkuCode, $"{evt.IdempotencyKey}:{line.LineReference}", token)) return false;
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
                    && string.Equals(payload.MovementType, InventoryMovementTypes.StatusTransferIn, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(payload.QualityStatus, InventoryQualityStatuses.Restricted, StringComparison.OrdinalIgnoreCase) && payload.Quantity > 0);
    }
}
