using DotNetCore.CAP;
using MediatR;
using Nerv.IIP.Business.Erp.Domain.DomainEvents;
using Nerv.IIP.Contracts.Erp;

namespace Nerv.IIP.Business.Erp.Web.Application.IntegrationEventConverters;

internal sealed class PurchaseOrderReleasedV2IntegrationEventPublisher(
    PurchaseOrderReleaseOutbox? outbox = null) : INotificationHandler<PurchaseOrderReleasedDomainEvent>
{
    public Task Handle(PurchaseOrderReleasedDomainEvent notification, CancellationToken cancellationToken) =>
        outbox?.PublishAsync(notification, cancellationToken) ?? Task.CompletedTask;
}

internal sealed class PurchaseOrderReleaseOutbox(
    ICapPublisher publisher,
    IHostEnvironment environment,
    IErpIntegrationEventContextAccessor contextAccessor)
{
    public Task PublishAsync(PurchaseOrderReleasedDomainEvent notification, CancellationToken cancellationToken)
    {
        var order = notification.PurchaseOrder;
        var context = contextAccessor.GetContext();
        var key = EventIds.Idempotency("purchase-order-released", "v2",
            Uri.EscapeDataString(order.OrganizationId),
            Uri.EscapeDataString(order.EnvironmentId),
            Uri.EscapeDataString(order.PurchaseOrderNo));
        var snapshot = new PurchaseOrderReleasedV2IntegrationEvent(
            EventIds.New(),
            PurchaseOrderReleasedV2IntegrationEvent.Type,
            PurchaseOrderReleasedV2IntegrationEvent.Version,
            notification.ReleasedAtUtc,
            ErpIntegrationEventSources.BusinessErp,
            context.CorrelationId,
            context.CausationId,
            order.OrganizationId,
            order.EnvironmentId,
            context.Actor,
            key,
            new PurchaseOrderReleasedV2Payload(
                ErpIntegrationEventConverterHelpers.PublicId(order.Id),
                order.PurchaseOrderNo,
                order.SupplierCode,
                order.SiteCode,
                order.Lines.OrderBy(line => line.LineNo, StringComparer.Ordinal)
                    .Select(line => new PurchaseOrderReleasedV2LinePayload(
                        line.LineNo, line.SkuCode, line.UomCode, line.OrderedQuantity, line.PromisedDate))
                    .ToArray()));

        return publisher.PublishAsync(PurchaseOrderReleasedV2IntegrationEvent.Topic(environment.EnvironmentName),
            snapshot, cancellationToken: cancellationToken);
    }
}
