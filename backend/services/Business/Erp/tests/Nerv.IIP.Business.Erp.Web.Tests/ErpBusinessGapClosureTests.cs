using Nerv.IIP.Business.Erp.Domain.AggregatesModel.DeliveryOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseReceiptAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.QuotationAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SalesOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SupplierInvoiceAggregate;
using Nerv.IIP.Business.Erp.Domain.DomainEvents;
using Nerv.IIP.Business.Erp.Web.Application.Approval;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Finance;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Procurement;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Sales;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Erp.Web.Application.Queries.SalesFinance;
using Nerv.IIP.Contracts.Approval;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Contracts.Wms;
using Microsoft.Extensions.DependencyInjection;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Erp.Web.Tests;

public sealed class ErpBusinessGapClosureTests
{
    [Fact]
    public void Purchase_receipt_converter_publishes_inventory_movement_requested_contract()
    {
        var order = PurchaseOrder.Create(
            "org-001",
            "env-dev",
            "PO-001",
            "SUP-001",
            "SITE-01",
            [
                new PurchaseOrderLineDraft("LINE-001", "SKU-RM-1000", "kg", 5m, 12.5m, new DateOnly(2026, 6, 5)),
                new PurchaseOrderLineDraft("LINE-002", "SKU-RM-2000", "kg", 7m, 8m, new DateOnly(2026, 6, 5)),
            ]);
        order.MarkApprovalRequested("approval-chain-001");
        order.ReleaseAfterApproval("approval-chain-001");
        var receipt = PurchaseReceipt.Record(
            order,
            "RCV-001",
            [
                new PurchaseReceiptLineDraft("LINE-001", 2m, "accepted", "RAW-A-01", "LOT-001"),
                new PurchaseReceiptLineDraft("LINE-002", 3m, "accepted", "RAW-A-02", "LOT-002"),
            ]);

        var movementEvents = receipt.GetDomainEvents()
            .OfType<PurchaseReceiptInventoryMovementRequestedDomainEvent>()
            .OrderBy(x => x.Line.PurchaseOrderLineNo, StringComparer.Ordinal)
            .ToArray();

        var integrationEvent = new PurchaseReceiptInventoryMovementRequestedIntegrationEventConverter()
            .Convert(movementEvents[0]);

        Assert.Equal(2, movementEvents.Length);
        Assert.Equal(InventoryIntegrationEventTypes.InventoryMovementRequested, integrationEvent.EventType);
        Assert.Equal(InventoryIntegrationEventSources.BusinessErp, integrationEvent.SourceService);
        Assert.Equal("purchase-receipt", integrationEvent.Payload.MovementType);
        Assert.Equal("RCV-001", integrationEvent.Payload.SourceDocumentId);
        Assert.Equal("LINE-001", integrationEvent.Payload.SourceDocumentLineId);
        Assert.Equal("SKU-RM-1000", integrationEvent.Payload.SkuCode);
        Assert.Equal("kg", integrationEvent.Payload.UomCode);
        Assert.Equal("SITE-01", integrationEvent.Payload.SiteCode);
        Assert.Equal("RAW-A-01", integrationEvent.Payload.LocationCode);
        Assert.Equal(2m, integrationEvent.Payload.Quantity);
    }

    [Fact]
    public void Delivery_order_converter_publishes_wms_outbound_order_requested_contract()
    {
        var quotation = Quotation.Create(
            "org-001",
            "env-dev",
            "QT-001",
            "CUST-001",
            new DateOnly(2026, 7, 1),
            [new QuotationLineDraft("LINE-001", "SKU-FG-1000", "ea", 3m, 20m, new DateOnly(2026, 7, 15))]);
        quotation.Approve();
        var salesOrder = SalesOrder.CreateFromQuotation("SO-001", quotation);
        var delivery = DeliveryOrder.Release(
            salesOrder,
            "DO-001",
            [new DeliveryOrderLineDraft("LINE-001", 2m, "FG-SHIP", "LOT-FG-001")]);

        var integrationEvent = new DeliveryOrderOutboundOrderRequestedIntegrationEventConverter()
            .Convert(new DeliveryOrderReleasedDomainEvent(delivery));

        Assert.Equal(WmsIntegrationEventTypes.OutboundOrderRequested, integrationEvent.EventType);
        Assert.Equal(WmsIntegrationEventSources.BusinessErp, integrationEvent.SourceService);
        Assert.Equal("DO-001", integrationEvent.Payload.DeliveryOrderNo);
        var line = Assert.Single(integrationEvent.Payload.Lines);
        Assert.Equal("LINE-001", line.SourceLineNo);
        Assert.Equal("SKU-FG-1000", line.SkuCode);
        Assert.Equal("ea", line.UomCode);
        Assert.Equal("FG-SHIP", line.LocationCode);
        Assert.Equal(2m, line.Quantity);
    }

    [Fact]
    public async Task Supplier_invoice_command_creates_matched_invoice_payable_and_subledger_voucher()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var approvalClient = new CapturingPurchaseOrderApprovalClient();
        await new CreatePurchaseOrderCommandHandler(dbContext, approvalClient: approvalClient).Handle(
            new CreatePurchaseOrderCommand(
                "org-001",
                "env-dev",
                "PO-001",
                "SUP-001",
                "SITE-01",
                [new PurchaseOrderCommandLine("LINE-001", "SKU-RM-1000", "kg", 5m, 12.5m, new DateOnly(2026, 6, 5))]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new ApprovalCompletedIntegrationEventHandlerForReleasePurchaseOrder(dbContext).HandleAsync(
            ApprovedPurchaseOrderEvent("PO-001", approvalClient.LastRequest!.ChainId),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new RecordPurchaseReceiptCommandHandler(dbContext).Handle(
            new RecordPurchaseReceiptCommand(
                "org-001",
                "env-dev",
                "RCV-001",
                "PO-001",
                [new PurchaseReceiptCommandLine("LINE-001", 2m, "accepted", "RAW-A-01", "LOT-001")]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var invoiceId = await new RecordSupplierInvoiceCommandHandler(dbContext).Handle(
            new RecordSupplierInvoiceCommand(
                "org-001",
                "env-dev",
                "INV-001",
                "PO-001",
                "RCV-001",
                new DateOnly(2026, 6, 10),
                new DateOnly(2026, 7, 10),
                "CNY",
                0m,
                0m,
                [new SupplierInvoiceCommandLine("LINE-001", "LINE-001", 2m, 12.5m)]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.NotNull(invoiceId);
        var payable = Assert.Single(dbContext.AccountPayables);
        Assert.Equal("INV-001", payable.SourceDocumentNo);
        Assert.NotEqual("AP-INV-001", payable.PayableNo);
        Assert.Equal(25m, payable.Amount);
        Assert.Equal(new DateOnly(2026, 7, 10), payable.DueDate);
        Assert.Single(dbContext.JournalVouchers);
    }

    [Fact]
    public async Task Supplier_invoice_command_holds_payment_when_cumulative_invoice_quantity_exceeds_receipt()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var approvalClient = new CapturingPurchaseOrderApprovalClient();
        await new CreatePurchaseOrderCommandHandler(dbContext, approvalClient: approvalClient).Handle(
            new CreatePurchaseOrderCommand(
                "org-001",
                "env-dev",
                "PO-001",
                "SUP-001",
                "SITE-01",
                [new PurchaseOrderCommandLine("LINE-001", "SKU-RM-1000", "kg", 5m, 12.5m, new DateOnly(2026, 6, 5))]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new ApprovalCompletedIntegrationEventHandlerForReleasePurchaseOrder(dbContext).HandleAsync(
            ApprovedPurchaseOrderEvent("PO-001", approvalClient.LastRequest!.ChainId),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new RecordPurchaseReceiptCommandHandler(dbContext).Handle(
            new RecordPurchaseReceiptCommand(
                "org-001",
                "env-dev",
                "RCV-001",
                "PO-001",
                [new PurchaseReceiptCommandLine("LINE-001", 2m, "accepted", "RAW-A-01", "LOT-001")]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordSupplierInvoiceCommandHandler(dbContext);
        await handler.Handle(
            new RecordSupplierInvoiceCommand(
                "org-001",
                "env-dev",
                "INV-001",
                "PO-001",
                "RCV-001",
                new DateOnly(2026, 6, 10),
                new DateOnly(2026, 7, 10),
                "CNY",
                0m,
                0m,
                [new SupplierInvoiceCommandLine("LINE-001", "LINE-001", 2m, 12.5m)]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await handler.Handle(
            new RecordSupplierInvoiceCommand(
                "org-001",
                "env-dev",
                "INV-002",
                "PO-001",
                "RCV-001",
                new DateOnly(2026, 6, 11),
                new DateOnly(2026, 7, 11),
                "CNY",
                0m,
                0m,
                [new SupplierInvoiceCommandLine("LINE-001", "LINE-001", 0.1m, 12.5m)]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var heldInvoice = dbContext.SupplierInvoices.Single(x => x.InvoiceNo == "INV-002");
        Assert.Equal(SupplierInvoiceMatchStatus.PaymentHeld, heldInvoice.MatchStatus);
        Assert.Single(dbContext.AccountPayables);
        Assert.Single(dbContext.JournalVouchers);
    }

    [Fact]
    public async Task Purchase_order_creation_starts_business_approval_and_blocks_receipts_until_approved()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var approvalClient = new CapturingPurchaseOrderApprovalClient();

        await new CreatePurchaseOrderCommandHandler(dbContext, approvalClient: approvalClient).Handle(
            new CreatePurchaseOrderCommand(
                "org-001",
                "env-dev",
                "PO-001",
                "SUP-001",
                "SITE-01",
                [new PurchaseOrderCommandLine("LINE-001", "SKU-RM-1000", "kg", 5m, 12.5m, new DateOnly(2026, 6, 5))],
                "po-idem-001"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var order = dbContext.PurchaseOrders.Single();
        Assert.Equal(PurchaseOrderStatus.PendingApproval, order.Status);
        Assert.Equal("PO-001", approvalClient.LastRequest!.DocumentId);
        Assert.Equal("purchase-order", approvalClient.LastRequest.DocumentType);
        Assert.Equal("business-erp", approvalClient.LastRequest.SourceService);
        await Assert.ThrowsAsync<KnownException>(() => new RecordPurchaseReceiptCommandHandler(dbContext).Handle(
            new RecordPurchaseReceiptCommand(
                "org-001",
                "env-dev",
                "RCV-001",
                "PO-001",
                [new PurchaseReceiptCommandLine("LINE-001", 1m, "accepted")]),
            CancellationToken.None));

        await new ApprovalCompletedIntegrationEventHandlerForReleasePurchaseOrder(dbContext).HandleAsync(
            ApprovedPurchaseOrderEvent("PO-001", approvalClient.LastRequest.ChainId),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(PurchaseOrderStatus.Released, dbContext.PurchaseOrders.Single().Status);
        await new RecordPurchaseReceiptCommandHandler(dbContext).Handle(
            new RecordPurchaseReceiptCommand(
                "org-001",
                "env-dev",
                "RCV-001",
                "PO-001",
                [new PurchaseReceiptCommandLine("LINE-001", 1m, "accepted")]),
            CancellationToken.None);
    }

    [Fact]
    public async Task Held_supplier_invoice_can_be_released_or_voided()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var approvalClient = new CapturingPurchaseOrderApprovalClient();
        await new CreatePurchaseOrderCommandHandler(dbContext, approvalClient: approvalClient).Handle(
            new CreatePurchaseOrderCommand(
                "org-001",
                "env-dev",
                "PO-001",
                "SUP-001",
                "SITE-01",
                [new PurchaseOrderCommandLine("LINE-001", "SKU-RM-1000", "kg", 5m, 12.5m, new DateOnly(2026, 6, 5))]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new ApprovalCompletedIntegrationEventHandlerForReleasePurchaseOrder(dbContext).HandleAsync(
            ApprovedPurchaseOrderEvent("PO-001", approvalClient.LastRequest!.ChainId),
            CancellationToken.None);
        await new RecordPurchaseReceiptCommandHandler(dbContext).Handle(
            new RecordPurchaseReceiptCommand(
                "org-001",
                "env-dev",
                "RCV-001",
                "PO-001",
                [new PurchaseReceiptCommandLine("LINE-001", 2m, "accepted", "RAW-A-01", "LOT-001")]),
            CancellationToken.None);
        await new RecordPurchaseReceiptCommandHandler(dbContext).Handle(
            new RecordPurchaseReceiptCommand(
                "org-001",
                "env-dev",
                "RCV-002",
                "PO-001",
                [new PurchaseReceiptCommandLine("LINE-001", 2m, "accepted", "RAW-A-02", "LOT-002")]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordSupplierInvoiceCommandHandler(dbContext);
        await handler.Handle(
            new RecordSupplierInvoiceCommand(
                "org-001",
                "env-dev",
                "INV-HELD-RELEASE",
                "PO-001",
                "RCV-001",
                new DateOnly(2026, 6, 10),
                new DateOnly(2026, 7, 10),
                "CNY",
                0m,
                0m,
                [new SupplierInvoiceCommandLine("LINE-001", "LINE-001", 2.1m, 12.5m)]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await new ReleaseSupplierInvoicePaymentHoldCommandHandler(dbContext).Handle(
            new ReleaseSupplierInvoicePaymentHoldCommand("org-001", "env-dev", "INV-HELD-RELEASE", null, "review-release-001"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(SupplierInvoiceMatchStatus.Matched, dbContext.SupplierInvoices.Single(x => x.InvoiceNo == "INV-HELD-RELEASE").MatchStatus);
        Assert.Single(dbContext.AccountPayables);
        Assert.Single(dbContext.JournalVouchers);

        await handler.Handle(
            new RecordSupplierInvoiceCommand(
                "org-001",
                "env-dev",
                "INV-HELD-VOID",
                "PO-001",
                "RCV-002",
                new DateOnly(2026, 6, 11),
                new DateOnly(2026, 7, 11),
                "CNY",
                0m,
                0m,
                [new SupplierInvoiceCommandLine("LINE-001", "LINE-001", 2.1m, 12.5m)]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new VoidSupplierInvoicePaymentHoldCommandHandler(dbContext).Handle(
            new VoidSupplierInvoicePaymentHoldCommand("org-001", "env-dev", "INV-HELD-VOID"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(SupplierInvoiceMatchStatus.Voided, dbContext.SupplierInvoices.Single(x => x.InvoiceNo == "INV-HELD-VOID").MatchStatus);
        await handler.Handle(
            new RecordSupplierInvoiceCommand(
                "org-001",
                "env-dev",
                "INV-AFTER-VOID",
                "PO-001",
                "RCV-002",
                new DateOnly(2026, 6, 12),
                new DateOnly(2026, 7, 12),
                "CNY",
                0m,
                0m,
                [new SupplierInvoiceCommandLine("LINE-001", "LINE-001", 2m, 12.5m)]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(SupplierInvoiceMatchStatus.Matched, dbContext.SupplierInvoices.Single(x => x.InvoiceNo == "INV-AFTER-VOID").MatchStatus);
    }

    [Fact]
    public async Task Finance_clearing_commands_update_open_items_and_post_balanced_vouchers()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        await new CreateAccountPayableCommandHandler(dbContext).Handle(
            new CreateAccountPayableCommand("org-001", "env-dev", "AP-001", "INV-001", "SUP-001", 100m, "CNY", new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 1), "NET30"),
            CancellationToken.None);
        await new CreateAccountReceivableCommandHandler(dbContext).Handle(
            new CreateAccountReceivableCommand("org-001", "env-dev", "AR-001", "DO-001", "CUS-001", 80m, "CNY", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 15), "NET14"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var payablePaymentHandler = new RegisterAccountPayablePaymentCommandHandler(dbContext);
        var receivableCollectionHandler = new RegisterAccountReceivableCollectionCommandHandler(dbContext);
        await payablePaymentHandler.Handle(
            new RegisterAccountPayablePaymentCommand("org-001", "env-dev", "AP-001", 40m, new DateOnly(2026, 6, 20), "BANK-001", "idem-ap-payment-001"),
            CancellationToken.None);
        await receivableCollectionHandler.Handle(
            new RegisterAccountReceivableCollectionCommand("org-001", "env-dev", "AR-001", 35m, new DateOnly(2026, 6, 20), "BANK-001", "idem-ar-collection-001"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await payablePaymentHandler.Handle(
            new RegisterAccountPayablePaymentCommand("org-001", "env-dev", "AP-001", 40m, new DateOnly(2026, 6, 20), "BANK-001", "idem-ap-payment-001"),
            CancellationToken.None);
        await receivableCollectionHandler.Handle(
            new RegisterAccountReceivableCollectionCommand("org-001", "env-dev", "AR-001", 35m, new DateOnly(2026, 6, 20), "BANK-001", "idem-ar-collection-001"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(60m, dbContext.AccountPayables.Single().OpenAmount);
        Assert.Equal(45m, dbContext.AccountReceivables.Single().OpenAmount);
        Assert.Equal(4, dbContext.JournalVouchers.Count());

        var payables = await new ListAccountPayablesQueryHandler(dbContext).Handle(
            new ListAccountPayablesQuery("org-001", "env-dev", "open", null, 0, 10, new DateOnly(2026, 7, 20)),
            CancellationToken.None);
        Assert.Equal("1-30", Assert.Single(payables.Items).AgingBucket);
    }

    [Fact]
    public async Task Sales_order_command_applies_credit_check_against_open_ar_and_active_order_exposure()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        await new CreateAccountReceivableCommandHandler(dbContext).Handle(
            new CreateAccountReceivableCommand("org-001", "env-dev", "AR-001", "DO-OLD", "CUST-001", 90m, "CNY"),
            CancellationToken.None);
        await new CreateQuotationCommandHandler(dbContext).Handle(
            new CreateQuotationCommand(
                "org-001",
                "env-dev",
                "QT-001",
                "CUST-001",
                new DateOnly(2026, 12, 31),
                [new QuotationCommandLine("LINE-001", "SKU-FG", "EA", 2m, 20m, new DateOnly(2026, 7, 1))]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new ApproveQuotationCommandHandler(dbContext).Handle(
            new ApproveQuotationCommand("org-001", "env-dev", "QT-001"),
            CancellationToken.None);

        await Assert.ThrowsAsync<KnownException>(() => new CreateSalesOrderCommandHandler(dbContext).Handle(
            new CreateSalesOrderCommand("org-001", "env-dev", "SO-001", "QT-001", CustomerCreditLimit: 100m),
            CancellationToken.None));
    }

    private static ApprovalCompletedIntegrationEvent ApprovedPurchaseOrderEvent(string purchaseOrderNo, string chainId)
    {
        return new ApprovalCompletedIntegrationEvent(
            "evt-approval-approved-001",
            ApprovalIntegrationEventTypes.ApprovalApproved,
            ApprovalIntegrationEventVersions.V1,
            DateTimeOffset.UtcNow,
            ApprovalIntegrationEventSources.BusinessApproval,
            "corr-001",
            "cause-001",
            "org-001",
            "env-dev",
            "system:approval",
            $"business-approval:approved:org-001:env-dev:{chainId}",
            new ApprovalCompletedPayload(
                chainId,
                ApprovalResults.Approved,
                "user",
                "u-manager",
                null,
                null,
                new ApprovalDocumentReferencePayload("business-erp", "purchase-order", purchaseOrderNo, null)));
    }

    private sealed class CapturingPurchaseOrderApprovalClient : IPurchaseOrderApprovalClient
    {
        public PurchaseOrderApprovalRequest? LastRequest { get; private set; }

        public Task<PurchaseOrderApprovalResult> StartApprovalAsync(PurchaseOrderApprovalRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new PurchaseOrderApprovalResult(request.ChainId));
        }
    }

    [Fact]
    public async Task Sales_order_credit_exposure_counts_only_open_sales_order_quantity()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        await new CreateQuotationCommandHandler(dbContext).Handle(
            new CreateQuotationCommand(
                "org-001",
                "env-dev",
                "QT-001",
                "CUST-001",
                new DateOnly(2026, 12, 31),
                [new QuotationCommandLine("LINE-001", "SKU-FG", "EA", 2m, 20m, new DateOnly(2026, 7, 1))]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new ApproveQuotationCommandHandler(dbContext).Handle(
            new ApproveQuotationCommand("org-001", "env-dev", "QT-001"),
            CancellationToken.None);
        await new CreateSalesOrderCommandHandler(dbContext).Handle(
            new CreateSalesOrderCommand("org-001", "env-dev", "SO-001", "QT-001", CustomerCreditLimit: 100m),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new ReleaseDeliveryOrderCommandHandler(dbContext).Handle(
            new ReleaseDeliveryOrderCommand(
                "org-001",
                "env-dev",
                "DO-001",
                "SO-001",
                [new DeliveryOrderCommandLine("LINE-001", 1m, "FG-SHIP", "LOT-FG-001")]),
            CancellationToken.None);
        await new CreateQuotationCommandHandler(dbContext).Handle(
            new CreateQuotationCommand(
                "org-001",
                "env-dev",
                "QT-002",
                "CUST-001",
                new DateOnly(2026, 12, 31),
                [new QuotationCommandLine("LINE-001", "SKU-FG", "EA", 1m, 40m, new DateOnly(2026, 7, 1))]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new ApproveQuotationCommandHandler(dbContext).Handle(
            new ApproveQuotationCommand("org-001", "env-dev", "QT-002"),
            CancellationToken.None);

        var salesOrderId = await new CreateSalesOrderCommandHandler(dbContext).Handle(
            new CreateSalesOrderCommand("org-001", "env-dev", "SO-002", "QT-002", CustomerCreditLimit: 60m),
            CancellationToken.None);

        Assert.NotNull(salesOrderId);
    }
}
