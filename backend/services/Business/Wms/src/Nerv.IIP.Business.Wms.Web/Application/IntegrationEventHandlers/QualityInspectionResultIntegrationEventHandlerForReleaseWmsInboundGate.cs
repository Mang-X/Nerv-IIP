using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Wms.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Quality.InspectionResultIntegrationEvent", ConsumerName)]
public sealed class QualityInspectionResultIntegrationEventHandlerForReleaseWmsInboundGate(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<InspectionResultIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-wms.quality-inspection-result";

    private readonly IntegrationEventConsumerGuard<InspectionResultIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            [
                QualityIntegrationEventTypes.InspectionPassed,
                QualityIntegrationEventTypes.InspectionConditionalReleased,
                QualityIntegrationEventTypes.InspectionRejected
            ],
            QualityIntegrationEventVersions.V1));

    public async Task HandleAsync(InspectionResultIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe("Nerv.IIP.Contracts.Quality.InspectionResultIntegrationEvent", Group = ConsumerName)]
    public Task HandleCapAsync(InspectionResultIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(InspectionResultIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var payload = integrationEvent.Payload;
        if (!string.Equals(payload.SourceService, "wms", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(payload.SourceType, "receiving", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!await WmsProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken))
        {
            return;
        }

        var inbound = await dbContext.InboundOrders
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(
                x => x.OrganizationId == integrationEvent.OrganizationId
                    && x.EnvironmentId == integrationEvent.EnvironmentId
                    && x.InboundOrderNo == payload.SourceDocumentId,
                cancellationToken);
        if (inbound is null)
        {
            await DeadLetterAsync(
                integrationEvent,
                "missing-inbound-order",
                $"WMS inbound order '{payload.SourceDocumentId}' was not found for quality inspection result '{payload.InspectionRecordId}'.",
                cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        try
        {
            var lotNo = payload.StockRelease?.LotNo ?? payload.LotNo;
            var serialNo = payload.StockRelease?.SerialNo ?? payload.SerialNo;
            var supplierReturn = inbound.ApplyInspectionResult(
                integrationEvent.EventType,
                payload.InspectionRecordId,
                payload.SkuCode,
                lotNo,
                serialNo,
                payload.InspectedQuantity,
                payload.DispositionReason);
            if (supplierReturn is not null
                && !await SupplierReturnExistsAsync(supplierReturn.SupplierReturnNo, integrationEvent, cancellationToken))
            {
                dbContext.SupplierReturnRequests.Add(supplierReturn);
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            await DeadLetterDivergenceAsync(integrationEvent, exception, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private Task<bool> SupplierReturnExistsAsync(
        string supplierReturnNo,
        InspectionResultIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        return dbContext.SupplierReturnRequests.AnyAsync(
            x => x.OrganizationId == integrationEvent.OrganizationId
                && x.EnvironmentId == integrationEvent.EnvironmentId
                && x.SupplierReturnNo == supplierReturnNo,
            cancellationToken);
    }

    private Task DeadLetterAsync(
        InspectionResultIntegrationEvent integrationEvent,
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

    private Task DeadLetterDivergenceAsync(
        InspectionResultIntegrationEvent integrationEvent,
        Exception exception,
        CancellationToken cancellationToken)
    {
        return DeadLetterAsync(
            integrationEvent,
            "quality-inspection-result-divergence",
            exception.Message,
            cancellationToken);
    }
}
