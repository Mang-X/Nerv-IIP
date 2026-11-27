using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Wms.Web.Application.IntegrationEventHandlers;

internal sealed class ErpReceiptInspectionProjection(
    ApplicationDbContext db,
    IIntegrationEventDeadLetterStore deadLetters)
{
    private const string ConsumerName = QualityInspectionResultIntegrationEventHandlerForReleaseWmsInboundGate.ConsumerName;

    public async Task HandleAsync(InspectionResultIntegrationEvent message, CancellationToken token)
    {
        var payload = message.Payload;
        if (!IsValidDecision(message))
        {
            await RejectAsync(message, "erp-receipt-quality-result-invalid",
                "Expected a Quality decision with an ERP receipt, WMS batch and original purchase order line.", token);
            return;
        }

        var receipt = await FindBatch(db, message).SingleOrDefaultAsync(token);
        if (receipt?.ErpPurchaseReceiptNo is null)
        {
            await RejectAsync(message, "erp-receipt-acceptance-not-bound",
                "The scoped WMS batch has no committed ERP receipt binding. Replay after binding the original batch.", token);
            return;
        }

        var line = MatchOriginalLine(receipt, payload);
        if (line is null)
        {
            await RejectAsync(message, "erp-receipt-quality-result-conflict",
                "The Quality decision disagrees with the bound ERP receipt or its original line.", token);
            return;
        }

        if (MatchesDecision(line, payload))
            return;

        if (line.ErpReceiptInspectionRecordId is not null
            || receipt.Lines.Any(x => x.ErpReceiptInspectionRecordId == payload.InspectionRecordId)
            || !await WmsProcessedIntegrationEventInbox.TryRecordAsync(db, ConsumerName, message, token))
        {
            await RejectAsync(message, "erp-receipt-quality-result-conflict",
                "The original line, inspection record or event key is already associated with a different decision.", token);
            return;
        }

        receipt.ProjectErpReceiptInspection(line.LineNo, payload.InspectionRecordId, payload.Result,
            payload.InspectedQuantity, payload.DispositionReason);
        if (await db.SaveChangesAsync(token) == 0 && !await IsPersistedAsync(db, message, token))
        {
            await RejectAsync(message, "erp-receipt-quality-result-conflict",
                "Another delivery committed a conflicting event key.", token);
        }
    }

    internal static async Task<bool> IsPersistedAsync(ApplicationDbContext db, InspectionResultIntegrationEvent message, CancellationToken token)
    {
        if (!IsValidDecision(message))
            return false;

        var receipt = await FindBatch(db, message).AsNoTracking().SingleOrDefaultAsync(token);
        return receipt is not null
            && MatchOriginalLine(receipt, message.Payload) is { } line
            && MatchesDecision(line, message.Payload);
    }

    private static IQueryable<InboundOrder> FindBatch(ApplicationDbContext db, InspectionResultIntegrationEvent message) =>
        db.InboundOrders.Include(x => x.Lines).Where(x => x.OrganizationId == message.OrganizationId
            && x.EnvironmentId == message.EnvironmentId && x.ReceiptBatchId == message.Payload.WmsReceiptBatchId);

    private static InboundOrderLine? MatchOriginalLine(InboundOrder receipt, InspectionResultPayload payload)
    {
        if (receipt.ErpPurchaseReceiptNo != payload.SourceDocumentId || !receipt.RequiresErpReceiptAcceptance
            || receipt.Status == InboundOrderStatus.Cancelled)
            return null;

        return receipt.Lines.SingleOrDefault(line => line.LineNo == payload.SourceDocumentLineId
            && line.SkuCode == payload.SkuCode && line.LotNo == payload.LotNo && line.SerialNo == payload.SerialNo
            && line.RequiresQualityInspection && line.ReceivedQuantity == payload.InspectedQuantity);
    }

    private static bool MatchesDecision(InboundOrderLine line, InspectionResultPayload payload) =>
        line.ErpReceiptInspectionRecordId == payload.InspectionRecordId
        && line.ErpReceiptInspectionResult == payload.Result
        && line.ErpReceiptInspectedQuantity == payload.InspectedQuantity
        && line.ErpReceiptInspectionReason == payload.DispositionReason;

    private static bool IsValidDecision(InspectionResultIntegrationEvent message)
    {
        var payload = message.Payload;
        var expected = message.EventType switch
        {
            QualityIntegrationEventTypes.InspectionPassed => QualityInspectionDispositionStatuses.Passed,
            QualityIntegrationEventTypes.InspectionConditionalReleased => QualityInspectionDispositionStatuses.ConditionalRelease,
            QualityIntegrationEventTypes.InspectionRejected => QualityInspectionDispositionStatuses.Rejected,
            _ => null,
        };

        return expected is not null && payload.Result == expected
            && message.SourceService == QualityIntegrationEventSources.BusinessQuality
            && payload.SourceType == QualityInspectionSourceTypes.Receiving
            && payload.SourceService == QualityInspectionSourceServices.Erp
            && payload.WmsReceiptBatchId is { } batchId && batchId != Guid.Empty
            && !string.IsNullOrWhiteSpace(payload.SourceDocumentId)
            && !string.IsNullOrWhiteSpace(payload.SourceDocumentLineId)
            && !string.IsNullOrWhiteSpace(payload.InspectionRecordId) && payload.InspectionRecordId.Length <= 150
            && payload.InspectedQuantity > 0m
            && payload.DispositionReason is not { Length: > 500 };
    }

    private Task RejectAsync(InspectionResultIntegrationEvent message, string code, string reason, CancellationToken token) =>
        deadLetters.AddAsync(IntegrationEventDeadLetterMessage.Create(ConsumerName, message, code, reason), token);
}
