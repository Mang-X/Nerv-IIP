using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Messaging.CAP;

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

        await handler.HandleAsync(CreatePostedEvent("FGR-001", quantity: 3m), CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var receipt = await dbContext.FinishedGoodsReceiptRequests.SingleAsync();
        Assert.Equal(FinishedGoodsReceiptRequest.RequestedStatus, receipt.Status);
        Assert.Null(receipt.PostedInventoryMovementId);
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
