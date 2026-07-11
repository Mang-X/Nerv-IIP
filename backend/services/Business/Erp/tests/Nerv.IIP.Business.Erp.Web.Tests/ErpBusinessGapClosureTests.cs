using Nerv.IIP.Business.Erp.Domain.AggregatesModel.DeliveryOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.CashReceiptAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PaymentExecutionAggregate;
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
using Nerv.IIP.Business.Erp.Web.Application.MasterData;
using Nerv.IIP.Business.Erp.Web.Application.Queries.SalesFinance;
using Nerv.IIP.Contracts.Approval;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Contracts.Wms;
using Nerv.IIP.Messaging.CAP;
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
        Assert.Equal("inbound", integrationEvent.Payload.MovementType);
        Assert.Equal("RCV-001", integrationEvent.Payload.SourceDocumentId);
        Assert.Equal("LINE-001", integrationEvent.Payload.SourceDocumentLineId);
        Assert.Equal("SKU-RM-1000", integrationEvent.Payload.SkuCode);
        Assert.Equal("kg", integrationEvent.Payload.UomCode);
        Assert.Equal("SITE-01", integrationEvent.Payload.SiteCode);
        Assert.Equal("RAW-A-01", integrationEvent.Payload.LocationCode);
        Assert.Equal("unrestricted", integrationEvent.Payload.QualityStatus);
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
            FutureDate(30),
            [new QuotationLineDraft("LINE-001", "SKU-FG-1000", "ea", 3m, 20m, FutureDate(45))]);
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
        await new ApprovalCompletedIntegrationEventHandlerForReleasePurchaseOrder(dbContext, new InMemoryIntegrationEventDeadLetterStore()).HandleAsync(
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
    public async Task Purchase_receipt_then_supplier_invoice_clears_gr_ir_without_duplicate_payable()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var approvalClient = new CapturingPurchaseOrderApprovalClient();
        await new CreatePurchaseOrderCommandHandler(dbContext, approvalClient: approvalClient).Handle(
            new CreatePurchaseOrderCommand(
                "org-001",
                "env-dev",
                "PO-GRIR-001",
                "SUP-001",
                "SITE-01",
                [new PurchaseOrderCommandLine("LINE-001", "SKU-RM-1000", "kg", 5m, 12.5m, new DateOnly(2026, 6, 5))]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new ApprovalCompletedIntegrationEventHandlerForReleasePurchaseOrder(dbContext, new InMemoryIntegrationEventDeadLetterStore()).HandleAsync(
            ApprovedPurchaseOrderEvent("PO-GRIR-001", approvalClient.LastRequest!.ChainId),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new RecordPurchaseReceiptCommandHandler(dbContext).Handle(
            new RecordPurchaseReceiptCommand(
                "org-001",
                "env-dev",
                "RCV-GRIR-001",
                "PO-GRIR-001",
                [new PurchaseReceiptCommandLine("LINE-001", 2m, "accepted", "RAW-A-01", "LOT-001")]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var receipt = dbContext.PurchaseReceipts.Single(x => x.PurchaseReceiptNo == "RCV-GRIR-001");
        var receiptEvent = new PurchaseReceiptRecordedIntegrationEventConverter()
            .Convert(new PurchaseReceiptRecordedDomainEvent(receipt));
        await new PurchaseReceiptRecordedIntegrationEventHandlerForPostGrIrAccrual(
            dbContext,
            new InMemoryIntegrationEventDeadLetterStore())
            .HandleAsync(receiptEvent, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Empty(dbContext.AccountPayables);
        Assert.Equal(25m, AccountBalance(dbContext, "1401"));
        Assert.Equal(-25m, AccountBalance(dbContext, "GR-IR"));
        Assert.Equal(0m, AccountBalance(dbContext, "2202"));

        await new RecordSupplierInvoiceCommandHandler(dbContext).Handle(
            new RecordSupplierInvoiceCommand(
                "org-001",
                "env-dev",
                "INV-GRIR-001",
                "PO-GRIR-001",
                "RCV-GRIR-001",
                new DateOnly(2026, 6, 10),
                new DateOnly(2026, 7, 10),
                "CNY",
                0m,
                0m,
                [new SupplierInvoiceCommandLine("LINE-001", "LINE-001", 2m, 12.5m)]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var payable = Assert.Single(dbContext.AccountPayables);
        Assert.Equal("INV-GRIR-001", payable.SourceDocumentNo);
        Assert.Equal(25m, payable.Amount);
        Assert.Equal(2, dbContext.JournalVouchers.Count());
        Assert.Equal(25m, AccountBalance(dbContext, "1401"));
        Assert.Equal(0m, AccountBalance(dbContext, "GR-IR"));
        Assert.Equal(-25m, AccountBalance(dbContext, "2202"));
    }

    [Fact]
    public async Task Foreign_currency_purchase_receipt_and_supplier_invoice_post_local_amounts_with_exchange_rate()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var approvalClient = new CapturingPurchaseOrderApprovalClient();
        await new CreatePurchaseOrderCommandHandler(dbContext, approvalClient: approvalClient).Handle(
            new CreatePurchaseOrderCommand(
                "org-001",
                "env-dev",
                "PO-FX-001",
                "SUP-001",
                "SITE-01",
                [new PurchaseOrderCommandLine("LINE-001", "SKU-RM-1000", "kg", 5m, 12.5m, new DateOnly(2026, 6, 5))],
                CurrencyCode: "USD"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new ApprovalCompletedIntegrationEventHandlerForReleasePurchaseOrder(dbContext, new InMemoryIntegrationEventDeadLetterStore()).HandleAsync(
            ApprovedPurchaseOrderEvent("PO-FX-001", approvalClient.LastRequest!.ChainId),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new RecordPurchaseReceiptCommandHandler(dbContext).Handle(
            new RecordPurchaseReceiptCommand(
                "org-001",
                "env-dev",
                "RCV-FX-001",
                "PO-FX-001",
                [new PurchaseReceiptCommandLine("LINE-001", 2m, "accepted", "RAW-A-01", "LOT-001")],
                ExchangeRate: 7.1m),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var receipt = dbContext.PurchaseReceipts.Single(x => x.PurchaseReceiptNo == "RCV-FX-001");
        await new PurchaseReceiptRecordedIntegrationEventHandlerForPostGrIrAccrual(
            dbContext,
            new InMemoryIntegrationEventDeadLetterStore())
            .HandleAsync(new PurchaseReceiptRecordedIntegrationEventConverter().Convert(new PurchaseReceiptRecordedDomainEvent(receipt)), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await new RecordSupplierInvoiceCommandHandler(dbContext).Handle(
            new RecordSupplierInvoiceCommand(
                "org-001",
                "env-dev",
                "INV-FX-001",
                "PO-FX-001",
                "RCV-FX-001",
                new DateOnly(2026, 6, 10),
                new DateOnly(2026, 7, 10),
                "USD",
                0m,
                0m,
                [new SupplierInvoiceCommandLine("LINE-001", "LINE-001", 2m, 12.5m)],
                ExchangeRate: 7.2m),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var payable = Assert.Single(dbContext.AccountPayables);
        Assert.Equal("USD", payable.CurrencyCode);
        Assert.Equal(7.2m, payable.ExchangeRate);
        Assert.Equal(25m, payable.Amount);
        Assert.Equal(180m, payable.LocalAmount);
        Assert.Equal(2, dbContext.JournalVouchers.Count());
        Assert.Contains(dbContext.JournalVouchers.SelectMany(x => x.Lines), line =>
            line.AccountCode == FinanceVoucherFactory.RealizedExchangeLossAccountCode
            && line.CurrencyCode == "CNY"
            && line.LocalDebitAmount == 2.5m);
        Assert.Equal(177.5m, LocalAccountBalance(dbContext, "1401"));
        Assert.Equal(0m, LocalAccountBalance(dbContext, "GR-IR"));
        Assert.Equal(-180m, LocalAccountBalance(dbContext, "2202"));
        Assert.Equal(2.5m, LocalAccountBalance(dbContext, FinanceVoucherFactory.RealizedExchangeLossAccountCode));
    }

    [Fact]
    public async Task Supplier_invoice_price_variance_leaves_gr_ir_balance_for_later_reconciliation()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var approvalClient = new CapturingPurchaseOrderApprovalClient();
        await new CreatePurchaseOrderCommandHandler(dbContext, approvalClient: approvalClient).Handle(
            new CreatePurchaseOrderCommand(
                "org-001",
                "env-dev",
                "PO-GRIR-VAR",
                "SUP-001",
                "SITE-01",
                [new PurchaseOrderCommandLine("LINE-001", "SKU-RM-1000", "kg", 5m, 12.5m, new DateOnly(2026, 6, 5))]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new ApprovalCompletedIntegrationEventHandlerForReleasePurchaseOrder(dbContext, new InMemoryIntegrationEventDeadLetterStore()).HandleAsync(
            ApprovedPurchaseOrderEvent("PO-GRIR-VAR", approvalClient.LastRequest!.ChainId),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new RecordPurchaseReceiptCommandHandler(dbContext).Handle(
            new RecordPurchaseReceiptCommand(
                "org-001",
                "env-dev",
                "RCV-GRIR-VAR",
                "PO-GRIR-VAR",
                [new PurchaseReceiptCommandLine("LINE-001", 2m, "accepted", "RAW-A-01", "LOT-001")]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var receipt = dbContext.PurchaseReceipts.Single(x => x.PurchaseReceiptNo == "RCV-GRIR-VAR");
        await new PurchaseReceiptRecordedIntegrationEventHandlerForPostGrIrAccrual(
            dbContext,
            new InMemoryIntegrationEventDeadLetterStore())
            .HandleAsync(new PurchaseReceiptRecordedIntegrationEventConverter().Convert(new PurchaseReceiptRecordedDomainEvent(receipt)), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await new RecordSupplierInvoiceCommandHandler(dbContext).Handle(
            new RecordSupplierInvoiceCommand(
                "org-001",
                "env-dev",
                "INV-GRIR-VAR",
                "PO-GRIR-VAR",
                "RCV-GRIR-VAR",
                new DateOnly(2026, 6, 10),
                new DateOnly(2026, 7, 10),
                "CNY",
                0m,
                1m,
                [new SupplierInvoiceCommandLine("LINE-001", "LINE-001", 2m, 13m)]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(26m, Assert.Single(dbContext.AccountPayables).Amount);
        Assert.Equal(25m, AccountBalance(dbContext, "1401"));
        Assert.Equal(1m, AccountBalance(dbContext, "GR-IR"));
        Assert.Equal(-26m, AccountBalance(dbContext, "2202"));
    }

    [Fact]
    public async Task Direct_account_payable_posts_expense_payable_without_touching_inventory_or_gr_ir()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();

        await new CreateAccountPayableCommandHandler(dbContext).Handle(
            new CreateAccountPayableCommand(
                "org-001",
                "env-dev",
                "AP-DIRECT-001",
                "MANUAL-AP-001",
                "SUP-001",
                100m,
                "CNY",
                new DateOnly(2026, 6, 1),
                new DateOnly(2026, 7, 1),
                "DIRECT"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(100m, Assert.Single(dbContext.AccountPayables).Amount);
        Assert.Equal(0m, AccountBalance(dbContext, "1401"));
        Assert.Equal(0m, AccountBalance(dbContext, "GR-IR"));
        Assert.Equal(100m, AccountBalance(dbContext, "5001"));
        Assert.Equal(-100m, AccountBalance(dbContext, "2202"));
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
        await new ApprovalCompletedIntegrationEventHandlerForReleasePurchaseOrder(dbContext, new InMemoryIntegrationEventDeadLetterStore()).HandleAsync(
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

        await new ApprovalCompletedIntegrationEventHandlerForReleasePurchaseOrder(dbContext, new InMemoryIntegrationEventDeadLetterStore()).HandleAsync(
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
        await new ApprovalCompletedIntegrationEventHandlerForReleasePurchaseOrder(dbContext, new InMemoryIntegrationEventDeadLetterStore()).HandleAsync(
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
    public async Task Closed_accounting_period_blocks_vouchers_until_reopened()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();

        await new OpenAccountingPeriodCommandHandler(dbContext).Handle(
            new OpenAccountingPeriodCommand("org-001", "env-dev", "2026-06", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30)),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new CloseAccountingPeriodCommandHandler(dbContext).Handle(
            new CloseAccountingPeriodCommand("org-001", "env-dev", "2026-06", "u-controller", "month-end complete"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var postCommand = new PostJournalVoucherCommand(
            "org-001",
            "env-dev",
            "JV-CLOSED-001",
            new DateOnly(2026, 6, 20),
            [
                new JournalVoucherCommandLine("1401", 100m, 0m, "inventory"),
                new JournalVoucherCommandLine("2202", 0m, 100m, "payable"),
            ],
            IdempotencyKey: "idem-jv-closed-001");
        var exception = await Assert.ThrowsAsync<KnownException>(() => new PostJournalVoucherCommandHandler(dbContext).Handle(postCommand, CancellationToken.None));

        Assert.Contains("closed accounting period", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(dbContext.JournalVouchers);

        await new ReopenAccountingPeriodCommandHandler(dbContext).Handle(
            new ReopenAccountingPeriodCommand("org-001", "env-dev", "2026-06", "u-controller", "late integration exception"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new PostJournalVoucherCommandHandler(dbContext).Handle(postCommand with { IdempotencyKey = "idem-jv-reopened-001" }, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Single(dbContext.JournalVouchers);
        Assert.Equal("late integration exception", dbContext.AccountingPeriods.Single().ReopenReason);
    }

    [Fact]
    public async Task Payment_execution_and_cash_receipt_persist_documents_and_update_aging()
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

        await new RegisterAccountPayablePaymentCommandHandler(dbContext).Handle(
            new RegisterAccountPayablePaymentCommand("org-001", "env-dev", "AP-001", 40m, new DateOnly(2026, 6, 20), "BANK-001", "idem-ap-payment-715"),
            CancellationToken.None);
        await new RegisterAccountReceivableCollectionCommandHandler(dbContext).Handle(
            new RegisterAccountReceivableCollectionCommand("org-001", "env-dev", "AR-001", 35m, new DateOnly(2026, 6, 20), "BANK-001", "idem-ar-collection-715"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var payment = Assert.Single(dbContext.PaymentExecutions);
        var receipt = Assert.Single(dbContext.CashReceipts);
        Assert.Equal(PaymentExecutionStatus.Executed, payment.Status);
        Assert.Equal(CashReceiptStatus.Matched, receipt.Status);
        Assert.Equal(40m, Assert.Single(payment.Allocations).Amount);
        Assert.Equal(35m, Assert.Single(receipt.Allocations).Amount);
        Assert.Equal(60m, dbContext.AccountPayables.Single().OpenAmount);
        Assert.Equal(45m, dbContext.AccountReceivables.Single().OpenAmount);

        var receivables = await new ListAccountReceivablesQueryHandler(dbContext).Handle(
            new ListAccountReceivablesQuery("org-001", "env-dev", "open", null, 0, 10, new DateOnly(2026, 7, 1)),
            CancellationToken.None);
        Assert.Equal("1-30", Assert.Single(receivables.Items).AgingBucket);
    }

    [Fact]
    public async Task Payment_execution_and_cash_receipt_two_stage_lifecycle_updates_aging_only_when_executed_or_matched()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        await new CreateAccountPayableCommandHandler(dbContext).Handle(
            new CreateAccountPayableCommand("org-001", "env-dev", "AP-2STAGE-001", "INV-2STAGE-001", "SUP-001", 100m, "CNY", new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 1), "NET30"),
            CancellationToken.None);
        await new CreateAccountReceivableCommandHandler(dbContext).Handle(
            new CreateAccountReceivableCommand("org-001", "env-dev", "AR-2STAGE-001", "DO-2STAGE-001", "CUS-001", 80m, "CNY", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 15), "NET14"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var paymentExecutionNo = await new ApprovePaymentExecutionCommandHandler(dbContext).Handle(
            new ApprovePaymentExecutionCommand("org-001", "env-dev", "AP-2STAGE-001", 40m, new DateOnly(2026, 6, 20), "BANK-001", "idem-ap-approve-715"),
            CancellationToken.None);
        var cashReceiptNo = await new RegisterCashReceiptCommandHandler(dbContext).Handle(
            new RegisterCashReceiptCommand("org-001", "env-dev", "AR-2STAGE-001", 35m, new DateOnly(2026, 6, 20), "BANK-001", "idem-ar-register-715"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(PaymentExecutionStatus.Approved, dbContext.PaymentExecutions.Single().Status);
        Assert.Equal(CashReceiptStatus.Registered, dbContext.CashReceipts.Single().Status);
        Assert.Equal(100m, dbContext.AccountPayables.Single().OpenAmount);
        Assert.Equal(80m, dbContext.AccountReceivables.Single().OpenAmount);
        Assert.DoesNotContain(dbContext.JournalVouchers, x => x.VoucherNo == paymentExecutionNo || x.VoucherNo == cashReceiptNo);

        await new ExecutePaymentExecutionCommandHandler(dbContext).Handle(
            new ExecutePaymentExecutionCommand("org-001", "env-dev", paymentExecutionNo, "u-finance"),
            CancellationToken.None);
        await new MatchCashReceiptCommandHandler(dbContext).Handle(
            new MatchCashReceiptCommand("org-001", "env-dev", cashReceiptNo),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(PaymentExecutionStatus.Executed, dbContext.PaymentExecutions.Single().Status);
        Assert.Equal(CashReceiptStatus.Matched, dbContext.CashReceipts.Single().Status);
        Assert.Equal(60m, dbContext.AccountPayables.Single().OpenAmount);
        Assert.Equal(45m, dbContext.AccountReceivables.Single().OpenAmount);
        Assert.Contains(dbContext.JournalVouchers, x => x.VoucherNo == paymentExecutionNo);
        Assert.Contains(dbContext.JournalVouchers, x => x.VoucherNo == cashReceiptNo);
    }

    [Fact]
    public async Task Cash_receipt_match_posts_foreign_currency_voucher_with_receivable_exchange_rate()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        await new CreateAccountReceivableCommandHandler(dbContext).Handle(
            new CreateAccountReceivableCommand("org-001", "env-dev", "AR-USD-COLLECT-001", "DO-USD-COLLECT-001", "CUS-001", 80m, "USD", new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 1), "NET30", ExchangeRate: 7.1m),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var cashReceiptNo = await new RegisterCashReceiptCommandHandler(dbContext).Handle(
            new RegisterCashReceiptCommand("org-001", "env-dev", "AR-USD-COLLECT-001", 35m, new DateOnly(2026, 6, 20), "BANK-USD", "idem-ar-usd-register-715"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await new MatchCashReceiptCommandHandler(dbContext).Handle(
            new MatchCashReceiptCommand("org-001", "env-dev", cashReceiptNo),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var voucher = dbContext.JournalVouchers.Single(x => x.VoucherNo == cashReceiptNo);
        Assert.Contains(voucher.Lines, x => x.AccountCode == "BANK-USD" && x.CurrencyCode == "USD" && x.ExchangeRate == 7.1m && x.LocalDebitAmount == 248.5m);
        Assert.Contains(voucher.Lines, x => x.AccountCode == "1122" && x.CurrencyCode == "USD" && x.ExchangeRate == 7.1m && x.LocalCreditAmount == 248.5m);
        Assert.Equal(248.5m, dbContext.AccountReceivables.Single().LocalCollectedAmount);
    }

    [Fact]
    public async Task Month_end_checklist_and_trial_balance_expose_minimum_close_read_model()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        await new CreateAccountPayableCommandHandler(dbContext).Handle(
            new CreateAccountPayableCommand("org-001", "env-dev", "AP-001", "INV-001", "SUP-001", 100m, "CNY", new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 1), "NET30"),
            CancellationToken.None);
        await new CreateAccountReceivableCommandHandler(dbContext).Handle(
            new CreateAccountReceivableCommand("org-001", "env-dev", "AR-001", "DO-001", "CUS-001", 80m, "CNY", new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 1), "NET30"),
            CancellationToken.None);
        await new PostJournalVoucherCommandHandler(dbContext).Handle(
            new PostJournalVoucherCommand(
                "org-001",
                "env-dev",
                "JV-GRIR-OPEN",
                new DateOnly(2026, 6, 15),
                [
                    new JournalVoucherCommandLine("1401", 25m, 0m, "inventory"),
                    new JournalVoucherCommandLine(FinanceVoucherFactory.GoodsReceiptInvoiceReceiptAccountCode, 0m, 25m, "open GR/IR"),
                ]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await new ApprovePaymentExecutionCommandHandler(dbContext).Handle(
            new ApprovePaymentExecutionCommand("org-001", "env-dev", "AP-001", 40m, new DateOnly(2026, 6, 20), "BANK-001", "idem-ap-checklist-715"),
            CancellationToken.None);
        await new RegisterCashReceiptCommandHandler(dbContext).Handle(
            new RegisterCashReceiptCommand("org-001", "env-dev", "AR-001", 35m, new DateOnly(2026, 6, 20), "BANK-001", "idem-ar-checklist-715"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var trialBalance = await new GetTrialBalanceQueryHandler(dbContext).Handle(
            new GetTrialBalanceQuery("org-001", "env-dev", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30)),
            CancellationToken.None);
        var checklist = await new GetMonthEndChecklistQueryHandler(dbContext).Handle(
            new GetMonthEndChecklistQuery("org-001", "env-dev", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30)),
            CancellationToken.None);

        Assert.True(trialBalance.IsBalanced);
        Assert.Contains(trialBalance.Lines, x => x.AccountCode == "1401" && x.LocalDebitAmount == 25m);
        Assert.Equal(25m, checklist.GrIrLocalBalance);
        Assert.Equal(3, checklist.PostedVoucherCount);
        Assert.Equal(2, checklist.UnpostedDocumentCount);
        Assert.Equal(0, checklist.UnmatchedSupplierInvoiceCount);
    }

    [Fact]
    public async Task Payable_payment_rejects_batch_allocations_that_span_multiple_suppliers()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        await new CreateAccountPayableCommandHandler(dbContext).Handle(
            new CreateAccountPayableCommand("org-001", "env-dev", "AP-SUP-001", "INV-SUP-001", "SUP-001", 100m, "CNY"),
            CancellationToken.None);
        await new CreateAccountPayableCommandHandler(dbContext).Handle(
            new CreateAccountPayableCommand("org-001", "env-dev", "AP-SUP-002", "INV-SUP-002", "SUP-002", 50m, "CNY"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var exception = await Assert.ThrowsAsync<KnownException>(() => new RegisterAccountPayablePaymentCommandHandler(dbContext).Handle(
            new RegisterAccountPayablePaymentCommand(
                "org-001",
                "env-dev",
                PayableNo: "",
                Amount: 150m,
                PaymentDate: new DateOnly(2026, 6, 20),
                CashAccountCode: "BANK-001",
                IdempotencyKey: "idem-ap-cross-supplier-715",
                Allocations:
                [
                    new PayablePaymentAllocationCommandLine("AP-SUP-001", 100m),
                    new PayablePaymentAllocationCommandLine("AP-SUP-002", 50m),
                ]),
            CancellationToken.None));

        Assert.Contains("only settle payables", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Payable_payment_allocates_multiple_invoices_posts_on_account_and_realized_fx()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        await new CreateAccountPayableCommandHandler(dbContext).Handle(
            new CreateAccountPayableCommand("org-001", "env-dev", "AP-USD-001", "INV-USD-001", "SUP-001", 100m, "USD", ExchangeRate: 7.1m),
            CancellationToken.None);
        await new CreateAccountPayableCommandHandler(dbContext).Handle(
            new CreateAccountPayableCommand("org-001", "env-dev", "AP-USD-002", "INV-USD-002", "SUP-001", 50m, "USD", ExchangeRate: 7.0m),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await new RegisterAccountPayablePaymentCommandHandler(dbContext).Handle(
            new RegisterAccountPayablePaymentCommand(
                "org-001",
                "env-dev",
                PayableNo: "",
                Amount: 160m,
                PaymentDate: new DateOnly(2026, 6, 20),
                CashAccountCode: "BANK-USD",
                IdempotencyKey: "idem-ap-batch-payment-001",
                PaymentCurrencyCode: "USD",
                PaymentExchangeRate: 7.2m,
                Allocations:
                [
                    new PayablePaymentAllocationCommandLine("AP-USD-001", 100m),
                    new PayablePaymentAllocationCommandLine("AP-USD-002", 50m),
                ]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.All(dbContext.AccountPayables, payable => Assert.Equal(0m, payable.OpenAmount));
        var voucher = dbContext.JournalVouchers
            .OrderByDescending(x => x.PostedAtUtc)
            .First();
        Assert.Equal(1_152m, voucher.Lines.Sum(x => x.LocalCreditAmount));
        Assert.Equal(1_152m, voucher.Lines.Sum(x => x.LocalDebitAmount));
        Assert.Contains(voucher.Lines, x => x.AccountCode == FinanceVoucherFactory.AccountsPayableAccountCode && x.Memo.Contains("AP-USD-001", StringComparison.Ordinal) && x.LocalDebitAmount == 710m);
        Assert.Contains(voucher.Lines, x => x.AccountCode == FinanceVoucherFactory.AccountsPayableAccountCode && x.Memo.Contains("AP-USD-002", StringComparison.Ordinal) && x.LocalDebitAmount == 350m);
        Assert.Contains(voucher.Lines, x => x.AccountCode == FinanceVoucherFactory.RealizedExchangeLossAccountCode && x.LocalDebitAmount == 20m);
        Assert.Contains(voucher.Lines, x => x.AccountCode == FinanceVoucherFactory.OnAccountPrepaymentAccountCode && x.LocalDebitAmount == 72m);
        Assert.Contains(voucher.Lines, x => x.AccountCode == "BANK-USD" && x.LocalCreditAmount == 1_152m);
    }

    [Fact]
    public async Task Finance_summary_exposes_currency_groups_instead_of_only_cross_currency_totals()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        await new CreateAccountPayableCommandHandler(dbContext).Handle(
            new CreateAccountPayableCommand("org-001", "env-dev", "AP-CNY-001", "INV-CNY-001", "SUP-001", 100m, "CNY"),
            CancellationToken.None);
        await new CreateAccountPayableCommandHandler(dbContext).Handle(
            new CreateAccountPayableCommand("org-001", "env-dev", "AP-USD-001", "INV-USD-001", "SUP-001", 10m, "USD", ExchangeRate: 7m),
            CancellationToken.None);
        await new CreateAccountReceivableCommandHandler(dbContext).Handle(
            new CreateAccountReceivableCommand("org-001", "env-dev", "AR-USD-001", "DO-USD-001", "CUST-001", 20m, "USD", ExchangeRate: 7m),
            CancellationToken.None);
        await new CreateCostCandidateCommandHandler(dbContext).Handle(
            new CreateCostCandidateCommand("org-001", "env-dev", "COST-USD-001", "production-report", "RPT-USD-001", 10m, "USD", ExchangeRate: 7m),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var summary = await new GetFinanceSummaryQueryHandler(dbContext).Handle(
            new GetFinanceSummaryQuery("org-001", "env-dev"),
            CancellationToken.None);

        Assert.Contains(summary.PayablesByCurrency, x => x.CurrencyCode == "CNY" && x.OpenAmount == 100m && x.LocalOpenAmount == 100m);
        Assert.Contains(summary.PayablesByCurrency, x => x.CurrencyCode == "USD" && x.OpenAmount == 10m && x.LocalOpenAmount == 70m);
        Assert.Contains(summary.ReceivablesByCurrency, x => x.CurrencyCode == "USD" && x.OpenAmount == 20m && x.LocalOpenAmount == 140m);
        Assert.Contains(summary.CostCandidatesByCurrency, x => x.CurrencyCode == "USD" && x.OpenAmount == 10m && x.LocalOpenAmount == 70m);
    }

    [Fact]
    public async Task Sales_order_command_places_credit_overrun_on_hold_and_release_command_unblocks_delivery()
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
                FutureDate(30),
                [new QuotationCommandLine("LINE-001", "SKU-FG", "EA", 2m, 20m, FutureDate(45))]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new ApproveQuotationCommandHandler(dbContext).Handle(
            new ApproveQuotationCommand("org-001", "env-dev", "QT-001"),
            CancellationToken.None);

        await new CreateSalesOrderCommandHandler(
            dbContext,
            new StaticCustomerCreditProfileReader(new CustomerCreditProfile("CUST-001", 100m, "CNY"))).Handle(
            new CreateSalesOrderCommand("org-001", "env-dev", "SO-001", "QT-001"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal("credit-held", dbContext.SalesOrders.Single().Status);
        await Assert.ThrowsAsync<KnownException>(() => new ReleaseDeliveryOrderCommandHandler(dbContext).Handle(
            new ReleaseDeliveryOrderCommand(
                "org-001",
                "env-dev",
                "DO-BLOCKED",
                "SO-001",
                [new DeliveryOrderCommandLine("LINE-001", 1m, "FG-SHIP", "LOT-FG-001")]),
            CancellationToken.None));

        await new ReleaseSalesOrderCreditHoldCommandHandler(dbContext, new CapturingPurchaseOrderApprovalClient()).Handle(
            new ReleaseSalesOrderCreditHoldCommand("org-001", "env-dev", "SO-001"),
            CancellationToken.None);
        Assert.Equal("credit-held", dbContext.SalesOrders.Single().Status);
        await new ApprovalCompletedIntegrationEventHandlerForReleasePurchaseOrder(dbContext, new InMemoryIntegrationEventDeadLetterStore()).HandleAsync(
            new ApprovalCompletedIntegrationEvent(
                "evt-sales-credit-approved-001",
                ApprovalIntegrationEventTypes.ApprovalApproved,
                ApprovalIntegrationEventVersions.V1,
                DateTimeOffset.UtcNow,
                ApprovalIntegrationEventSources.BusinessApproval,
                "chain-sales-credit-001",
                "decision-sales-credit-001",
                "org-001",
                "env-dev",
                "user:credit-manager",
                "sales-credit-approved:SO-001",
                new ApprovalCompletedPayload(
                    "chain-sales-credit-001",
                    ApprovalResults.Approved,
                    "user",
                    "credit-manager",
                    null,
                    null,
                    new ApprovalDocumentReferencePayload("business-erp", "sales-order-credit-release", "SO-001", null),
                    "user:sales-001")),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal("released", dbContext.SalesOrders.Single().Status);
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

    private static decimal AccountBalance(Infrastructure.ApplicationDbContext dbContext, string accountCode)
    {
        return dbContext.JournalVouchers
            .SelectMany(x => x.Lines)
            .Where(x => x.AccountCode == accountCode)
            .Sum(x => x.DebitAmount - x.CreditAmount);
    }

    private static decimal LocalAccountBalance(Infrastructure.ApplicationDbContext dbContext, string accountCode)
    {
        return dbContext.JournalVouchers
            .SelectMany(x => x.Lines)
            .Where(x => x.AccountCode == accountCode)
            .Sum(x => x.LocalDebitAmount - x.LocalCreditAmount);
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
                FutureDate(30),
                [new QuotationCommandLine("LINE-001", "SKU-FG", "EA", 2m, 20m, FutureDate(45))]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new ApproveQuotationCommandHandler(dbContext).Handle(
            new ApproveQuotationCommand("org-001", "env-dev", "QT-001"),
            CancellationToken.None);
        await new CreateSalesOrderCommandHandler(
            dbContext,
            new StaticCustomerCreditProfileReader(new CustomerCreditProfile("CUST-001", 100m, "CNY"))).Handle(
            new CreateSalesOrderCommand("org-001", "env-dev", "SO-001", "QT-001"),
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
                FutureDate(30),
                [new QuotationCommandLine("LINE-001", "SKU-FG", "EA", 1m, 40m, FutureDate(45))]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new ApproveQuotationCommandHandler(dbContext).Handle(
            new ApproveQuotationCommand("org-001", "env-dev", "QT-002"),
            CancellationToken.None);

        var salesOrderId = await new CreateSalesOrderCommandHandler(
            dbContext,
            new StaticCustomerCreditProfileReader(new CustomerCreditProfile("CUST-001", 60m, "CNY"))).Handle(
            new CreateSalesOrderCommand("org-001", "env-dev", "SO-002", "QT-002"),
            CancellationToken.None);

        Assert.NotNull(salesOrderId);
    }

    private static DateOnly FutureDate(int days)
    {
        return DateOnly.FromDateTime(DateTime.UtcNow.AddDays(days));
    }
}
