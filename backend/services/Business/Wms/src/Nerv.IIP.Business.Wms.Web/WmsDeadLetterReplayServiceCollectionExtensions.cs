using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.Repository;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetCorePal.Extensions.DistributedTransactions;
using NetCorePal.Extensions.Repository.EntityFrameworkCore;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InventoryMovementRequestAggregate;
using Nerv.IIP.Business.Wms.Web.Application.Commands;
using Nerv.IIP.Business.Wms.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Erp;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Contracts.Wms;

namespace Nerv.IIP.Business.Wms.Web;

public static class WmsDeadLetterReplayServiceCollectionExtensions
{
    public static IServiceCollection AddWmsDeadLetterReplay(this IServiceCollection services)
    {
        services.AddObservedIntegrationEventDeadLetterStore();
        Add<WmsOutboundOrderRequestedIntegrationEvent, WmsOutboundOrderRequestedIntegrationEventHandler>(services,
            WmsOutboundOrderRequestedIntegrationEventHandler.ConsumerName, async (provider, ev, token) =>
                string.Equals(ev.SourceService, WmsIntegrationEventSources.BusinessErp, StringComparison.OrdinalIgnoreCase)
                && await Db(provider).OutboundOrders.AsNoTracking().AnyAsync(x => x.OrganizationId == ev.OrganizationId
                    && x.EnvironmentId == ev.EnvironmentId && x.SourceDocumentType == WmsSourceDocumentTypes.DeliveryOrder
                    && x.SourceDocumentId == ev.Payload.DeliveryOrderNo, token));
        Add<MesMaterialIssueRequestedIntegrationEvent, MesMaterialIssueRequestedIntegrationEventHandler>(services,
            MesMaterialIssueRequestedIntegrationEventHandler.ConsumerName, async (provider, ev, token) =>
                string.Equals(ev.SourceService, MesIntegrationEventSources.BusinessMes, StringComparison.OrdinalIgnoreCase)
                && await Db(provider).OutboundOrders.AsNoTracking().AnyAsync(x => x.OrganizationId == ev.OrganizationId
                    && x.EnvironmentId == ev.EnvironmentId && x.SourceDocumentType == WmsSourceDocumentTypes.MesMaterialIssueRequest
                    && x.SourceDocumentId == ev.Payload.RequestNo.Trim(), token)
                && await Db(provider).WarehouseTasks.AsNoTracking().AnyAsync(x => x.OrganizationId == ev.OrganizationId
                    && x.EnvironmentId == ev.EnvironmentId && x.SourceOrderNo == PrepareMesMaterialIssueOutboundCommandHandler.OutboundOrderNoFor(ev.Payload.RequestNo), token));
        Add<SalesReturnAuthorizedIntegrationEvent, ErpSalesReturnAuthorizedIntegrationEventHandler>(services,
            ErpSalesReturnAuthorizedIntegrationEventHandler.ConsumerName, async (provider, ev, token) =>
                await HasInboxAsync(provider, ErpSalesReturnAuthorizedIntegrationEventHandler.ConsumerName, ev, token)
                && await Db(provider).InboundOrders.AsNoTracking().AnyAsync(x => x.OrganizationId == ev.OrganizationId
                    && x.EnvironmentId == ev.EnvironmentId && x.SourceDocumentType == WmsSourceDocumentTypes.SalesReturnRma
                    && x.SourceDocumentId == ev.Payload.RmaNo, token));
        Add<InspectionResultIntegrationEvent, QualityInspectionResultIntegrationEventHandlerForReleaseWmsInboundGate>(services,
            QualityInspectionResultIntegrationEventHandlerForReleaseWmsInboundGate.ConsumerName, QualityCompletedAsync,
            (consumer, ev, token) => consumer.ReplayAsync(ev, token));
        Add<StockMovementPostedIntegrationEvent, StockMovementPostedIntegrationEventHandlerForMarkWmsRequestPosted>(services,
            StockMovementPostedIntegrationEventHandlerForMarkWmsRequestPosted.ConsumerName, async (provider, ev, token) =>
                string.Equals(ev.Payload.SourceService, InventoryMovementSourceServices.Wms, StringComparison.OrdinalIgnoreCase)
                && await Db(provider).InventoryMovementRequests.AsNoTracking().AnyAsync(x => x.OrganizationId == ev.OrganizationId
                    && x.EnvironmentId == ev.EnvironmentId && x.MovementType == ev.Payload.MovementType
                    && x.SourceDocumentId == ev.Payload.SourceDocumentId && x.SourceDocumentLineId == ev.Payload.SourceDocumentLineId
                    && x.IdempotencyKey == ev.Payload.IdempotencyKey && x.Status == InventoryMovementRequestStatus.Posted
                    && x.InventoryMovementId == ev.Payload.InventoryMovementId, token));
        Add<StockMovementPostingFailedIntegrationEvent, StockMovementPostingFailedIntegrationEventHandlerForMarkWmsRequestFailed>(services,
            StockMovementPostingFailedIntegrationEventHandlerForMarkWmsRequestFailed.ConsumerName, async (provider, ev, token) =>
                string.Equals(ev.Payload.SourceService, InventoryMovementSourceServices.Wms, StringComparison.OrdinalIgnoreCase)
                && await Db(provider).InventoryMovementRequests.AsNoTracking().AnyAsync(x => x.OrganizationId == ev.OrganizationId
                    && x.EnvironmentId == ev.EnvironmentId && x.MovementType == ev.Payload.MovementType
                    && x.SourceDocumentId == ev.Payload.SourceDocumentId && x.SourceDocumentLineId == ev.Payload.SourceDocumentLineId
                    && x.IdempotencyKey == ev.Payload.IdempotencyKey && x.Status == InventoryMovementRequestStatus.Failed
                    && x.FailureCode == ev.Payload.FailureCode && x.FailureMessage == ev.Payload.FailureMessage, token));
        Add<InventoryReservationExpiredIntegrationEvent, InventoryReservationExpiredIntegrationEventHandlerForCancelWmsPicking>(services,
            InventoryReservationExpiredIntegrationEventHandlerForCancelWmsPicking.ConsumerName);
        Add<WmsIntegrationEvent, WcsTaskCancelledIntegrationEventHandlerForAdapterCancellation>(services,
            WcsTaskCancelledIntegrationEventHandlerForAdapterCancellation.ConsumerName);
        return services;
    }

    private static ApplicationDbContext Db(IServiceProvider provider) => provider.GetRequiredService<ApplicationDbContext>();

    private static void Add<TEvent, TConsumer>(IServiceCollection services, string name,
        Func<IServiceProvider, TEvent, CancellationToken, Task<bool>>? completed = null,
        Func<TConsumer, TEvent, CancellationToken, Task>? consume = null)
        where TEvent : IIntegrationEventEnvelope
        where TConsumer : class, IIntegrationEventHandler<TEvent>
    {
        services.TryAddScoped<TConsumer>();
        services.AddIntegrationEventDeadLetterReplay<TEvent, WmsReplayConsumer<TEvent, TConsumer>>(name,
            (wrapper, ev, token) => wrapper.HandleAsync(ev, token, consume ?? ((consumer, message, ct) => consumer.HandleAsync(message, ct))),
            completed ?? ((provider, ev, token) => HasInboxAsync(provider, name, ev, token)));
    }

    private static Task<bool> HasInboxAsync(IServiceProvider provider, string name, IIntegrationEventEnvelope ev, CancellationToken token) =>
        Db(provider).ProcessedIntegrationEvents.AsNoTracking().AnyAsync(x => x.ConsumerName == name && x.EventId == ev.EventId
            && x.IdempotencyKey == ev.IdempotencyKey && x.EventType == ev.EventType && x.EventVersion == ev.EventVersion
            && x.SourceService == ev.SourceService, token);

    private static async Task<bool> QualityCompletedAsync(IServiceProvider provider, InspectionResultIntegrationEvent ev, CancellationToken token)
    {
        if (!await HasInboxAsync(provider, QualityInspectionResultIntegrationEventHandlerForReleaseWmsInboundGate.ConsumerName, ev, token))
            return false;
        var inbound = await Db(provider).InboundOrders.AsNoTracking().Include(x => x.Lines).SingleOrDefaultAsync(x =>
            x.OrganizationId == ev.OrganizationId && x.EnvironmentId == ev.EnvironmentId && x.InboundOrderNo == ev.Payload.SourceDocumentId, token);
        var expected = ev.EventType switch
        {
            QualityIntegrationEventTypes.InspectionPassed => InboundQualityGateStatuses.Passed,
            QualityIntegrationEventTypes.InspectionConditionalReleased => InboundQualityGateStatuses.ConditionalReleased,
            QualityIntegrationEventTypes.InspectionRejected => InboundQualityGateStatuses.Rejected,
            _ => null
        };
        return expected is not null && inbound is not null && inbound.Lines.Count(x =>
            x.SkuCode == ev.Payload.SkuCode.Trim() && x.LotNo == WmsText.Optional(ev.Payload.StockRelease?.LotNo ?? ev.Payload.LotNo)
            && x.SerialNo == WmsText.Optional(ev.Payload.StockRelease?.SerialNo ?? ev.Payload.SerialNo)
            && x.InspectionRecordId == ev.Payload.InspectionRecordId.Trim() && x.QualityGateStatus == expected) == 1;
    }
}

internal sealed class WmsReplayConsumer<TEvent, TConsumer>(TConsumer consumer, ITransactionUnitOfWork unitOfWork)
    where TEvent : IIntegrationEventEnvelope
    where TConsumer : class
{
    public async Task HandleAsync(TEvent ev, CancellationToken token, Func<TConsumer, TEvent, CancellationToken, Task> consume)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(token);
        unitOfWork.CurrentTransaction = transaction;
        try
        {
            await consume(consumer, ev, token);
            await ((IUnitOfWork)unitOfWork).SaveEntitiesAsync(token);
            await unitOfWork.CommitAsync(token);
        }
        catch
        {
            await unitOfWork.RollbackAsync(token);
            throw;
        }
        finally
        {
            unitOfWork.CurrentTransaction = null;
        }
    }
}
