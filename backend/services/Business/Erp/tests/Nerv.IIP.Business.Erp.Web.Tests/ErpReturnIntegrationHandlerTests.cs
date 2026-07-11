using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountPayableAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountReceivableAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.CreditNoteAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseReceiptAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseReturnAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SalesReturnAuthorizationAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SupplierInvoiceAggregate;
using Nerv.IIP.Business.Erp.Web.Application.Commands;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Finance;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Sales;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Erp.Web.Application.MasterData;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Contracts.Wms;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Erp.Web.Tests;

public sealed class ErpReturnIntegrationHandlerTests
{
    [Fact]
    public async Task Rma_inbound_and_quality_pass_issue_one_credit_note_and_settle_ar()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        await CreateReleasedSalesOrderAsync(dbContext);
        await new CreateAccountReceivableCommandHandler(dbContext).Handle(
            new CreateAccountReceivableCommand("org-001", "env-dev", "AR-RMA-CLOSE-001", "DO-RMA-CLOSE-001", "CUST-001", 200m, "CNY"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new CreateSalesReturnAuthorizationCommandHandler(dbContext, new ErpCodingService()).Handle(
            new CreateSalesReturnAuthorizationCommand(
                "org-001", "env-dev", "RMA-CLOSE-001", "SO-RMA-CLOSE-001", "AR-RMA-CLOSE-001", "SITE-01",
                [new SalesReturnAuthorizationCommandLine("LINE-001", 1m, "LOC-RETURN", null)], "rma-close-command"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var inboundHandler = new WmsInboundOrderCompletedIntegrationEventHandlerForRecordSalesReturnReceipt(dbContext, deadLetters);
        var inboundEvent = WmsEvent(
            WmsIntegrationEventTypes.InboundOrderCompleted,
            "IN-RMA-CLOSE-001",
            WmsSourceDocumentTypes.SalesReturnRma,
            "RMA-CLOSE-001",
            "rma-inbound-completed");
        await inboundHandler.HandleAsync(inboundEvent, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var qualityHandler = new QualityInspectionResultIntegrationEventHandlerForSettleSalesReturnCredit(dbContext, deadLetters, new ErpCodingService());
        var qualityEvent = QualityEvent(QualityIntegrationEventTypes.InspectionPassed, "IN-RMA-CLOSE-001", "rma-quality-passed");
        await qualityHandler.HandleAsync(qualityEvent, CancellationToken.None);
        Assert.Equal(EntityState.Added, dbContext.ChangeTracker.Entries<CreditNote>().Single().State);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await qualityHandler.HandleAsync(qualityEvent, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var rma = await dbContext.SalesReturnAuthorizations.SingleAsync(x => x.RmaNo == "RMA-CLOSE-001");
        var creditNote = Assert.Single(await dbContext.CreditNotes.ToListAsync());
        var receivable = await dbContext.AccountReceivables.SingleAsync(x => x.ReceivableNo == "AR-RMA-CLOSE-001");
        var voucher = Assert.Single(await dbContext.JournalVouchers.Where(x => x.VoucherNo == $"JV-CN-{creditNote.CreditNoteNo}").Include(x => x.Lines).ToListAsync());
        Assert.Equal("IN-RMA-CLOSE-001", rma.WmsInboundOrderNo);
        Assert.Equal("passed", rma.QualityDisposition);
        Assert.Equal(100m, creditNote.Amount);
        Assert.Equal(100m, receivable.CreditNoteAmount);
        Assert.Equal(100m, receivable.OpenAmount);
        Assert.Contains(voucher.Lines, x => x.AccountCode == "6001" && x.DebitAmount == 100m);
        Assert.Contains(voucher.Lines, x => x.AccountCode == "1122" && x.CreditAmount == 100m);
        Assert.Empty(await deadLetters.ListAsync(QualityInspectionResultIntegrationEventHandlerForSettleSalesReturnCredit.ConsumerName, IntegrationEventDeadLetterStatus.Pending, CancellationToken.None));
    }

    [Fact]
    public async Task Completed_supplier_return_issues_debit_note_reduces_ap_and_posts_balanced_compensation()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var order = PurchaseOrder.Create(
            "org-001", "env-dev", "PO-RETURN-001", "SUP-001", "SITE-01", "CNY",
            [new PurchaseOrderLineDraft("LINE-001", "SKU-RETURN-001", "EA", 2m, 100m, new DateOnly(2026, 7, 1))]);
        order.MarkApprovalRequested("approval-return-001");
        order.ReleaseAfterApproval("approval-return-001");
        var receipt = PurchaseReceipt.Record(
            order,
            "GR-RETURN-001",
            [new PurchaseReceiptLineDraft("LINE-001", 1m, "quality", "LOC-QA", null)],
            1m);
        var invoice = SupplierInvoice.Match(
            order,
            receipt,
            "SI-RETURN-001",
            new DateOnly(2026, 7, 11),
            new DateOnly(2026, 8, 11),
            "CNY",
            0m,
            20m,
            [new SupplierInvoiceLineDraft("LINE-001", "LINE-001", 1m, 110m)]);
        var payable = AccountPayable.Create(
            "org-001", "env-dev", "AP-RETURN-001", "SI-RETURN-001", "SUP-001", 110m, "CNY");
        dbContext.PurchaseOrders.Add(order);
        dbContext.PurchaseReceipts.Add(receipt);
        dbContext.SupplierInvoices.Add(invoice);
        dbContext.AccountPayables.Add(payable);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = new WmsOutboundOrderCompletedIntegrationEventHandlerForRecordPurchaseReturn(dbContext, deadLetters, new ErpCodingService());
        var integrationEvent = WmsEvent(
            WmsIntegrationEventTypes.OutboundOrderCompleted,
            "RTS-RETURN-001",
            WmsSourceDocumentTypes.PurchaseReceiptReturn,
            "GR-RETURN-001",
            "supplier-return-completed",
            [new WmsIntegrationPayloadLine("LINE-001", "SKU-RETURN-001", "EA", "SITE-01", "LOC-QA", 1m, "Completed")]);
        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        Assert.Equal(EntityState.Added, dbContext.ChangeTracker.Entries<PurchaseReturn>().Single().State);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var purchaseReturn = Assert.Single(await dbContext.PurchaseReturns.Include(x => x.Lines).ToListAsync());
        var debitNote = Assert.Single(await dbContext.DebitNotes.ToListAsync());
        var persistedPayable = await dbContext.AccountPayables.SingleAsync(x => x.PayableNo == "AP-RETURN-001");
        var voucher = Assert.Single(await dbContext.JournalVouchers.Where(x => x.VoucherNo == $"JV-PRTN-{purchaseReturn.PurchaseReturnNo}").Include(x => x.Lines).ToListAsync());
        Assert.Equal(0m, purchaseReturn.GrIrReversalAmount);
        Assert.Equal(110m, purchaseReturn.DebitNoteAmount);
        Assert.Equal(purchaseReturn.PurchaseReturnNo, debitNote.PurchaseReturnNo);
        Assert.Equal(110m, persistedPayable.DebitNoteAmount);
        Assert.Equal(0m, persistedPayable.OpenAmount);
        Assert.Contains(voucher.Lines, x => x.AccountCode == "2202" && x.DebitAmount == 110m);
        Assert.Contains(voucher.Lines, x => x.AccountCode == "1401" && x.CreditAmount == 110m);
        Assert.Empty(await deadLetters.ListAsync(WmsOutboundOrderCompletedIntegrationEventHandlerForRecordPurchaseReturn.ConsumerName, IntegrationEventDeadLetterStatus.Pending, CancellationToken.None));
    }

    [Fact]
    public async Task Supplier_return_with_insufficient_ap_dead_letters_without_mutating_ap_or_inbox()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var order = PurchaseOrder.Create(
            "org-001", "env-dev", "PO-RETURN-INSUFFICIENT-001", "SUP-001", "SITE-01", "CNY",
            [new PurchaseOrderLineDraft("LINE-001", "SKU-RETURN-001", "EA", 1m, 100m, new DateOnly(2026, 7, 1))]);
        order.MarkApprovalRequested("approval-return-insufficient-001");
        order.ReleaseAfterApproval("approval-return-insufficient-001");
        var receipt = PurchaseReceipt.Record(
            order,
            "GR-RETURN-INSUFFICIENT-001",
            [new PurchaseReceiptLineDraft("LINE-001", 1m, "quality", "LOC-QA", null)],
            1m);
        var invoice = SupplierInvoice.Match(
            order,
            receipt,
            "SI-RETURN-INSUFFICIENT-001",
            new DateOnly(2026, 7, 11),
            new DateOnly(2026, 8, 11),
            "CNY",
            0m,
            0m,
            [new SupplierInvoiceLineDraft("LINE-001", "LINE-001", 1m, 100m)]);
        var payable = AccountPayable.Create(
            "org-001", "env-dev", "AP-RETURN-INSUFFICIENT-001", "SI-RETURN-INSUFFICIENT-001", "SUP-001", 60m, "CNY");
        dbContext.PurchaseOrders.Add(order);
        dbContext.PurchaseReceipts.Add(receipt);
        dbContext.SupplierInvoices.Add(invoice);
        dbContext.AccountPayables.Add(payable);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = new WmsOutboundOrderCompletedIntegrationEventHandlerForRecordPurchaseReturn(dbContext, deadLetters, new ErpCodingService());
        var integrationEvent = WmsEvent(
            WmsIntegrationEventTypes.OutboundOrderCompleted,
            "RTS-RETURN-INSUFFICIENT-001",
            WmsSourceDocumentTypes.PurchaseReceiptReturn,
            "GR-RETURN-INSUFFICIENT-001",
            "supplier-return-insufficient",
            [new WmsIntegrationPayloadLine("LINE-001", "SKU-RETURN-001", "EA", "SITE-01", "LOC-QA", 1m, "Completed")]);

        await handler.HandleAsync(integrationEvent, CancellationToken.None);

        var persistedPayable = await dbContext.AccountPayables.SingleAsync(x => x.PayableNo == "AP-RETURN-INSUFFICIENT-001");
        Assert.Equal(0m, persistedPayable.DebitNoteAmount);
        Assert.Equal(60m, persistedPayable.OpenAmount);
        Assert.Empty(await dbContext.PurchaseReturns.ToListAsync());
        Assert.Empty(await dbContext.DebitNotes.ToListAsync());
        Assert.Empty(await dbContext.ProcessedIntegrationEvents.ToListAsync());
        Assert.Single(await deadLetters.ListAsync(
            WmsOutboundOrderCompletedIntegrationEventHandlerForRecordPurchaseReturn.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
    }

    [Fact]
    public async Task Uninvoiced_supplier_return_reverses_grir_without_creating_debit_note()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var order = PurchaseOrder.Create(
            "org-001", "env-dev", "PO-RETURN-GRIR-001", "SUP-001", "SITE-01", "CNY",
            [new PurchaseOrderLineDraft("LINE-001", "SKU-RETURN-001", "EA", 1m, 100m, new DateOnly(2026, 7, 1))]);
        order.MarkApprovalRequested("approval-return-grir-001");
        order.ReleaseAfterApproval("approval-return-grir-001");
        var receipt = PurchaseReceipt.Record(
            order,
            "GR-RETURN-GRIR-001",
            [new PurchaseReceiptLineDraft("LINE-001", 1m, "quality", "LOC-QA", null)],
            1m);
        dbContext.PurchaseOrders.Add(order);
        dbContext.PurchaseReceipts.Add(receipt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = new WmsOutboundOrderCompletedIntegrationEventHandlerForRecordPurchaseReturn(dbContext, deadLetters, new ErpCodingService());
        await handler.HandleAsync(
            WmsEvent(
                WmsIntegrationEventTypes.OutboundOrderCompleted,
                "RTS-RETURN-GRIR-001",
                WmsSourceDocumentTypes.PurchaseReceiptReturn,
                "GR-RETURN-GRIR-001",
                "supplier-return-grir",
                [new WmsIntegrationPayloadLine("LINE-001", "SKU-RETURN-001", "EA", "SITE-01", "LOC-QA", 1m, "Completed")]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var purchaseReturn = Assert.Single(await dbContext.PurchaseReturns.Include(x => x.Lines).ToListAsync());
        var voucher = Assert.Single(await dbContext.JournalVouchers.Where(x => x.VoucherNo == $"JV-PRTN-{purchaseReturn.PurchaseReturnNo}").Include(x => x.Lines).ToListAsync());
        Assert.Equal(100m, purchaseReturn.GrIrReversalAmount);
        Assert.Equal(0m, purchaseReturn.DebitNoteAmount);
        Assert.Empty(await dbContext.DebitNotes.ToListAsync());
        Assert.Contains(voucher.Lines, x => x.AccountCode == "GR-IR" && x.DebitAmount == 100m);
        Assert.Contains(voucher.Lines, x => x.AccountCode == "1401" && x.CreditAmount == 100m);
        Assert.Empty(await deadLetters.ListAsync(WmsOutboundOrderCompletedIntegrationEventHandlerForRecordPurchaseReturn.ConsumerName, IntegrationEventDeadLetterStatus.Pending, CancellationToken.None));
    }

    [Fact]
    public async Task Rejected_sales_return_deny_credit_without_moving_ar()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        await CreateReleasedSalesOrderAsync(dbContext);
        await new CreateAccountReceivableCommandHandler(dbContext).Handle(
            new CreateAccountReceivableCommand("org-001", "env-dev", "AR-RMA-REJECT-001", "DO-RMA-REJECT-001", "CUST-001", 200m, "CNY"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new CreateSalesReturnAuthorizationCommandHandler(dbContext, new ErpCodingService()).Handle(
            new CreateSalesReturnAuthorizationCommand(
                "org-001", "env-dev", "RMA-REJECT-001", "SO-RMA-CLOSE-001", "AR-RMA-REJECT-001", "SITE-01",
                [new SalesReturnAuthorizationCommandLine("LINE-001", 1m, "LOC-RETURN", null)], "rma-reject-command"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var inboundHandler = new WmsInboundOrderCompletedIntegrationEventHandlerForRecordSalesReturnReceipt(dbContext, deadLetters);
        await inboundHandler.HandleAsync(
            WmsEvent(WmsIntegrationEventTypes.InboundOrderCompleted, "IN-RMA-REJECT-001", WmsSourceDocumentTypes.SalesReturnRma, "RMA-REJECT-001", "rma-reject-inbound"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var qualityHandler = new QualityInspectionResultIntegrationEventHandlerForSettleSalesReturnCredit(dbContext, deadLetters, new ErpCodingService());
        await qualityHandler.HandleAsync(QualityEvent(QualityIntegrationEventTypes.InspectionRejected, "IN-RMA-REJECT-001", "rma-reject-quality"), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var rma = await dbContext.SalesReturnAuthorizations.SingleAsync(x => x.RmaNo == "RMA-REJECT-001");
        var receivable = await dbContext.AccountReceivables.SingleAsync(x => x.ReceivableNo == "AR-RMA-REJECT-001");
        Assert.Equal(SalesReturnAuthorizationStatus.CreditDenied, rma.Status);
        Assert.Equal("rejected", rma.QualityDisposition);
        Assert.Null(rma.CreditNoteNo);
        Assert.Equal(0m, receivable.CreditNoteAmount);
        Assert.Equal(200m, receivable.OpenAmount);
        Assert.Empty(await dbContext.CreditNotes.ToListAsync());
        Assert.Empty(await dbContext.JournalVouchers.Where(x => x.VoucherNo.StartsWith("JV-CN-", StringComparison.Ordinal)).ToListAsync());
        Assert.Empty(await deadLetters.ListAsync(QualityInspectionResultIntegrationEventHandlerForSettleSalesReturnCredit.ConsumerName, IntegrationEventDeadLetterStatus.Pending, CancellationToken.None));
    }

    private static WmsIntegrationEvent WmsEvent(
        string eventType,
        string publicReference,
        string sourceDocumentType,
        string sourceDocumentId,
        string idempotencyKey,
        IReadOnlyCollection<WmsIntegrationPayloadLine>? lines = null)
    {
        return new WmsIntegrationEvent(
            $"evt-{idempotencyKey}", eventType, WmsIntegrationEventVersions.V1, DateTimeOffset.UtcNow,
            WmsIntegrationEventSources.BusinessWms, "corr-return", "cause-return", "org-001", "env-dev", "system:wms", idempotencyKey,
            new WmsIntegrationPayload(publicReference, "LINE-001", "SKU-RETURN-001", "EA", "SITE-01", "LOC-QA", 1m, "Completed", null, null, lines, sourceDocumentType, sourceDocumentId));
    }

    private static InspectionResultIntegrationEvent QualityEvent(string eventType, string wmsInboundOrderNo, string idempotencyKey)
    {
        return new InspectionResultIntegrationEvent(
            $"evt-{idempotencyKey}", eventType, QualityIntegrationEventVersions.V1, DateTimeOffset.UtcNow,
            QualityIntegrationEventSources.BusinessQuality, "corr-return", "cause-return", "org-001", "env-dev", "system:quality", idempotencyKey,
            new InspectionResultPayload("QI-RMA-001", "PLAN-RMA-001", "receiving", "wms", wmsInboundOrderNo, "SKU-RMA-001", 1m, "passed", null, [], DateTimeOffset.UtcNow));
    }

    private static async Task CreateReleasedSalesOrderAsync(Infrastructure.ApplicationDbContext dbContext)
    {
        await new CreateQuotationCommandHandler(dbContext).Handle(
            new CreateQuotationCommand("org-001", "env-dev", "QUO-RMA-CLOSE-001", "CUST-001", new DateOnly(2026, 12, 31),
                [new QuotationCommandLine("LINE-001", "SKU-RMA-001", "EA", 2m, 100m, new DateOnly(2026, 7, 1))]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new ApproveQuotationCommandHandler(dbContext).Handle(new ApproveQuotationCommand("org-001", "env-dev", "QUO-RMA-CLOSE-001"), CancellationToken.None);
        await new CreateSalesOrderCommandHandler(
                dbContext,
                new StaticCustomerCreditProfileReader(new CustomerCreditProfile("CUST-001", 1_000_000m, "CNY"))).Handle(
                new CreateSalesOrderCommand("org-001", "env-dev", "SO-RMA-CLOSE-001", "QUO-RMA-CLOSE-001"),
                CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new ReleaseDeliveryOrderCommandHandler(dbContext).Handle(
            new ReleaseDeliveryOrderCommand("org-001", "env-dev", "DO-RMA-CLOSE-001", "SO-RMA-CLOSE-001", [new DeliveryOrderCommandLine("LINE-001", 2m)]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

}
