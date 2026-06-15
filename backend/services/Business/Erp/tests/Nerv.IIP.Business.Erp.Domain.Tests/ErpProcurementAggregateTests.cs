using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseReceiptAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseRequisitionAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.RequestForQuotationAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SupplierInvoiceAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SupplierQuotationAggregate;
using Nerv.IIP.Business.Erp.Domain.DomainEvents;

namespace Nerv.IIP.Business.Erp.Domain.Tests;

public sealed class ErpProcurementAggregateTests
{
    [Fact]
    public void Purchase_requisition_can_be_created_from_demand_planning_suggestion()
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

        Assert.Equal("MPS-SUG-001", requisition.SuggestionId);
        Assert.Equal(PurchaseRequisitionStatus.Open, requisition.Status);
        Assert.Single(requisition.GetDomainEvents());
        Assert.IsType<PurchaseRequisitionCreatedDomainEvent>(requisition.GetDomainEvents().Single());
    }

    [Fact]
    public void Request_for_quotation_requires_supplier_and_item()
    {
        Assert.Throws<ArgumentException>(() => RequestForQuotation.Create(
            "org-001",
            "env-dev",
            "RFQ-001",
            [],
            [NewRfqLine()]));

        Assert.Throws<ArgumentException>(() => RequestForQuotation.Create(
            "org-001",
            "env-dev",
            "RFQ-001",
            ["SUP-001"],
            []));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    public void Supplier_quotation_rejects_non_positive_quantity_or_price(decimal quantity, decimal unitPrice)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SupplierQuotation.Receive(
            "org-001",
            "env-dev",
            "QUO-001",
            "RFQ-001",
            "SUP-001",
            [new SupplierQuotationLineDraft("LINE-001", "SKU-RM-1000", "kg", quantity, unitPrice, new DateOnly(2026, 6, 3))]));
    }

    [Fact]
    public void Purchase_order_rejects_empty_lines()
    {
        Assert.Throws<ArgumentException>(() => PurchaseOrder.Create(
            "org-001",
            "env-dev",
            "PO-001",
            "SUP-001",
            "SITE-01",
            []));
    }

    [Fact]
    public void Purchase_receipt_cannot_exceed_open_order_quantity()
    {
        var order = PurchaseOrder.Create(
            "org-001",
            "env-dev",
            "PO-001",
            "SUP-001",
            "SITE-01",
            [NewPurchaseOrderLine(quantity: 10m)]);

        Assert.Throws<ArgumentOutOfRangeException>(() => PurchaseReceipt.Record(
            order,
            "RCV-001",
            [new PurchaseReceiptLineDraft("LINE-001", 11m, "accepted")]));
    }

    [Fact]
    public void Purchase_receipt_records_event_and_is_immutable()
    {
        var order = PurchaseOrder.Create(
            "org-001",
            "env-dev",
            "PO-001",
            "SUP-001",
            "SITE-01",
            [NewPurchaseOrderLine(quantity: 10m)]);
        order.ClearDomainEvents();

        var receipt = PurchaseReceipt.Record(
            order,
            "RCV-001",
            [new PurchaseReceiptLineDraft("LINE-001", 4m, "accepted")]);

        Assert.Equal(PurchaseReceiptStatus.Recorded, receipt.Status);
        Assert.Equal(4m, order.Lines.Single().ReceivedQuantity);
        Assert.Contains(receipt.GetDomainEvents(), domainEvent => domainEvent is PurchaseReceiptRecordedDomainEvent);
        Assert.Single(receipt.GetDomainEvents().OfType<PurchaseReceiptInventoryMovementRequestedDomainEvent>());
        Assert.Throws<InvalidOperationException>(() => receipt.Cancel());
    }

    [Fact]
    public void Purchase_receipt_lines_keep_inventory_posting_dimensions_from_purchase_order()
    {
        var order = PurchaseOrder.Create(
            "org-001",
            "env-dev",
            "PO-001",
            "SUP-001",
            "SITE-01",
            [NewPurchaseOrderLine(quantity: 10m)]);

        var receipt = PurchaseReceipt.Record(
            order,
            "RCV-001",
            [new PurchaseReceiptLineDraft("LINE-001", 4m, "accepted", "RAW-A-01", "LOT-001")]);

        var line = Assert.Single(receipt.Lines);
        Assert.Equal("SKU-RM-1000", line.SkuCode);
        Assert.Equal("kg", line.UomCode);
        Assert.Equal("RAW-A-01", line.LocationCode);
        Assert.Equal("LOT-001", line.LotNo);
    }

    [Fact]
    public void Supplier_invoice_three_way_match_requires_po_receipt_and_invoice_to_align()
    {
        var order = PurchaseOrder.Create(
            "org-001",
            "env-dev",
            "PO-001",
            "SUP-001",
            "SITE-01",
            [NewPurchaseOrderLine(quantity: 10m)]);
        var receipt = PurchaseReceipt.Record(
            order,
            "RCV-001",
            [new PurchaseReceiptLineDraft("LINE-001", 4m, "accepted", "RAW-A-01", "LOT-001")]);

        var invoice = SupplierInvoice.Match(
            order,
            receipt,
            "INV-001",
            new DateOnly(2026, 6, 10),
            new DateOnly(2026, 7, 10),
            "CNY",
            quantityTolerance: 0m,
            amountTolerance: 0m,
            [new SupplierInvoiceLineDraft("LINE-001", "LINE-001", 4m, 12.5m)]);

        Assert.Equal(SupplierInvoiceMatchStatus.Matched, invoice.MatchStatus);
        Assert.Equal(50m, invoice.TotalAmount);
        Assert.Single(invoice.GetDomainEvents());
        Assert.IsType<SupplierInvoiceMatchedDomainEvent>(invoice.GetDomainEvents().Single());

        Assert.Throws<InvalidOperationException>(() => SupplierInvoice.Match(
            order,
            receipt,
            "INV-002",
            new DateOnly(2026, 6, 10),
            new DateOnly(2026, 7, 10),
            "CNY",
            quantityTolerance: 0m,
            amountTolerance: 0m,
            [new SupplierInvoiceLineDraft("LINE-001", "LINE-001", 4.1m, 12.5m)]));

        var multiLineOrder = PurchaseOrder.Create(
            "org-001",
            "env-dev",
            "PO-002",
            "SUP-001",
            "SITE-01",
            [
                new PurchaseOrderLineDraft("LINE-001", "SKU-RM-1000", "kg", 10m, 12.5m, new DateOnly(2026, 6, 3)),
                new PurchaseOrderLineDraft("LINE-002", "SKU-RM-2000", "kg", 10m, 20m, new DateOnly(2026, 6, 3)),
            ]);
        var multiLineReceipt = PurchaseReceipt.Record(
            multiLineOrder,
            "RCV-002",
            [
                new PurchaseReceiptLineDraft("LINE-001", 4m, "accepted", "RAW-A-01", "LOT-001"),
                new PurchaseReceiptLineDraft("LINE-002", 4m, "accepted", "RAW-A-02", "LOT-002"),
            ]);

        Assert.Throws<InvalidOperationException>(() => SupplierInvoice.Match(
            multiLineOrder,
            multiLineReceipt,
            "INV-003",
            new DateOnly(2026, 6, 10),
            new DateOnly(2026, 7, 10),
            "CNY",
            quantityTolerance: 0m,
            amountTolerance: 0m,
            [new SupplierInvoiceLineDraft("LINE-001", "LINE-002", 4m, 12.5m)]));
    }

    private static RfqLineDraft NewRfqLine()
    {
        return new RfqLineDraft("LINE-001", "SKU-RM-1000", "kg", 25m, "SITE-01", new DateOnly(2026, 6, 1));
    }

    private static PurchaseOrderLineDraft NewPurchaseOrderLine(decimal quantity)
    {
        return new PurchaseOrderLineDraft("LINE-001", "SKU-RM-1000", "kg", quantity, 12.5m, new DateOnly(2026, 6, 3));
    }
}
