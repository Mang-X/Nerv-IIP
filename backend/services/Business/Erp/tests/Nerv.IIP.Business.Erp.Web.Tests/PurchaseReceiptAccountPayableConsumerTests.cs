using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.DomainEvents;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Procurement;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Erp.Web.Application.Queries.SalesFinance;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Erp.Web.Tests;

public sealed class PurchaseReceiptAccountPayableConsumerTests
{
    [Fact]
    public async Task PurchaseReceiptRecordedHandler_CreatesPayableFromReceiptAndPurchaseOrderFactsOnce()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        await RecordReceiptAsync(
            dbContext,
            "PO-AP-001",
            "RCV-AP-001",
            [
                new PurchaseOrderLineDraft("LINE-001", "SKU-RM-001", "kg", 5m, 12.5m, new DateOnly(2026, 7, 1)),
                new PurchaseOrderLineDraft("LINE-002", "SKU-RM-002", "kg", 5m, 7.25m, new DateOnly(2026, 7, 1)),
            ],
            [
                new PurchaseReceiptCommandLine("LINE-001", 2m, "accepted"),
                new PurchaseReceiptCommandLine("LINE-002", 3m, "accepted"),
            ]);
        var receipt = await dbContext.PurchaseReceipts
            .Include(x => x.Lines)
            .SingleAsync(x => x.PurchaseReceiptNo == "RCV-AP-001", CancellationToken.None);
        var integrationEvent = new PurchaseReceiptRecordedIntegrationEventConverter()
            .Convert(new PurchaseReceiptRecordedDomainEvent(receipt));
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = new PurchaseReceiptRecordedIntegrationEventHandlerForCreateAccountPayable(dbContext, deadLetters);

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var payable = await new GetAccountPayableBySourceDocumentQueryHandler(dbContext).Handle(
            new GetAccountPayableBySourceDocumentQuery("org-001", "env-dev", "RCV-AP-001"),
            CancellationToken.None);
        Assert.Equal("RCV-AP-001", payable.SourceDocumentNo);
        Assert.Equal("SUP-001", payable.SupplierCode);
        Assert.Equal(46.75m, payable.Amount);
        Assert.Equal("CNY", payable.CurrencyCode);
        Assert.Single(dbContext.AccountPayables);
        Assert.Single(dbContext.JournalVouchers);
        Assert.Single(dbContext.ProcessedIntegrationEvents);
        Assert.Empty(await deadLetters.ListAsync(
            PurchaseReceiptRecordedIntegrationEventHandlerForCreateAccountPayable.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
    }

    [Fact]
    public async Task PurchaseReceiptRecordedHandler_DeadLettersUnsupportedQualityWithoutCreatingPayable()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        await RecordReceiptAsync(
            dbContext,
            "PO-AP-REJECTED",
            "RCV-AP-REJECTED",
            [new PurchaseOrderLineDraft("LINE-001", "SKU-RM-001", "kg", 5m, 12.5m, new DateOnly(2026, 7, 1))],
            [new PurchaseReceiptCommandLine("LINE-001", 2m, "rejected")]);
        var receipt = await dbContext.PurchaseReceipts
            .Include(x => x.Lines)
            .SingleAsync(x => x.PurchaseReceiptNo == "RCV-AP-REJECTED", CancellationToken.None);
        var integrationEvent = new PurchaseReceiptRecordedIntegrationEventConverter()
            .Convert(new PurchaseReceiptRecordedDomainEvent(receipt));
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = new PurchaseReceiptRecordedIntegrationEventHandlerForCreateAccountPayable(dbContext, deadLetters);

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Empty(dbContext.AccountPayables);
        Assert.Empty(dbContext.JournalVouchers);
        Assert.Empty(dbContext.ProcessedIntegrationEvents);
        var deadLetter = Assert.Single(await deadLetters.ListAsync(
            PurchaseReceiptRecordedIntegrationEventHandlerForCreateAccountPayable.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
        Assert.Equal("unsupported-quality-status", deadLetter.FailureCode);
        Assert.Equal(integrationEvent.IdempotencyKey, deadLetter.IdempotencyKey);
    }

    private static async Task RecordReceiptAsync(
        Infrastructure.ApplicationDbContext dbContext,
        string purchaseOrderNo,
        string purchaseReceiptNo,
        IReadOnlyCollection<PurchaseOrderLineDraft> orderLines,
        IReadOnlyCollection<PurchaseReceiptCommandLine> receiptLines)
    {
        var order = PurchaseOrder.Create(
            "org-001",
            "env-dev",
            purchaseOrderNo,
            "SUP-001",
            "SITE-01",
            orderLines);
        order.MarkApprovalRequested($"chain-{purchaseOrderNo}");
        order.ReleaseAfterApproval($"chain-{purchaseOrderNo}");
        dbContext.PurchaseOrders.Add(order);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await new RecordPurchaseReceiptCommandHandler(dbContext).Handle(
            new RecordPurchaseReceiptCommand("org-001", "env-dev", purchaseReceiptNo, purchaseOrderNo, receiptLines),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }
}
