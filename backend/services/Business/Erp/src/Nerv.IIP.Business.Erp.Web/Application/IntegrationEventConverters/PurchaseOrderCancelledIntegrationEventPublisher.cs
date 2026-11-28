using DotNetCore.CAP;
using MediatR;
using Nerv.IIP.Business.Erp.Domain.DomainEvents;
using Nerv.IIP.Contracts.Erp;

namespace Nerv.IIP.Business.Erp.Web.Application.IntegrationEventConverters;

internal sealed class PurchaseOrderCancelledIntegrationEventPublisher(
    PurchaseOrderCancellationOutbox? outbox = null) : INotificationHandler<PurchaseOrderCancelledDomainEvent>
{
    public Task Handle(PurchaseOrderCancelledDomainEvent notification, CancellationToken cancellationToken) =>
        (outbox ?? throw new InvalidOperationException("WMS-owned purchase order cancellation requires the ERP CAP outbox."))
        .PublishAsync(notification, cancellationToken);
}

internal sealed class PurchaseOrderCancellationOutbox(
    ICapPublisher publisher,
    IHostEnvironment environment,
    IErpIntegrationEventContextAccessor contextAccessor)
{
    public Task PublishAsync(PurchaseOrderCancelledDomainEvent notification, CancellationToken cancellationToken)
    {
        if (publisher.Transaction is not { AutoCommit: false, DbTransaction: not null })
        {
            throw new InvalidOperationException("WMS-owned purchase order cancellation requires an active CAP transaction.");
        }

        var order = notification.PurchaseOrder;
        var context = contextAccessor.GetContext();
        var message = new PurchaseOrderCancelledIntegrationEvent(
            EventIds.New(),
            PurchaseOrderCancelledIntegrationEvent.Type,
            PurchaseOrderCancelledIntegrationEvent.Version,
            notification.CancelledAtUtc,
            ErpIntegrationEventSources.BusinessErp,
            context.CorrelationId,
            context.CausationId,
            order.OrganizationId,
            order.EnvironmentId,
            context.Actor,
            EventIds.Idempotency("purchase-order-cancelled", "v1",
                Uri.EscapeDataString(order.OrganizationId),
                Uri.EscapeDataString(order.EnvironmentId),
                Uri.EscapeDataString(order.PurchaseOrderNo)),
            new PurchaseOrderCancelledPayload(
                ErpIntegrationEventConverterHelpers.PublicId(order.Id),
                order.PurchaseOrderNo,
                order.SiteCode,
                notification.PurchaseOrderVersion,
                notification.Reason));

        return publisher.PublishAsync(PurchaseOrderCancelledIntegrationEvent.Topic(environment.EnvironmentName),
            message, cancellationToken: cancellationToken);
    }
}
