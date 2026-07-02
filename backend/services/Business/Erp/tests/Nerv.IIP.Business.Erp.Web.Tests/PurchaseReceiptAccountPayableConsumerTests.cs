using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.DomainEvents;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Procurement;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEvents;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Erp.Web.Application.Queries.SalesFinance;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Erp.Web.Tests;

public sealed class PurchaseReceiptAccountPayableConsumerTests
{
    [Fact]
    public async Task PurchaseReceiptRecordedHandler_PostsGrIrAccrualFromReceiptAndPurchaseOrderFactsOnce()
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
        var handler = CreateHandler(scope, dbContext, deadLetters);

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Empty(dbContext.AccountPayables);
        Assert.Single(dbContext.JournalVouchers);
        Assert.Equal(46.75m, AccountBalance(dbContext, "1401"));
        Assert.Equal(-46.75m, AccountBalance(dbContext, "GR-IR"));
        Assert.Equal(0m, AccountBalance(dbContext, "2202"));
        Assert.Single(dbContext.ProcessedIntegrationEvents);
        Assert.Empty(await deadLetters.ListAsync(
            PurchaseReceiptRecordedIntegrationEventHandlerForPostGrIrAccrual.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
    }

    [Fact]
    public async Task PurchaseReceiptRecordedHandler_PostsDistinctGrIrAccrualsForDifferentReceipts()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = CreateHandler(scope, dbContext, deadLetters);
        await RecordReceiptAsync(
            dbContext,
            "PO-AP-101",
            "RCV-AP-101",
            [new PurchaseOrderLineDraft("LINE-001", "SKU-RM-001", "kg", 5m, 12.5m, new DateOnly(2026, 7, 1))],
            [new PurchaseReceiptCommandLine("LINE-001", 2m, "accepted")]);
        await RecordReceiptAsync(
            dbContext,
            "PO-AP-102",
            "RCV-AP-102",
            [new PurchaseOrderLineDraft("LINE-001", "SKU-RM-001", "kg", 5m, 12.5m, new DateOnly(2026, 7, 1))],
            [new PurchaseReceiptCommandLine("LINE-001", 3m, "accepted")]);

        await handler.HandleAsync(await BuildReceiptRecordedEventAsync(dbContext, "RCV-AP-101"), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await handler.HandleAsync(await BuildReceiptRecordedEventAsync(dbContext, "RCV-AP-102"), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var vouchers = await dbContext.JournalVouchers.OrderBy(x => x.VoucherNo).ToListAsync(CancellationToken.None);
        Assert.Empty(dbContext.AccountPayables);
        Assert.Equal(["JV-GRIR-RCV-AP-101", "JV-GRIR-RCV-AP-102"], vouchers.Select(x => x.VoucherNo).ToArray());
        Assert.Equal(2, dbContext.JournalVouchers.Count());
        Assert.Empty(await deadLetters.ListAsync(
            PurchaseReceiptRecordedIntegrationEventHandlerForPostGrIrAccrual.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
    }

    [Fact]
    public async Task PurchaseReceiptRecordedHandler_PostsGrIrAccrualForInspectionReceiptBecauseNoQualityPassRetriggerExists()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        await RecordReceiptAsync(
            dbContext,
            "PO-AP-INSPECTION",
            "RCV-AP-INSPECTION",
            [new PurchaseOrderLineDraft("LINE-001", "SKU-RM-001", "kg", 5m, 12.5m, new DateOnly(2026, 7, 1))],
            [new PurchaseReceiptCommandLine("LINE-001", 2m, "inspection")]);
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = CreateHandler(scope, dbContext, deadLetters);

        await handler.HandleAsync(await BuildReceiptRecordedEventAsync(dbContext, "RCV-AP-INSPECTION"), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Empty(dbContext.AccountPayables);
        Assert.Single(dbContext.JournalVouchers);
        Assert.Equal(25m, AccountBalance(dbContext, "1401"));
        Assert.Equal(-25m, AccountBalance(dbContext, "GR-IR"));
        Assert.Single(dbContext.ProcessedIntegrationEvents);
        Assert.Empty(await deadLetters.ListAsync(
            PurchaseReceiptRecordedIntegrationEventHandlerForPostGrIrAccrual.ConsumerName,
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
            "PO-AP-DAMAGED",
            "RCV-AP-DAMAGED",
            [new PurchaseOrderLineDraft("LINE-001", "SKU-RM-001", "kg", 5m, 12.5m, new DateOnly(2026, 7, 1))],
            [new PurchaseReceiptCommandLine("LINE-001", 2m, "damaged")]);
        var integrationEvent = await BuildReceiptRecordedEventAsync(dbContext, "RCV-AP-DAMAGED");
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = CreateHandler(scope, dbContext, deadLetters);

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Empty(dbContext.AccountPayables);
        Assert.Empty(dbContext.JournalVouchers);
        Assert.Empty(dbContext.ProcessedIntegrationEvents);
        var deadLetter = Assert.Single(await deadLetters.ListAsync(
            PurchaseReceiptRecordedIntegrationEventHandlerForPostGrIrAccrual.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
        Assert.Equal("unsupported-quality-status", deadLetter.FailureCode);
        Assert.Equal(integrationEvent.IdempotencyKey, deadLetter.IdempotencyKey);
    }

    private static async Task<PurchaseReceiptRecordedIntegrationEvent> BuildReceiptRecordedEventAsync(
        Infrastructure.ApplicationDbContext dbContext,
        string purchaseReceiptNo)
    {
        var receipt = await dbContext.PurchaseReceipts
            .Include(x => x.Lines)
            .SingleAsync(x => x.PurchaseReceiptNo == purchaseReceiptNo, CancellationToken.None);
        return new PurchaseReceiptRecordedIntegrationEventConverter()
            .Convert(new PurchaseReceiptRecordedDomainEvent(receipt));
    }

    private static PurchaseReceiptRecordedIntegrationEventHandlerForPostGrIrAccrual CreateHandler(
        IServiceScope scope,
        Infrastructure.ApplicationDbContext dbContext,
        IIntegrationEventDeadLetterStore deadLetters)
    {
        return new PurchaseReceiptRecordedIntegrationEventHandlerForPostGrIrAccrual(
            dbContext,
            deadLetters);
    }

    private static decimal AccountBalance(Infrastructure.ApplicationDbContext dbContext, string accountCode)
    {
        return dbContext.JournalVouchers
            .SelectMany(x => x.Lines)
            .Where(x => x.AccountCode == accountCode)
            .Sum(x => x.DebitAmount - x.CreditAmount);
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
