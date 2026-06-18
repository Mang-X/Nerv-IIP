using System.Text.Json;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseReceiptAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseRequisitionAggregate;
using Nerv.IIP.Business.Erp.Domain.DomainEvents;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventConverters;

namespace Nerv.IIP.Business.Erp.Web.Tests;

public sealed class ErpProcurementIntegrationEventTests
{
    [Fact]
    public void Purchase_requisition_created_event_uses_stable_adr0011_envelope_shape()
    {
        var requisition = PurchaseRequisition.CreateFromSuggestion(
            "org-001",
            "env-dev",
            "REQ-001",
            "MPS-SUG-001",
            "SKU-RM-1000",
            "kg",
            "SITE-01",
            120m,
            new DateOnly(2026, 6, 1));
        var converter = new PurchaseRequisitionCreatedIntegrationEventConverter();

        var integrationEvent = converter.Convert(new PurchaseRequisitionCreatedDomainEvent(requisition));
        var json = JsonSerializer.Serialize(integrationEvent, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal("erp.PurchaseRequisitionCreated", integrationEvent.EventType);
        Assert.Equal(1, integrationEvent.EventVersion);
        Assert.Equal("business-erp", integrationEvent.SourceService);
        Assert.Equal("org-001", integrationEvent.OrganizationId);
        Assert.Equal("env-dev", integrationEvent.EnvironmentId);
        Assert.Contains("MPS-SUG-001", integrationEvent.IdempotencyKey, StringComparison.Ordinal);
        Assert.Contains("\"eventType\":\"erp.PurchaseRequisitionCreated\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Purchase_order_released_event_uses_required_event_type()
    {
        var order = PurchaseOrder.Create(
            "org-001",
            "env-dev",
            "PO-001",
            "SUP-001",
            "SITE-01",
            [new PurchaseOrderLineDraft("LINE-001", "SKU-RM-1000", "kg", 3m, 12m, new DateOnly(2026, 6, 5))]);
        var converter = new PurchaseOrderReleasedIntegrationEventConverter();

        var integrationEvent = converter.Convert(new PurchaseOrderReleasedDomainEvent(order));

        Assert.Equal("erp.PurchaseOrderReleased", integrationEvent.EventType);
        Assert.Equal("PO-001", integrationEvent.Payload.PurchaseOrderNo);
        Assert.Equal(36m, integrationEvent.Payload.TotalAmount);
    }

    [Fact]
    public void Purchase_receipt_recorded_event_uses_required_event_type()
    {
        var order = PurchaseOrder.Create(
            "org-001",
            "env-dev",
            "PO-001",
            "SUP-001",
            "SITE-01",
            [new PurchaseOrderLineDraft("LINE-001", "SKU-RM-1000", "kg", 3m, 12m, new DateOnly(2026, 6, 5))]);
        order.MarkApprovalRequested("approval-chain-001");
        order.ReleaseAfterApproval("approval-chain-001");
        var receipt = PurchaseReceipt.Record(order, "RCV-001", [new PurchaseReceiptLineDraft("LINE-001", 2m, "accepted")]);
        var converter = new PurchaseReceiptRecordedIntegrationEventConverter();

        var integrationEvent = converter.Convert(new PurchaseReceiptRecordedDomainEvent(receipt));

        Assert.Equal("erp.PurchaseReceiptRecorded", integrationEvent.EventType);
        Assert.Equal("RCV-001", integrationEvent.Payload.PurchaseReceiptNo);
        Assert.Equal("accepted", integrationEvent.Payload.QualityStatus);
    }
}
