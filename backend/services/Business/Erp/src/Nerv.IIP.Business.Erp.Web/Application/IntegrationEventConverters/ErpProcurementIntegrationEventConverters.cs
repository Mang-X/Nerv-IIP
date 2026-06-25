using Nerv.IIP.Business.Erp.Domain.DomainEvents;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEvents;
using Nerv.IIP.Contracts.Inventory;
using static Nerv.IIP.Business.Erp.Web.Application.IntegrationEventConverters.ErpIntegrationEventConverterHelpers;

namespace Nerv.IIP.Business.Erp.Web.Application.IntegrationEventConverters;

public sealed class PurchaseRequisitionCreatedIntegrationEventConverter
    : IIntegrationEventConverter<PurchaseRequisitionCreatedDomainEvent, ErpIntegrationEvent<PurchaseRequisitionCreatedPayload>>
{
    public ErpIntegrationEvent<PurchaseRequisitionCreatedPayload> Convert(PurchaseRequisitionCreatedDomainEvent domainEvent)
    {
        var requisition = domainEvent.PurchaseRequisition;
        return Envelope(
            ErpIntegrationEventTypes.PurchaseRequisitionCreated,
            requisition.OrganizationId,
            requisition.EnvironmentId,
            EventIds.Idempotency("purchase-requisition-created", requisition.OrganizationId, requisition.EnvironmentId, requisition.SuggestionId),
            new PurchaseRequisitionCreatedPayload(
                PublicId(requisition.Id),
                requisition.RequisitionNo,
                requisition.SuggestionId,
                requisition.SkuCode,
                requisition.UomCode,
                requisition.SiteCode,
                requisition.Quantity,
                requisition.RequiredDate));
    }
}

public sealed class PurchaseOrderReleasedIntegrationEventConverter
    : IIntegrationEventConverter<PurchaseOrderReleasedDomainEvent, ErpIntegrationEvent<PurchaseOrderReleasedPayload>>
{
    public ErpIntegrationEvent<PurchaseOrderReleasedPayload> Convert(PurchaseOrderReleasedDomainEvent domainEvent)
    {
        var order = domainEvent.PurchaseOrder;
        return Envelope(
            ErpIntegrationEventTypes.PurchaseOrderReleased,
            order.OrganizationId,
            order.EnvironmentId,
            EventIds.Idempotency("purchase-order-released", order.OrganizationId, order.EnvironmentId, order.PurchaseOrderNo),
            new PurchaseOrderReleasedPayload(
                PublicId(order.Id),
                order.PurchaseOrderNo,
                order.SupplierCode,
                order.SiteCode,
                order.TotalAmount));
    }
}

public sealed class PurchaseReceiptRecordedIntegrationEventConverter
    : IIntegrationEventConverter<PurchaseReceiptRecordedDomainEvent, PurchaseReceiptRecordedIntegrationEvent>
{
    public PurchaseReceiptRecordedIntegrationEvent Convert(PurchaseReceiptRecordedDomainEvent domainEvent)
    {
        var receipt = domainEvent.PurchaseReceipt;
        var payload = new PurchaseReceiptRecordedPayload(
            PublicId(receipt.Id),
            receipt.PurchaseReceiptNo,
            receipt.PurchaseOrderNo,
            receipt.SupplierCode,
            receipt.SiteCode,
            receipt.QualityStatus);
        return new PurchaseReceiptRecordedIntegrationEvent(
            EventIds.New(),
            ErpIntegrationEventTypes.PurchaseReceiptRecorded,
            1,
            DateTimeOffset.UtcNow,
            ErpIntegrationEventSources.BusinessErp,
            "system:erp",
            "system:erp",
            receipt.OrganizationId,
            receipt.EnvironmentId,
            "system:erp",
            EventIds.Idempotency("purchase-receipt-recorded", receipt.OrganizationId, receipt.EnvironmentId, receipt.PurchaseReceiptNo),
            payload);
    }
}

public sealed class PurchaseReceiptInventoryMovementRequestedIntegrationEventConverter
    : IIntegrationEventConverter<PurchaseReceiptInventoryMovementRequestedDomainEvent, InventoryMovementRequestedIntegrationEvent>
{
    public InventoryMovementRequestedIntegrationEvent Convert(PurchaseReceiptInventoryMovementRequestedDomainEvent domainEvent)
    {
        var receipt = domainEvent.PurchaseReceipt;
        var line = domainEvent.Line;
        var occurredAtUtc = DateTimeOffset.UtcNow;
        var idempotencyKey = EventIds.Idempotency("purchase-receipt-inventory-movement", receipt.OrganizationId, receipt.EnvironmentId, receipt.PurchaseReceiptNo, line.PurchaseOrderLineNo);
        return new InventoryMovementRequestedIntegrationEvent(
            EventIds.New(),
            InventoryIntegrationEventTypes.InventoryMovementRequested,
            InventoryIntegrationEventVersions.V1,
            occurredAtUtc,
            InventoryIntegrationEventSources.BusinessErp,
            "system:erp",
            "system:erp",
            receipt.OrganizationId,
            receipt.EnvironmentId,
            "system:erp",
            idempotencyKey,
            new InventoryMovementRequestedPayload(
                "inbound",
                InventoryIntegrationEventSources.BusinessErp,
                receipt.PurchaseReceiptNo,
                line.PurchaseOrderLineNo,
                idempotencyKey,
                line.SkuCode,
                line.UomCode,
                receipt.SiteCode,
                line.LocationCode,
                line.LotNo,
                null,
                NormalizeInventoryQualityStatus(line.QualityStatus),
                "company",
                null,
                line.ReceivedQuantity,
                occurredAtUtc));
    }

    private static string NormalizeInventoryQualityStatus(string qualityStatus)
    {
        var normalized = qualityStatus.Trim().ToLowerInvariant();
        return normalized switch
        {
            "accepted" or "unrestricted" => "unrestricted",
            "inspection" or "quality" => "quality",
            "rejected" or "blocked" => "blocked",
            _ => normalized,
        };
    }
}

internal static class ErpIntegrationEventConverterHelpers
{
    public static ErpIntegrationEvent<TPayload> Envelope<TPayload>(
        string eventType,
        string organizationId,
        string environmentId,
        string idempotencyKey,
        TPayload payload)
    {
        return new ErpIntegrationEvent<TPayload>(
            EventIds.New(),
            eventType,
            1,
            DateTimeOffset.UtcNow,
            ErpIntegrationEventSources.BusinessErp,
            "system:erp",
            "system:erp",
            organizationId,
            environmentId,
            "system:erp",
            idempotencyKey,
            payload);
    }

    public static string PublicId(object? stronglyTypedId)
    {
        return stronglyTypedId?.ToString() ?? "unassigned";
    }
}

internal static class EventIds
{
    public static string New() => $"evt-{Guid.CreateVersion7():N}";

    public static string Idempotency(params string[] parts) => $"erp:{string.Join(':', parts)}";
}
