using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountPayableAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountReceivableAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseReceiptAggregate;
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

        var qualityHandler = new QualityInspectionResultIntegrationEventHandlerForSettleSalesReturnCredit(dbContext, deadLetters, new ErpCodingService());
        var qualityEvent = QualityEvent(QualityIntegrationEventTypes.InspectionPassed, "IN-RMA-CLOSE-001", "rma-quality-passed");
        await qualityHandler.HandleAsync(qualityEvent, CancellationToken.None);
        await qualityHandler.HandleAsync(qualityEvent, CancellationToken.None);

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
            0m,
            [new SupplierInvoiceLineDraft("LINE-001", "LINE-001", 1m, 100m)]);
        var payable = AccountPayable.Create(
            "org-001", "env-dev", "AP-RETURN-001", "SI-RETURN-001", "SUP-001", 100m, "CNY");
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
        await handler.HandleAsync(integrationEvent, CancellationToken.None);

        var purchaseReturn = Assert.Single(await dbContext.PurchaseReturns.Include(x => x.Lines).ToListAsync());
        var debitNote = Assert.Single(await dbContext.DebitNotes.ToListAsync());
        var persistedPayable = await dbContext.AccountPayables.SingleAsync(x => x.PayableNo == "AP-RETURN-001");
        var voucher = Assert.Single(await dbContext.JournalVouchers.Where(x => x.VoucherNo == $"JV-PRTN-{purchaseReturn.PurchaseReturnNo}").Include(x => x.Lines).ToListAsync());
        Assert.Equal(0m, purchaseReturn.GrIrReversalAmount);
        Assert.Equal(100m, purchaseReturn.DebitNoteAmount);
        Assert.Equal(purchaseReturn.PurchaseReturnNo, debitNote.PurchaseReturnNo);
        Assert.Equal(100m, persistedPayable.DebitNoteAmount);
        Assert.Equal(0m, persistedPayable.OpenAmount);
        Assert.Contains(voucher.Lines, x => x.AccountCode == "2202" && x.DebitAmount == 100m);
        Assert.Contains(voucher.Lines, x => x.AccountCode == "1401" && x.CreditAmount == 100m);
        Assert.Empty(await deadLetters.ListAsync(WmsOutboundOrderCompletedIntegrationEventHandlerForRecordPurchaseReturn.ConsumerName, IntegrationEventDeadLetterStatus.Pending, CancellationToken.None));
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
