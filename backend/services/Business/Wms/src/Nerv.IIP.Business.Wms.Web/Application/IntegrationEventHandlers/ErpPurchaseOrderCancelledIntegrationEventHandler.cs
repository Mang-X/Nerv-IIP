using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Infrastructure.IntegrationEvents;
using Nerv.IIP.Contracts.Erp;
using Nerv.IIP.Contracts.Wms;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;
using NetCorePal.Extensions.Repository.EntityFrameworkCore;

namespace Nerv.IIP.Business.Wms.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Erp.PurchaseOrderCancelledIntegrationEvent", ConsumerName)]
public sealed class ErpPurchaseOrderCancelledIntegrationEventHandler(
    ApplicationDbContext db,
    ITransactionUnitOfWork unitOfWork,
    IIntegrationEventDeadLetterStore deadLetters)
    : IIntegrationEventHandler<PurchaseOrderCancelledIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-wms.erp-purchase-order-cancelled";

    private readonly IntegrationEventConsumerGuard<PurchaseOrderCancelledIntegrationEvent> guard = new(
        new IntegrationEventEnvelopeValidator(), deadLetters,
        new IntegrationEventConsumerOptions(ConsumerName,
            PurchaseOrderCancelledIntegrationEvent.Type, PurchaseOrderCancelledIntegrationEvent.Version));

    [CapSubscribe(PurchaseOrderCancelledIntegrationEvent.TopicTemplate, Group = ConsumerName)]
    public Task HandleCapAsync(PurchaseOrderCancelledIntegrationEvent message, CancellationToken cancellationToken) =>
        db.Database.CurrentTransaction is not null
            ? HandleAsync(message, cancellationToken)
            : new WmsReplayConsumer<PurchaseOrderCancelledIntegrationEvent, ErpPurchaseOrderCancelledIntegrationEventHandler>(this, unitOfWork)
                .HandleAsync(message, cancellationToken, (consumer, ev, token) => consumer.HandleAsync(ev, token));

    public Task HandleAsync(PurchaseOrderCancelledIntegrationEvent message, CancellationToken cancellationToken) =>
        guard.HandleAsync(message, HandleValidatedAsync, cancellationToken);

    private async Task HandleValidatedAsync(PurchaseOrderCancelledIntegrationEvent message, CancellationToken token)
    {
        if (message.SourceService != ErpIntegrationEventSources.BusinessErp)
        {
            await RejectAsync(message, "unexpected-source-service", "Only BusinessERP may cancel purchase orders.", token);
            return;
        }

        var payload = message.Payload;
        if (payload is null || !HasCode(message.OrganizationId, 100) || !HasCode(message.EnvironmentId, 100)
            || !HasCode(payload.PurchaseOrderNo, 100) || !HasCode(payload.PurchaseOrderId, 150)
            || !HasCode(payload.SiteCode, 100) || payload.PurchaseOrderVersion <= 0 || payload.Reason?.Length > 1000)
        {
            await RejectAsync(message, "invalid-purchase-order-cancellation",
                "Cancellation requires valid scope, source identity, site, committed order version and a reason of at most 1000 characters.", token);
            return;
        }

        await PurchaseOrderInboundLock.AcquireAsync(db, message.OrganizationId, message.EnvironmentId, payload.PurchaseOrderNo, token);
        var existing = await db.PurchaseOrderCancellations.FindAsync(
            [message.OrganizationId, message.EnvironmentId, payload.PurchaseOrderNo], token);
        if (existing is not null && !Matches(existing, payload))
        {
            await RejectAsync(message, "purchase-order-cancellation-conflict",
                "The source already has a cancellation with a different ERP identity, site or committed version.", token);
            return;
        }

        if (!await WmsProcessedIntegrationEventInbox.TryRecordAsync(db, ConsumerName, message, token))
        {
            if (existing is null)
            {
                await RejectAsync(message, "purchase-order-cancellation-inbox-conflict",
                    "The cancellation idempotency key is already recorded without a matching source cancellation.", token);
            }

            return;
        }

        if (existing is null)
        {
            var reason = string.IsNullOrWhiteSpace(payload.Reason) ? "erp-purchase-order-cancelled" : payload.Reason.Trim();
            db.PurchaseOrderCancellations.Add(new PurchaseOrderCancellation(
                message.OrganizationId, message.EnvironmentId, payload.PurchaseOrderNo, payload.PurchaseOrderId,
                payload.SiteCode, payload.PurchaseOrderVersion, message.OccurredAtUtc.ToUniversalTime(), reason));

            var order = await db.InboundOrders.Include(x => x.Lines).SingleOrDefaultAsync(x =>
                x.OrganizationId == message.OrganizationId && x.EnvironmentId == message.EnvironmentId
                && x.InboundOrderNo == payload.PurchaseOrderNo && x.SourceDocumentType == WmsSourceDocumentTypes.PurchaseOrder
                && x.SourceDocumentId == payload.PurchaseOrderNo && x.SiteCode == payload.SiteCode, token);
            order?.CancelUnreceivedExpectation(reason);
        }
    }

    internal static Task<bool> HasPersistedCancellationAsync(
        ApplicationDbContext db, PurchaseOrderCancelledIntegrationEvent message, CancellationToken token) =>
        db.PurchaseOrderCancellations.AnyAsync(x => x.OrganizationId == message.OrganizationId
            && x.EnvironmentId == message.EnvironmentId && x.PurchaseOrderNo == message.Payload.PurchaseOrderNo
            && x.PurchaseOrderId == message.Payload.PurchaseOrderId && x.SiteCode == message.Payload.SiteCode
            && x.PurchaseOrderVersion == message.Payload.PurchaseOrderVersion, token);

    private static bool Matches(PurchaseOrderCancellation cancellation, PurchaseOrderCancelledPayload payload) =>
        cancellation.PurchaseOrderId == payload.PurchaseOrderId && cancellation.SiteCode == payload.SiteCode
        && cancellation.PurchaseOrderVersion == payload.PurchaseOrderVersion;

    private static bool HasCode(string value, int maxLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maxLength && value == value.Trim();

    private Task RejectAsync(PurchaseOrderCancelledIntegrationEvent message, string code, string detail, CancellationToken token) =>
        deadLetters.AddAsync(IntegrationEventDeadLetterMessage.Create(ConsumerName, message, code, detail), token);
}
