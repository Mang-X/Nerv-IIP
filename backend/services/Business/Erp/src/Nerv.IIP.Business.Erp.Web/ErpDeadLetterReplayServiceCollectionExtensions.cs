using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetCorePal.Extensions.Repository.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Approval;
using Nerv.IIP.Contracts.DemandPlanning;
using Nerv.IIP.Contracts.Erp;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Contracts.MasterData;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Contracts.Wms;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Erp.Web;

public static class ErpDeadLetterReplayServiceCollectionExtensions
{
    public static IServiceCollection AddErpDeadLetterReplay(this IServiceCollection services)
    {
        services.AddObservedIntegrationEventDeadLetterStore();
        Add<ApprovalCompletedIntegrationEvent, ApprovalCompletedIntegrationEventHandlerForReleasePurchaseOrder>(services, ApprovalCompletedIntegrationEventHandlerForReleasePurchaseOrder.ConsumerName);
        Add<BusinessPartnerChangedIntegrationEvent, BusinessPartnerChangedIntegrationEventHandlerForProjectBusinessPartnerAvailability>(services, BusinessPartnerChangedIntegrationEventHandlerForProjectBusinessPartnerAvailability.ConsumerName);
        Add<MesOperationActualTimeSettledIntegrationEvent, MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost>(services, MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost.ConsumerName);
        Add<MesOperationActualTimeSettledV2IntegrationEvent, MesOperationActualTimeSettledV2IntegrationEventHandlerForAccumulateMachineOverhead>(services, MesOperationActualTimeSettledV2IntegrationEventHandlerForAccumulateMachineOverhead.ConsumerName);
        Add<MesOperationActualTimeSettlementVoidedIntegrationEvent, MesOperationActualTimeSettlementVoidedIntegrationEventHandlerForReverseLaborCost>(services, MesOperationActualTimeSettlementVoidedIntegrationEventHandlerForReverseLaborCost.ConsumerName);
        Add<MesOperationActualTimeSettlementVoidedV2IntegrationEvent, MesOperationActualTimeSettlementVoidedV2IntegrationEventHandlerForReverseMachineOverhead>(services, MesOperationActualTimeSettlementVoidedV2IntegrationEventHandlerForReverseMachineOverhead.ConsumerName);
        Add<PlanningSuggestionAcceptedIntegrationEvent, PlanningSuggestionAcceptedIntegrationEventHandlerForCreatePurchaseRequisition>(services, PlanningSuggestionAcceptedIntegrationEventHandlerForCreatePurchaseRequisition.ConsumerName);
        Add<ProductionReportRecordedIntegrationEvent, ProductionReportRecordedIntegrationEventHandlerForAccumulateLaborCost>(services, ProductionReportRecordedIntegrationEventHandlerForAccumulateLaborCost.ConsumerName);
        Add<PurchaseReceiptRecordedIntegrationEvent, PurchaseReceiptRecordedIntegrationEventHandlerForPostGrIrAccrual>(services, PurchaseReceiptRecordedIntegrationEventHandlerForPostGrIrAccrual.ConsumerName);
        Add<InspectionResultIntegrationEvent, QualityInspectionResultIntegrationEventHandlerForSettleSalesReturnCredit>(services, QualityInspectionResultIntegrationEventHandlerForSettleSalesReturnCredit.ConsumerName);
        Add<ReworkWorkOrderCreatedIntegrationEvent, ReworkWorkOrderCreatedIntegrationEventHandlerForAttributeCost>(services, ReworkWorkOrderCreatedIntegrationEventHandlerForAttributeCost.ConsumerName);
        Add<StockMovementPostedIntegrationEvent, StockMovementPostedIntegrationEventHandlerForAccumulateMaterialCost>(services, StockMovementPostedIntegrationEventHandlerForAccumulateMaterialCost.ConsumerName);
        Add<WmsIntegrationEvent, WmsInboundOrderCompletedIntegrationEventHandlerForRecordPurchaseReceipt>(services, WmsInboundOrderCompletedIntegrationEventHandlerForRecordPurchaseReceipt.ConsumerName,
            async (provider, integrationEvent, token) => await HasInboxAsync(provider, WmsInboundOrderCompletedIntegrationEventHandlerForRecordPurchaseReceipt.ConsumerName, integrationEvent, token)
                || WmsInboundOrderCompletedIntegrationEventHandlerForRecordPurchaseReceipt.IsPurchaseOrderSource(integrationEvent.Payload.SourceDocumentType)
                    && await provider.GetRequiredService<ApplicationDbContext>().PurchaseReceipts.AsNoTracking().AnyAsync(x =>
                        x.OrganizationId == integrationEvent.OrganizationId && x.EnvironmentId == integrationEvent.EnvironmentId
                        && x.PurchaseReceiptNo == integrationEvent.Payload.PublicReference.Trim(), token));
        Add<WmsIntegrationEvent, WmsInboundOrderCompletedIntegrationEventHandlerForRecordSalesReturnReceipt>(services, WmsInboundOrderCompletedIntegrationEventHandlerForRecordSalesReturnReceipt.ConsumerName);
        Add<WmsIntegrationEvent, WmsOutboundOrderCancelledIntegrationEventHandlerForCancelDeliveryProjection>(services, WmsOutboundOrderCancelledIntegrationEventHandlerForCancelDeliveryProjection.ConsumerName);
        Add<WmsIntegrationEvent, WmsOutboundOrderCompletedIntegrationEventHandlerForCreateAccountReceivable>(services, WmsOutboundOrderCompletedIntegrationEventHandlerForCreateAccountReceivable.ConsumerName);
        Add<WmsIntegrationEvent, WmsOutboundOrderCompletedIntegrationEventHandlerForRecordPurchaseReturn>(services, WmsOutboundOrderCompletedIntegrationEventHandlerForRecordPurchaseReturn.ConsumerName);
        Add<WorkOrderCompletedIntegrationEvent, WorkOrderCompletedIntegrationEventHandlerForCapitalizeCost>(services, WorkOrderCompletedIntegrationEventHandlerForCapitalizeCost.ConsumerName);
        return services;
    }

    private static void Add<TEvent, TConsumer>(IServiceCollection services, string consumerName,
        Func<IServiceProvider, TEvent, CancellationToken, Task<bool>>? completed = null)
        where TEvent : IIntegrationEventEnvelope
        where TConsumer : class, IIntegrationEventHandler<TEvent>
    {
        services.TryAddScoped<TConsumer>();
        services.AddIntegrationEventDeadLetterReplay<TEvent, ErpReplayConsumer<TEvent, TConsumer>>(consumerName,
            (consumer, integrationEvent, token) => consumer.HandleAsync(integrationEvent, token),
            completed ?? ((provider, integrationEvent, token) => HasInboxAsync(provider, consumerName, integrationEvent, token)));
    }

    private static Task<bool> HasInboxAsync(IServiceProvider provider, string consumerName,
        IIntegrationEventEnvelope integrationEvent, CancellationToken token) =>
        provider.GetRequiredService<ApplicationDbContext>().ProcessedIntegrationEvents.AsNoTracking()
            .AnyAsync(x => x.ConsumerName == consumerName && x.IdempotencyKey == integrationEvent.IdempotencyKey, token);
}

internal sealed class ErpReplayConsumer<TEvent, TConsumer>(TConsumer consumer, ApplicationDbContext dbContext,
    ITransactionUnitOfWork unitOfWork)
    where TEvent : IIntegrationEventEnvelope
    where TConsumer : class, IIntegrationEventHandler<TEvent>
{
    public Task HandleAsync(TEvent integrationEvent, CancellationToken token) =>
        CostingIntegrationEventUnitOfWork.ExecuteAsync(dbContext, unitOfWork,
            () => consumer.HandleAsync(integrationEvent, token), token);
}
