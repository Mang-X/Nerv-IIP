using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Production;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MesStockMovementPostedConsumerTests
{
    [Fact]
    public async Task Stock_movement_posted_consumer_marks_matching_finished_goods_receipt_posted()
    {
        await using var dbContext = CreateDbContext(nameof(Stock_movement_posted_consumer_marks_matching_finished_goods_receipt_posted));
        dbContext.FinishedGoodsReceiptRequests.Add(FinishedGoodsReceiptRequest.Create(
            "org-001",
            "env-dev",
            "FGR-001",
            "WO-001",
            "SKU-FG",
            8m,
            "PCS",
            DateTimeOffset.Parse("2026-06-15T09:00:00Z"),
            "LOT-FG-001",
            null));
        await dbContext.SaveChangesAsync();

        var handler = new StockMovementPostedIntegrationEventHandlerForMarkMesReceiptPosted(
            dbContext,
            new InMemoryIntegrationEventDeadLetterStore());

        await handler.HandleAsync(CreatePostedEvent("FGR-001"), CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var receipt = await dbContext.FinishedGoodsReceiptRequests.SingleAsync();
        Assert.Equal(FinishedGoodsReceiptRequest.PostedStatus, receipt.Status);
        Assert.Equal("INV-MOV-001", receipt.PostedInventoryMovementId);
        Assert.Equal(DateTimeOffset.Parse("2026-06-15T09:05:00Z"), receipt.PostedAtUtc);
    }

    [Fact]
    public async Task Stock_movement_posted_consumer_ignores_non_mes_source_documents()
    {
        await using var dbContext = CreateDbContext(nameof(Stock_movement_posted_consumer_ignores_non_mes_source_documents));
        dbContext.FinishedGoodsReceiptRequests.Add(FinishedGoodsReceiptRequest.Create(
            "org-001",
            "env-dev",
            "FGR-001",
            "WO-001",
            "SKU-FG",
            8m,
            "PCS",
            DateTimeOffset.Parse("2026-06-15T09:00:00Z"),
            "LOT-FG-001",
            null));
        await dbContext.SaveChangesAsync();

        var handler = new StockMovementPostedIntegrationEventHandlerForMarkMesReceiptPosted(
            dbContext,
            new InMemoryIntegrationEventDeadLetterStore());

        await handler.HandleAsync(CreatePostedEvent("FGR-001", payloadSourceService: "business-wms"), CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var receipt = await dbContext.FinishedGoodsReceiptRequests.SingleAsync();
        Assert.Equal(FinishedGoodsReceiptRequest.RequestedStatus, receipt.Status);
        Assert.Null(receipt.PostedInventoryMovementId);
    }

    [Fact]
    public async Task Stock_movement_posted_consumer_rejects_mismatched_receipt_payload()
    {
        await using var dbContext = CreateDbContext(nameof(Stock_movement_posted_consumer_rejects_mismatched_receipt_payload));
        dbContext.FinishedGoodsReceiptRequests.Add(FinishedGoodsReceiptRequest.Create(
            "org-001",
            "env-dev",
            "FGR-001",
            "WO-001",
            "SKU-FG",
            8m,
            "PCS",
            DateTimeOffset.Parse("2026-06-15T09:00:00Z"),
            "LOT-FG-001",
            null));
        await dbContext.SaveChangesAsync();

        var handler = new StockMovementPostedIntegrationEventHandlerForMarkMesReceiptPosted(
            dbContext,
            new InMemoryIntegrationEventDeadLetterStore());

        await handler.HandleAsync(CreatePostedEvent("FGR-001", quantity: 9m), CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var receipt = await dbContext.FinishedGoodsReceiptRequests.SingleAsync();
        Assert.Equal(FinishedGoodsReceiptRequest.RequestedStatus, receipt.Status);
        Assert.Null(receipt.PostedInventoryMovementId);
    }

    [Fact]
    public async Task Stock_movement_posted_consumer_marks_partial_finished_goods_receipt_without_closing_request()
    {
        await using var dbContext = CreateDbContext(nameof(Stock_movement_posted_consumer_marks_partial_finished_goods_receipt_without_closing_request));
        dbContext.FinishedGoodsReceiptRequests.Add(FinishedGoodsReceiptRequest.Create(
            "org-001",
            "env-dev",
            "FGR-001",
            "WO-001",
            "SKU-FG",
            8m,
            "PCS",
            DateTimeOffset.Parse("2026-06-15T09:00:00Z"),
            "LOT-FG-001",
            null));
        await dbContext.SaveChangesAsync();

        var handler = new StockMovementPostedIntegrationEventHandlerForMarkMesReceiptPosted(
            dbContext,
            new InMemoryIntegrationEventDeadLetterStore());

        await handler.HandleAsync(CreatePostedEvent("FGR-001", quantity: 3m), CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var receipt = await dbContext.FinishedGoodsReceiptRequests.SingleAsync();
        Assert.Equal("PartiallyPosted", receipt.Status);
        Assert.Equal(3m, receipt.PostedQuantity);
        Assert.Equal(5m, receipt.RemainingQuantity);
        Assert.Equal("INV-MOV-001", receipt.PostedInventoryMovementId);
    }

    [Fact]
    public async Task Retry_finished_goods_receipt_inventory_posting_reemits_remaining_quantity_after_partial_failure()
    {
        await using var dbContext = CreateDbContext(nameof(Retry_finished_goods_receipt_inventory_posting_reemits_remaining_quantity_after_partial_failure));
        var receipt = FinishedGoodsReceiptRequest.Create(
            "org-001",
            "env-dev",
            "FGR-001",
            "WO-001",
            "SKU-FG",
            8m,
            "PCS",
            DateTimeOffset.Parse("2026-06-15T09:00:00Z"),
            "LOT-FG-001",
            null,
            12.34m);
        receipt.MarkInventoryPosted("INV-MOV-PARTIAL", 3m, DateTimeOffset.Parse("2026-06-15T09:05:00Z"));
        receipt.MarkInventoryPostingFailed(
            "inventory.validation.failed",
            "remaining posting rejected",
            DateTimeOffset.Parse("2026-06-15T09:10:00Z"));
        receipt.ClearDomainEvents();
        dbContext.FinishedGoodsReceiptRequests.Add(receipt);
        await dbContext.SaveChangesAsync();

        var result = await new RetryFinishedGoodsReceiptInventoryPostingCommandHandler(dbContext).Handle(
            new RetryFinishedGoodsReceiptInventoryPostingCommand("org-001", "env-dev", "FGR-001", "retry-remaining-001"),
            CancellationToken.None);

        Assert.Equal("FGR-001", result.RequestNo);
        Assert.Equal(FinishedGoodsReceiptRequest.PartiallyPostedStatus, receipt.Status);
        var retryEvent = Assert.IsType<FinishedGoodsReceiptRequestedDomainEvent>(receipt.GetDomainEvents().Single());
        Assert.Equal(5m, retryEvent.Quantity);
        Assert.Contains("retry-remaining-001", retryEvent.IdempotencyKey, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Retry_finished_goods_receipt_inventory_posting_rejects_partial_post_without_failure()
    {
        await using var dbContext = CreateDbContext(nameof(Retry_finished_goods_receipt_inventory_posting_rejects_partial_post_without_failure));
        var receipt = FinishedGoodsReceiptRequest.Create(
            "org-001",
            "env-dev",
            "FGR-001",
            "WO-001",
            "SKU-FG",
            8m,
            "PCS",
            DateTimeOffset.Parse("2026-06-15T09:00:00Z"),
            "LOT-FG-001",
            null,
            12.34m);
        receipt.MarkInventoryPosted("INV-MOV-PARTIAL", 3m, DateTimeOffset.Parse("2026-06-15T09:05:00Z"));
        receipt.ClearDomainEvents();
        dbContext.FinishedGoodsReceiptRequests.Add(receipt);
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            new RetryFinishedGoodsReceiptInventoryPostingCommandHandler(dbContext).Handle(
                new RetryFinishedGoodsReceiptInventoryPostingCommand("org-001", "env-dev", "FGR-001", "retry-remaining-001"),
                CancellationToken.None));

        Assert.Contains("Only failed finished-goods receipt requests", exception.Message, StringComparison.Ordinal);
        Assert.Empty(receipt.GetDomainEvents());
    }

    [Fact]
    public async Task Retry_finished_goods_receipt_inventory_posting_returns_partial_failure_to_posted_after_inventory_posts_remaining_quantity()
    {
        await using var dbContext = CreateDbContext(nameof(Retry_finished_goods_receipt_inventory_posting_returns_partial_failure_to_posted_after_inventory_posts_remaining_quantity));
        var receipt = FinishedGoodsReceiptRequest.Create(
            "org-001",
            "env-dev",
            "FGR-001",
            "WO-001",
            "SKU-FG",
            8m,
            "PCS",
            DateTimeOffset.Parse("2026-06-15T09:00:00Z"),
            "LOT-FG-001",
            null,
            12.34m);
        receipt.MarkInventoryPosted("INV-MOV-PARTIAL", 3m, DateTimeOffset.Parse("2026-06-15T09:05:00Z"));
        receipt.MarkInventoryPostingFailed(
            "inventory.validation.failed",
            "remaining posting rejected",
            DateTimeOffset.Parse("2026-06-15T09:10:00Z"));
        receipt.ClearDomainEvents();
        dbContext.FinishedGoodsReceiptRequests.Add(receipt);
        await dbContext.SaveChangesAsync();

        await new RetryFinishedGoodsReceiptInventoryPostingCommandHandler(dbContext).Handle(
            new RetryFinishedGoodsReceiptInventoryPostingCommand("org-001", "env-dev", "FGR-001", "retry-remaining-001"),
            CancellationToken.None);

        var postedHandler = new StockMovementPostedIntegrationEventHandlerForMarkMesReceiptPosted(
            dbContext,
            new InMemoryIntegrationEventDeadLetterStore());
        await postedHandler.HandleAsync(CreatePostedEvent("FGR-001", quantity: 5m), CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Equal(FinishedGoodsReceiptRequest.PostedStatus, receipt.Status);
        Assert.Equal(8m, receipt.PostedQuantity);
        Assert.Equal(0m, receipt.RemainingQuantity);
    }

    [Fact]
    public async Task Stock_movement_posting_failed_consumer_ignores_cancelled_finished_goods_receipt()
    {
        await using var dbContext = CreateDbContext(nameof(Stock_movement_posting_failed_consumer_ignores_cancelled_finished_goods_receipt));
        var receipt = FinishedGoodsReceiptRequest.Create(
            "org-001",
            "env-dev",
            "FGR-001",
            "WO-001",
            "SKU-FG",
            8m,
            "PCS",
            DateTimeOffset.Parse("2026-06-15T09:00:00Z"),
            "LOT-FG-001",
            null);
        receipt.Cancel();
        dbContext.FinishedGoodsReceiptRequests.Add(receipt);
        await dbContext.SaveChangesAsync();

        var failedHandler = new StockMovementPostingFailedIntegrationEventHandlerForMarkMesRequestFailed(
            dbContext,
            new InMemoryIntegrationEventDeadLetterStore());
        await failedHandler.HandleAsync(CreateFailedEvent("FGR-001"), CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Equal(FinishedGoodsReceiptRequest.CancelledStatus, receipt.Status);
        Assert.Null(receipt.InventoryPostingFailureCode);
    }

    [Fact]
    public void Finished_goods_receipt_keeps_partial_posting_evidence_when_remaining_posting_fails()
    {
        var receipt = FinishedGoodsReceiptRequest.Create(
            "org-001",
            "env-dev",
            "FGR-001",
            "WO-001",
            "SKU-FG",
            8m,
            "PCS",
            DateTimeOffset.Parse("2026-06-15T09:00:00Z"),
            "LOT-FG-001",
            null,
            12.34m);
        var postedAtUtc = DateTimeOffset.Parse("2026-06-15T09:05:00Z");
        receipt.MarkInventoryPosted("INV-MOV-PARTIAL", 3m, postedAtUtc);

        receipt.MarkInventoryPostingFailed(
            "inventory.validation.failed",
            "remaining posting rejected",
            DateTimeOffset.Parse("2026-06-15T09:10:00Z"));

        Assert.Equal(FinishedGoodsReceiptRequest.InventoryPostingFailedStatus, receipt.Status);
        Assert.Equal(3m, receipt.PostedQuantity);
        Assert.Equal(5m, receipt.RemainingQuantity);
        Assert.Equal("INV-MOV-PARTIAL", receipt.PostedInventoryMovementId);
        Assert.Equal(postedAtUtc, receipt.PostedAtUtc);
    }

    [Fact]
    public async Task Stock_movement_posted_consumer_persists_posted_status_without_external_save_changes()
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        await using (var seedContext = CreateDbContext(
            nameof(Stock_movement_posted_consumer_persists_posted_status_without_external_save_changes),
            databaseRoot))
        {
            seedContext.FinishedGoodsReceiptRequests.Add(FinishedGoodsReceiptRequest.Create(
                "org-001",
                "env-dev",
                "FGR-001",
                "WO-001",
                "SKU-FG",
                8m,
                "PCS",
                DateTimeOffset.Parse("2026-06-15T09:00:00Z"),
                "LOT-FG-001",
                null));
            await seedContext.SaveChangesAsync();
        }

        await using (var handlerContext = CreateDbContext(
            nameof(Stock_movement_posted_consumer_persists_posted_status_without_external_save_changes),
            databaseRoot))
        {
            var handler = new StockMovementPostedIntegrationEventHandlerForMarkMesReceiptPosted(
                handlerContext,
                new InMemoryIntegrationEventDeadLetterStore());

            await handler.HandleAsync(CreatePostedEvent("FGR-001"), CancellationToken.None);
        }

        await using var assertionContext = CreateDbContext(
            nameof(Stock_movement_posted_consumer_persists_posted_status_without_external_save_changes),
            databaseRoot);
        var receipt = await assertionContext.FinishedGoodsReceiptRequests.SingleAsync();
        Assert.Equal(FinishedGoodsReceiptRequest.PostedStatus, receipt.Status);
        Assert.Equal("INV-MOV-001", receipt.PostedInventoryMovementId);
    }

    [Fact]
    public async Task Stock_movement_posting_failed_consumer_persists_failure_without_external_save_changes()
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        await using (var seedContext = CreateDbContext(
            nameof(Stock_movement_posting_failed_consumer_persists_failure_without_external_save_changes),
            databaseRoot))
        {
            seedContext.FinishedGoodsReceiptRequests.Add(FinishedGoodsReceiptRequest.Create(
                "org-001",
                "env-dev",
                "FGR-001",
                "WO-001",
                "SKU-FG",
                8m,
                "PCS",
                DateTimeOffset.Parse("2026-06-15T09:00:00Z"),
                "LOT-FG-001",
                null));
            await seedContext.SaveChangesAsync();
        }

        await using (var handlerContext = CreateDbContext(
            nameof(Stock_movement_posting_failed_consumer_persists_failure_without_external_save_changes),
            databaseRoot))
        {
            var handler = new StockMovementPostingFailedIntegrationEventHandlerForMarkMesRequestFailed(
                handlerContext,
                new InMemoryIntegrationEventDeadLetterStore());

            await handler.HandleAsync(CreateFailedEvent("FGR-001"), CancellationToken.None);
        }

        await using var assertionContext = CreateDbContext(
            nameof(Stock_movement_posting_failed_consumer_persists_failure_without_external_save_changes),
            databaseRoot);
        var receipt = await assertionContext.FinishedGoodsReceiptRequests.SingleAsync();
        Assert.Equal(FinishedGoodsReceiptRequest.InventoryPostingFailedStatus, receipt.Status);
        Assert.Equal("inventory.validation.failed", receipt.InventoryPostingFailureCode);
    }

    private static StockMovementPostedIntegrationEvent CreatePostedEvent(
        string sourceDocumentId,
        string payloadSourceService = "business-mes",
        decimal quantity = 8m)
    {
        return new StockMovementPostedIntegrationEvent(
            "evt-inventory-posted-001",
            InventoryIntegrationEventTypes.StockMovementPosted,
            InventoryIntegrationEventVersions.V1,
            DateTimeOffset.Parse("2026-06-15T09:05:00Z"),
            InventoryIntegrationEventSources.BusinessInventory,
            "WO-001",
            sourceDocumentId,
            "org-001",
            "env-dev",
            "inventory",
            $"inventory:posted:{sourceDocumentId}",
            new StockMovementPostedPayload(
                "INV-MOV-001",
                "inbound",
                payloadSourceService,
                sourceDocumentId,
                "WO-001",
                $"mes:finished-goods-receipt:org-001:env-dev:{sourceDocumentId}",
                "SKU-FG",
                "PCS",
                "finished-goods",
                "receiving",
                "LOT-FG-001",
                null,
                "Unrestricted",
                "production",
                null,
                quantity,
                DateTimeOffset.Parse("2026-06-15T09:05:00Z"),
                null,
                null));
    }

    private static StockMovementPostingFailedIntegrationEvent CreateFailedEvent(string sourceDocumentId)
    {
        return new StockMovementPostingFailedIntegrationEvent(
            "evt-inventory-failed-001",
            InventoryIntegrationEventTypes.StockMovementPostingFailed,
            InventoryIntegrationEventVersions.V1,
            DateTimeOffset.Parse("2026-06-15T09:10:00Z"),
            InventoryIntegrationEventSources.BusinessInventory,
            "WO-001",
            sourceDocumentId,
            "org-001",
            "env-dev",
            "system:business-inventory",
            $"inventory:failed:{sourceDocumentId}",
            new StockMovementPostingFailedPayload(
                "inbound",
                InventoryIntegrationEventSources.BusinessMes,
                sourceDocumentId,
                "WO-001",
                $"mes:finished-goods-receipt:org-001:env-dev:{sourceDocumentId}",
                "SKU-FG",
                "PCS",
                "finished-goods",
                "receiving",
                "LOT-FG-001",
                null,
                "Unrestricted",
                "production",
                null,
                8m,
                "inventory.validation.failed",
                "posting rejected",
                DateTimeOffset.Parse("2026-06-15T09:10:00Z")));
    }

    private static ApplicationDbContext CreateDbContext(string databaseName)
    {
        return CreateDbContext(databaseName, new InMemoryDatabaseRoot());
    }

    private static ApplicationDbContext CreateDbContext(string databaseName, InMemoryDatabaseRoot databaseRoot)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
