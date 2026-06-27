extern alias WmsWeb;

using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.DomainEvents;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Commands;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Procurement;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Erp.Web.Application.Queries.SalesFinance;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.DomainEvents;
using Nerv.IIP.Contracts.Wms;
using Nerv.IIP.Messaging.CAP;
using InboundOrderCompletedIntegrationEventConverter = WmsWeb::Nerv.IIP.Business.Wms.Web.Application.IntegrationEventConverters.InboundOrderCompletedIntegrationEventConverter;

namespace Nerv.IIP.Business.Erp.Web.Tests;

public sealed class WmsInboundCompletedPurchaseReceiptConsumerTests
{
    [Fact]
    public async Task InboundOrderCompletedHandler_RecordsPurchaseReceiptAndGrIrToApClosureOnce()
    {
        await using var dbContext = CreateDbContext();
        await ReleasePurchaseOrderAsync(dbContext, "PO-WMS-GRIR-001", "LINE-001", 2m, 12.5m);
        var integrationEvent = BuildWmsCompletedEvent("WMS-IN-GRIR-001", "purchase-order", "PO-WMS-GRIR-001", "LINE-001", 2m);
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var inboundHandler = CreateInboundHandler(dbContext, deadLetters);

        await inboundHandler.HandleAsync(integrationEvent, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await inboundHandler.HandleAsync(integrationEvent, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var receipt = Assert.Single(dbContext.PurchaseReceipts.Include(x => x.Lines));
        Assert.Equal("WMS-IN-GRIR-001", receipt.PurchaseReceiptNo);
        Assert.Equal("PO-WMS-GRIR-001", receipt.PurchaseOrderNo);
        var receiptLine = Assert.Single(receipt.Lines);
        Assert.Equal("LINE-001", receiptLine.PurchaseOrderLineNo);
        Assert.Equal("unrestricted", receiptLine.QualityStatus);
        Assert.Empty(dbContext.AccountPayables);
        Assert.Empty(dbContext.JournalVouchers);

        var receiptEvent = new PurchaseReceiptRecordedIntegrationEventConverter()
            .Convert(new PurchaseReceiptRecordedDomainEvent(receipt));
        var grIrHandler = new PurchaseReceiptRecordedIntegrationEventHandlerForPostGrIrAccrual(dbContext, deadLetters);
        await grIrHandler.HandleAsync(receiptEvent, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await grIrHandler.HandleAsync(receiptEvent, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Empty(dbContext.AccountPayables);
        Assert.Single(dbContext.JournalVouchers);
        Assert.Equal(25m, AccountBalance(dbContext, "1401"));
        Assert.Equal(-25m, AccountBalance(dbContext, "GR-IR"));

        await new RecordSupplierInvoiceCommandHandler(dbContext, new ErpCodingService()).Handle(
            new RecordSupplierInvoiceCommand(
                "org-001",
                "env-dev",
                "INV-WMS-GRIR-001",
                "PO-WMS-GRIR-001",
                "WMS-IN-GRIR-001",
                new DateOnly(2026, 7, 4),
                new DateOnly(2026, 8, 3),
                "CNY",
                0m,
                0m,
                [new SupplierInvoiceCommandLine("LINE-001", "LINE-001", 2m, 12.5m)],
                "AP-WMS-GRIR-001",
                "invoice:WMS-IN-GRIR-001"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var payable = await new GetAccountPayableBySourceDocumentQueryHandler(dbContext).Handle(
            new GetAccountPayableBySourceDocumentQuery("org-001", "env-dev", "INV-WMS-GRIR-001"),
            CancellationToken.None);
        Assert.Equal("AP-WMS-GRIR-001", payable.PayableNo);
        Assert.Equal(25m, payable.Amount);
        Assert.Equal(2, dbContext.JournalVouchers.Count());
        Assert.Equal(0m, AccountBalance(dbContext, "GR-IR"));
        Assert.Equal(-25m, AccountBalance(dbContext, "2202"));
        Assert.Equal(2, dbContext.ProcessedIntegrationEvents.Count());
        Assert.Empty(await deadLetters.ListAsync(
            WmsInboundOrderCompletedIntegrationEventHandlerForRecordPurchaseReceipt.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
    }

    [Fact]
    public async Task InboundOrderCompletedHandler_DeadLettersWhenNoPayableQualityLinesRemain()
    {
        await using var dbContext = CreateDbContext();
        await ReleasePurchaseOrderAsync(dbContext, "PO-WMS-BLOCKED-ONLY", "LINE-001", 2m, 12.5m);
        var integrationEvent = BuildWmsCompletedEvent("WMS-IN-BLOCKED-ONLY", "purchase-order", "PO-WMS-BLOCKED-ONLY", "LINE-001", 2m);
        integrationEvent = integrationEvent with
        {
            Payload = integrationEvent.Payload with
            {
                Lines =
                [
                    new WmsIntegrationPayloadLine("LINE-001", "SKU-RM-1000", "kg", "SITE-01", "HOLD-A-01", 2m, "blocked"),
                ],
            },
        };
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = CreateInboundHandler(dbContext, deadLetters);

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Empty(dbContext.PurchaseReceipts);
        Assert.Empty(dbContext.ProcessedIntegrationEvents);
        var order = dbContext.PurchaseOrders.Include(x => x.Lines).Single(x => x.PurchaseOrderNo == "PO-WMS-BLOCKED-ONLY");
        Assert.Equal(0m, Assert.Single(order.Lines).ReceivedQuantity);
        var deadLetter = Assert.Single(await deadLetters.ListAsync(
            WmsInboundOrderCompletedIntegrationEventHandlerForRecordPurchaseReceipt.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
        Assert.Equal("no-payable-receipt-lines", deadLetter.FailureCode);
    }

    [Fact]
    public async Task InboundOrderCompletedHandler_IgnoresPurchaseReceiptSourcedInboundToAvoidDuplicateGrIr()
    {
        await using var dbContext = CreateDbContext();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = CreateInboundHandler(dbContext, deadLetters);
        var integrationEvent = BuildWmsCompletedEvent("WMS-IN-FROM-ERP-RECEIPT", "purchase-receipt", "RCV-ERP-001", "LINE-001", 2m);

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Empty(dbContext.PurchaseReceipts);
        Assert.Empty(dbContext.JournalVouchers);
        Assert.Empty(dbContext.ProcessedIntegrationEvents);
        Assert.Empty(await deadLetters.ListAsync(
            WmsInboundOrderCompletedIntegrationEventHandlerForRecordPurchaseReceipt.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
    }

    [Fact]
    public async Task InboundOrderCompletedHandler_DeadLettersDuplicateSourceLinesWithoutMutatingPurchaseOrder()
    {
        await using var dbContext = CreateDbContext();
        await ReleasePurchaseOrderAsync(
            dbContext,
            "PO-WMS-DUP-LINE",
            "LINE-001",
            2m,
            12.5m);
        var integrationEvent = BuildWmsCompletedEvent("WMS-IN-DUP-LINE", "purchase-order", "PO-WMS-DUP-LINE", "LINE-001", 1m);
        integrationEvent = integrationEvent with
        {
            Payload = integrationEvent.Payload with
            {
                Lines =
                [
                    new WmsIntegrationPayloadLine("LINE-001", "SKU-RM-1000", "kg", "SITE-01", "RAW-A-01", 1m, "accepted"),
                    new WmsIntegrationPayloadLine("LINE-001", "SKU-RM-1000", "kg", "SITE-01", "RAW-A-02", 2m, "accepted"),
                ],
            },
        };
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = CreateInboundHandler(dbContext, deadLetters);

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Empty(dbContext.PurchaseReceipts);
        Assert.Empty(dbContext.JournalVouchers);
        Assert.Empty(dbContext.ProcessedIntegrationEvents);
        var order = dbContext.PurchaseOrders.Include(x => x.Lines).Single(x => x.PurchaseOrderNo == "PO-WMS-DUP-LINE");
        Assert.Equal(0m, Assert.Single(order.Lines).ReceivedQuantity);
        var deadLetter = Assert.Single(await deadLetters.ListAsync(
            WmsInboundOrderCompletedIntegrationEventHandlerForRecordPurchaseReceipt.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
        Assert.Equal("duplicate-source-line", deadLetter.FailureCode);
    }

    [Fact]
    public async Task InboundOrderCompletedHandler_RecordsOnlyPayableQualityLinesWithoutClosingBlockedLine()
    {
        await using var dbContext = CreateDbContext();
        var order = PurchaseOrder.Create(
            "org-001",
            "env-dev",
            "PO-WMS-MIXED-QUALITY",
            "SUP-001",
            "SITE-01",
            [
                new PurchaseOrderLineDraft("LINE-001", "SKU-RM-1000", "kg", 2m, 12.5m, new DateOnly(2026, 7, 1)),
                new PurchaseOrderLineDraft("LINE-002", "SKU-RM-1000", "kg", 3m, 12.5m, new DateOnly(2026, 7, 1)),
            ]);
        order.MarkApprovalRequested("chain-PO-WMS-MIXED-QUALITY");
        order.ReleaseAfterApproval("chain-PO-WMS-MIXED-QUALITY");
        dbContext.PurchaseOrders.Add(order);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var integrationEvent = BuildWmsCompletedEvent("WMS-IN-MIXED-QUALITY", "purchase-order", "PO-WMS-MIXED-QUALITY", "LINE-001", 2m);
        integrationEvent = integrationEvent with
        {
            Payload = integrationEvent.Payload with
            {
                Lines =
                [
                    new WmsIntegrationPayloadLine("LINE-001", "SKU-RM-1000", "kg", "SITE-01", "RAW-A-01", 2m, "qualified"),
                    new WmsIntegrationPayloadLine("LINE-002", "SKU-RM-1000", "kg", "SITE-01", "HOLD-A-01", 3m, "blocked"),
                ],
            },
        };
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = CreateInboundHandler(dbContext, deadLetters);

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var receipt = Assert.Single(dbContext.PurchaseReceipts.Include(x => x.Lines));
        var receiptLine = Assert.Single(receipt.Lines);
        Assert.Equal("LINE-001", receiptLine.PurchaseOrderLineNo);
        Assert.Equal("unrestricted", receiptLine.QualityStatus);
        var persistedOrder = dbContext.PurchaseOrders.Include(x => x.Lines).Single(x => x.PurchaseOrderNo == "PO-WMS-MIXED-QUALITY");
        Assert.Equal(2m, persistedOrder.Lines.Single(x => x.LineNo == "LINE-001").ReceivedQuantity);
        Assert.Equal(0m, persistedOrder.Lines.Single(x => x.LineNo == "LINE-002").ReceivedQuantity);
        Assert.Empty(await deadLetters.ListAsync(
            WmsInboundOrderCompletedIntegrationEventHandlerForRecordPurchaseReceipt.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
    }

    private static WmsInboundOrderCompletedIntegrationEventHandlerForRecordPurchaseReceipt CreateInboundHandler(
        ApplicationDbContext dbContext,
        IIntegrationEventDeadLetterStore deadLetterStore)
    {
        return new WmsInboundOrderCompletedIntegrationEventHandlerForRecordPurchaseReceipt(
            dbContext,
            deadLetterStore,
            new ErpCodingService(),
            new TestLogger<WmsInboundOrderCompletedIntegrationEventHandlerForRecordPurchaseReceipt>());
    }

    private static async Task ReleasePurchaseOrderAsync(
        ApplicationDbContext dbContext,
        string purchaseOrderNo,
        string lineNo,
        decimal quantity,
        decimal unitPrice)
    {
        var order = PurchaseOrder.Create(
            "org-001",
            "env-dev",
            purchaseOrderNo,
            "SUP-001",
            "SITE-01",
            [new PurchaseOrderLineDraft(lineNo, "SKU-RM-1000", "kg", quantity, unitPrice, new DateOnly(2026, 7, 1))]);
        order.MarkApprovalRequested($"chain-{purchaseOrderNo}");
        order.ReleaseAfterApproval($"chain-{purchaseOrderNo}");
        dbContext.PurchaseOrders.Add(order);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private static WmsIntegrationEvent BuildWmsCompletedEvent(
        string inboundOrderNo,
        string sourceDocumentType,
        string sourceDocumentId,
        string sourceLineNo,
        decimal quantity)
    {
        var inbound = InboundOrder.Create(
            "org-001",
            "env-dev",
            inboundOrderNo,
            sourceDocumentType,
            sourceDocumentId,
            "SITE-01",
            [
                new InboundOrderLineDraft(
                    sourceLineNo,
                    "SKU-RM-1000",
                    "kg",
                    quantity,
                    "RAW-A-01",
                    "LOT-001",
                    null,
                    "qualified",
                    "company",
                    null)
            ]);
        inbound.Complete($"wms-complete:{inboundOrderNo}");
        return new InboundOrderCompletedIntegrationEventConverter()
            .Convert(new InboundOrderCompletedDomainEvent(inbound));
    }

    private static decimal AccountBalance(ApplicationDbContext dbContext, string accountCode)
    {
        return dbContext.JournalVouchers
            .SelectMany(x => x.Lines)
            .Where(x => x.AccountCode == accountCode)
            .Sum(x => x.DebitAmount - x.CreditAmount);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"erp-wms-inbound-grir-{Guid.CreateVersion7():N}", new InMemoryDatabaseRoot())
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
        }
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Noop mediator cannot send requests.");
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            throw new NotSupportedException("Noop mediator cannot send requests.");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Noop mediator cannot send requests.");
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Noop mediator cannot stream requests.");
        }

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Noop mediator cannot stream requests.");
        }
    }
}
