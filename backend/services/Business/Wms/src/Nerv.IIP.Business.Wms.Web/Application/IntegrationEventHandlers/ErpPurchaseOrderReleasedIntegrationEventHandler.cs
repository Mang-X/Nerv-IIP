using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Contracts.Erp;
using Nerv.IIP.Contracts.Wms;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;
using NetCorePal.Extensions.Repository.EntityFrameworkCore;

namespace Nerv.IIP.Business.Wms.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Erp.PurchaseOrderReleasedV2IntegrationEvent", ConsumerName)]
public sealed class ErpPurchaseOrderReleasedIntegrationEventHandler(
    ApplicationDbContext db,
    ITransactionUnitOfWork unitOfWork,
    IIntegrationEventDeadLetterStore deadLetters,
    IOptionsSnapshot<WmsPurchaseOrderReceivingOptions> receiving)
    : IIntegrationEventHandler<PurchaseOrderReleasedV2IntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-wms.erp-purchase-order-released";

    private readonly IntegrationEventConsumerGuard<PurchaseOrderReleasedV2IntegrationEvent> guard = new(
        new IntegrationEventEnvelopeValidator(), deadLetters,
        new IntegrationEventConsumerOptions(ConsumerName,
            PurchaseOrderReleasedV2IntegrationEvent.Type, PurchaseOrderReleasedV2IntegrationEvent.Version));

    [CapSubscribe(PurchaseOrderReleasedV2IntegrationEvent.TopicTemplate, Group = ConsumerName)]
    public Task HandleCapAsync(PurchaseOrderReleasedV2IntegrationEvent message, CancellationToken cancellationToken) =>
        db.Database.CurrentTransaction is not null
            ? HandleAsync(message, cancellationToken)
            : new WmsReplayConsumer<PurchaseOrderReleasedV2IntegrationEvent, ErpPurchaseOrderReleasedIntegrationEventHandler>(this, unitOfWork)
                .HandleAsync(message, cancellationToken, (consumer, ev, token) => consumer.HandleAsync(ev, token));

    public Task HandleAsync(PurchaseOrderReleasedV2IntegrationEvent message, CancellationToken cancellationToken) =>
        guard.HandleAsync(message, HandleValidatedAsync, cancellationToken);

    private async Task HandleValidatedAsync(PurchaseOrderReleasedV2IntegrationEvent message, CancellationToken token)
    {
        if (message.SourceService != ErpIntegrationEventSources.BusinessErp)
        {
            await RejectAsync(message, "unexpected-source-service", "Only BusinessERP may release purchase orders.", token);
            return;
        }

        if (!IsValid(message))
        {
            await RejectAsync(message, "invalid-purchase-order-release",
                "Purchase order release requires valid scope, order and site codes, and unique original lines with representable positive quantities.", token);
            return;
        }

        var payload = message.Payload;
        if (payload.ReceivingAuthority == PurchaseReceiptInventoryPostingRoute.Direct)
        {
            return;
        }

        if (payload.ReceivingAuthority != PurchaseReceiptInventoryPostingRoute.Wms)
        {
            await RejectAsync(message, "invalid-purchase-order-receiving-authority",
                "Purchase order release has an unsupported receiving authority.", token);
            return;
        }

        var number = payload.PurchaseOrderNo;
        await PurchaseOrderInboundLock.AcquireAsync(db, message.OrganizationId, message.EnvironmentId, number, token);
        var candidates = await Candidates(db, message).ToListAsync(token);
        var order = candidates.SingleOrDefault(x => x.InboundOrderNo == number);
        if (order is not null && MatchesRelease(order, message))
        {
            return;
        }

        if (order is not null || candidates.Any(x => x.Status != InboundOrderStatus.Cancelled))
        {
            await RejectAsync(message, "purchase-order-inbound-conflict",
                $"Purchase order '{number}' already has a conflicting WMS inbound receipt or its number is occupied. Reconcile the existing receipt before replaying.", token);
            return;
        }

        var route = receiving.Value.Routes.SingleOrDefault(x => x.OrganizationId == message.OrganizationId
            && x.EnvironmentId == message.EnvironmentId && x.SiteCode == payload.SiteCode);
        if (route is null)
        {
            await RejectAsync(message, "missing-purchase-order-receiving-route",
                $"Configure exactly one PurchaseOrderReceiving route for '{message.OrganizationId}/{message.EnvironmentId}/{payload.SiteCode}' before replaying.", token);
            return;
        }

        db.InboundOrders.Add(InboundOrder.CreatePendingReceipt(
            message.OrganizationId, message.EnvironmentId, number, WmsSourceDocumentTypes.PurchaseOrder,
            number, payload.SiteCode, payload.Lines.Select(line =>
                new ExpectedInboundOrderLine(line.LineNo, line.SkuCode, line.UomCode, line.OrderedQuantity,
                    route.StagingLocationCode, route.QualityStatus, route.OwnerType, null))));
    }

    internal static async Task<bool> HasPersistedReleaseAsync(
        ApplicationDbContext db, PurchaseOrderReleasedV2IntegrationEvent message, CancellationToken token)
    {
        if (message.SourceService != ErpIntegrationEventSources.BusinessErp || !IsValid(message)
            || message.Payload.ReceivingAuthority != PurchaseReceiptInventoryPostingRoute.Wms)
        {
            return false;
        }

        var order = await Candidates(db, message)
            .SingleOrDefaultAsync(x => x.InboundOrderNo == message.Payload.PurchaseOrderNo, token);
        return order is not null && MatchesRelease(order, message);
    }

    private static IQueryable<InboundOrder> Candidates(ApplicationDbContext db, PurchaseOrderReleasedV2IntegrationEvent message) =>
        db.InboundOrders.AsNoTracking().Include(x => x.Lines).Where(x =>
            x.OrganizationId == message.OrganizationId && x.EnvironmentId == message.EnvironmentId
            && (x.InboundOrderNo == message.Payload.PurchaseOrderNo
                || (x.SourceDocumentType.ToLower() == WmsSourceDocumentTypes.PurchaseOrder
                    && (x.SourceDocumentId == message.Payload.PurchaseOrderNo
                        || x.SourceDocumentId == message.Payload.PurchaseOrderId))));

    private static bool MatchesRelease(InboundOrder order, PurchaseOrderReleasedV2IntegrationEvent message)
    {
        var payload = message.Payload;
        if (order.SourceDocumentType != WmsSourceDocumentTypes.PurchaseOrder
            || order.SourceDocumentId != payload.PurchaseOrderNo || order.SiteCode != payload.SiteCode
            || order.Lines.Count != payload.Lines.Count)
        {
            return false;
        }

        var expected = payload.Lines.ToDictionary(x => x.LineNo, StringComparer.Ordinal);
        return order.Lines.All(line => expected.TryGetValue(line.LineNo, out var source)
            && line.SkuCode == source.SkuCode && line.UomCode == source.UomCode
            && line.ExpectedQuantity == source.OrderedQuantity);
    }

    private static bool IsValid(PurchaseOrderReleasedV2IntegrationEvent message) =>
        HasCode(message.OrganizationId, 100) && HasCode(message.EnvironmentId, 100)
        && message.Payload is not null
        && HasCode(message.Payload.PurchaseOrderId, 150) && HasCode(message.Payload.PurchaseOrderNo, 100)
        && HasCode(message.Payload.SiteCode, 100)
        && message.Payload.Lines is { Count: > 0 } lines
        && lines.All(line => line is not null && HasCode(line.LineNo, 100)
            && HasCode(line.SkuCode, 100) && HasCode(line.UomCode, 50)
            && line.OrderedQuantity > 0m && line.OrderedQuantity < 1_000_000_000_000m
            && decimal.Round(line.OrderedQuantity, 6) == line.OrderedQuantity)
        && lines.Select(line => line.LineNo).Distinct(StringComparer.Ordinal).Count() == lines.Count;

    private static bool HasCode(string value, int maxLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maxLength && value == value.Trim();

    private Task RejectAsync(PurchaseOrderReleasedV2IntegrationEvent message, string code, string detail, CancellationToken token) =>
        deadLetters.AddAsync(IntegrationEventDeadLetterMessage.Create(ConsumerName, message, code, detail), token);
}
