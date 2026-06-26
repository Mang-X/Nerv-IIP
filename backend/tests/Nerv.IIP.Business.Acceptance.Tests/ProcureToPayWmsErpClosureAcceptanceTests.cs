using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.DomainEvents;
using Nerv.IIP.Business.Erp.Web.Application.Commands;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Procurement;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.DomainEvents;
using Nerv.IIP.Business.Wms.Web.Application.Commands;
using Nerv.IIP.Business.Wms.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Messaging.CAP;
using ErpDbContext = Nerv.IIP.Business.Erp.Infrastructure.ApplicationDbContext;
using WmsDbContext = Nerv.IIP.Business.Wms.Infrastructure.ApplicationDbContext;

namespace Nerv.IIP.Business.Acceptance.Tests;

public sealed class ProcureToPayWmsErpClosureAcceptanceTests
{
    [Fact]
    public async Task Wms_purchase_order_inbound_completion_projects_to_erp_receipt_grir_and_matched_ap_once()
    {
        await using var wmsDb = CreateWmsContext();
        await using var erpDb = CreateErpContext();
        await ReleasePurchaseOrderAsync(erpDb);
        var inbound = InboundOrder.Create(
            "org-001",
            "env-dev",
            "WMS-IN-P2P-001",
            "purchase-order",
            "PO-P2P-WMS-001",
            "SITE-01",
            [new InboundOrderLineDraft("LINE-001", "SKU-RM-1000", "kg", 2m, "RAW-A-01", "LOT-001", null, "accepted", "company", null)]);
        wmsDb.InboundOrders.Add(inbound);
        await wmsDb.SaveChangesAsync(CancellationToken.None);

        await new CompleteInboundOrderCommandHandler(wmsDb).Handle(
            new CompleteInboundOrderCommand(inbound.Id, "wms-complete:p2p:001"),
            CancellationToken.None);
        await wmsDb.SaveChangesAsync(CancellationToken.None);

        var wmsEvent = new InboundOrderCompletedIntegrationEventConverter()
            .Convert(new InboundOrderCompletedDomainEvent(inbound));
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var wmsToErpHandler = new WmsInboundOrderCompletedIntegrationEventHandlerForRecordPurchaseReceipt(
            erpDb,
            deadLetters,
            new ErpCodingService(),
            NullLogger<WmsInboundOrderCompletedIntegrationEventHandlerForRecordPurchaseReceipt>.Instance);

        await wmsToErpHandler.HandleAsync(wmsEvent, CancellationToken.None);
        await erpDb.SaveChangesAsync(CancellationToken.None);
        await wmsToErpHandler.HandleAsync(wmsEvent, CancellationToken.None);
        await erpDb.SaveChangesAsync(CancellationToken.None);

        var receipt = Assert.Single(erpDb.PurchaseReceipts.Include(x => x.Lines));
        Assert.Equal("WMS-IN-P2P-001", receipt.PurchaseReceiptNo);
        Assert.Empty(erpDb.AccountPayables);
        Assert.Empty(erpDb.JournalVouchers);

        var grIrHandler = new PurchaseReceiptRecordedIntegrationEventHandlerForPostGrIrAccrual(erpDb, deadLetters);
        var receiptEvent = new PurchaseReceiptRecordedIntegrationEventConverter()
            .Convert(new PurchaseReceiptRecordedDomainEvent(receipt));
        await grIrHandler.HandleAsync(receiptEvent, CancellationToken.None);
        await erpDb.SaveChangesAsync(CancellationToken.None);
        await grIrHandler.HandleAsync(receiptEvent, CancellationToken.None);
        await erpDb.SaveChangesAsync(CancellationToken.None);

        Assert.Single(erpDb.JournalVouchers);
        Assert.Empty(erpDb.AccountPayables);
        Assert.Equal(-25m, AccountBalance(erpDb, "GR-IR"));

        await new RecordSupplierInvoiceCommandHandler(erpDb, new ErpCodingService()).Handle(
            new RecordSupplierInvoiceCommand(
                "org-001",
                "env-dev",
                "INV-P2P-WMS-001",
                "PO-P2P-WMS-001",
                "WMS-IN-P2P-001",
                new DateOnly(2026, 7, 4),
                new DateOnly(2026, 8, 3),
                "CNY",
                0m,
                0m,
                [new SupplierInvoiceCommandLine("LINE-001", "LINE-001", 2m, 12.5m)],
                "AP-P2P-WMS-001",
                "invoice:p2p:wms:001"),
            CancellationToken.None);
        await erpDb.SaveChangesAsync(CancellationToken.None);

        var payable = Assert.Single(erpDb.AccountPayables);
        Assert.Equal("AP-P2P-WMS-001", payable.PayableNo);
        Assert.Equal("INV-P2P-WMS-001", payable.SourceDocumentNo);
        Assert.Equal(2, erpDb.JournalVouchers.Count());
        Assert.Equal(0m, AccountBalance(erpDb, "GR-IR"));
        Assert.Equal(-25m, AccountBalance(erpDb, "2202"));
        Assert.Equal(2, erpDb.ProcessedIntegrationEvents.Count());
        Assert.Empty(await deadLetters.ListAsync(
            WmsInboundOrderCompletedIntegrationEventHandlerForRecordPurchaseReceipt.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
    }

    private static async Task ReleasePurchaseOrderAsync(ErpDbContext erpDb)
    {
        var order = PurchaseOrder.Create(
            "org-001",
            "env-dev",
            "PO-P2P-WMS-001",
            "SUP-001",
            "SITE-01",
            [new PurchaseOrderLineDraft("LINE-001", "SKU-RM-1000", "kg", 2m, 12.5m, new DateOnly(2026, 7, 1))]);
        order.MarkApprovalRequested("chain-p2p-wms-001");
        order.ReleaseAfterApproval("chain-p2p-wms-001");
        erpDb.PurchaseOrders.Add(order);
        await erpDb.SaveChangesAsync(CancellationToken.None);
    }

    private static decimal AccountBalance(ErpDbContext erpDb, string accountCode)
    {
        return erpDb.JournalVouchers
            .SelectMany(x => x.Lines)
            .Where(x => x.AccountCode == accountCode)
            .Sum(x => x.DebitAmount - x.CreditAmount);
    }

    private static ErpDbContext CreateErpContext()
    {
        var options = new DbContextOptionsBuilder<ErpDbContext>()
            .UseInMemoryDatabase($"p2p-wms-erp-{Guid.NewGuid():N}")
            .Options;
        return new ErpDbContext(options, new NoopMediator());
    }

    private static WmsDbContext CreateWmsContext()
    {
        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase($"p2p-wms-erp-{Guid.NewGuid():N}")
            .Options;
        return new WmsDbContext(options, new NoopMediator());
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Test mediator cannot send requests.");
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            throw new NotSupportedException("Test mediator cannot send requests.");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Test mediator cannot send requests.");
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Test mediator cannot stream requests.");
        }

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Test mediator cannot stream requests.");
        }
    }
}
