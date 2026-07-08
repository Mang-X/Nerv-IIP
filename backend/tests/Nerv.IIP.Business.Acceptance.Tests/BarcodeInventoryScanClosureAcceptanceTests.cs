using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nerv.IIP.Business.BarcodeLabel.Domain.DomainEvents;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Commands.Scans;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockMovements;
using Nerv.IIP.Business.Inventory.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;
using BarcodeDbContext = Nerv.IIP.Business.BarcodeLabel.Infrastructure.ApplicationDbContext;
using InventoryDbContext = Nerv.IIP.Business.Inventory.Infrastructure.ApplicationDbContext;

namespace Nerv.IIP.Business.Acceptance.Tests;

public sealed class BarcodeInventoryScanClosureAcceptanceTests
{
    [Fact]
    public async Task Inventory_issue_barcode_scan_posts_real_inventory_outbound_once_for_duplicate_scan()
    {
        await using var barcodeDb = CreateBarcodeContext();
        await using var inventoryDb = CreateInventoryContext();
        var inventoryHandler = CreateInventoryHandler(inventoryDb);
        await SeedInventoryAsync(inventoryHandler);
        var scanHandler = new RecordScanCommandHandler(barcodeDb);

        var firstScanId = await scanHandler.Handle(NewInventoryIssueScanCommand("scan-issue-001"), CancellationToken.None);
        await barcodeDb.SaveChangesAsync(CancellationToken.None);
        var firstScan = await barcodeDb.ScanRecords.SingleAsync(x => x.Id == firstScanId, CancellationToken.None);
        var requestedEvent = new InventoryMovementRequestedFromBarcodeScanIntegrationEventConverter()
            .Convert(new InventoryMovementRequestedFromScanDomainEvent(firstScan));
        await inventoryHandler.HandleAsync(requestedEvent, CancellationToken.None);

        barcodeDb.ChangeTracker.Clear();
        var secondScanId = await scanHandler.Handle(NewInventoryIssueScanCommand("scan-issue-002"), CancellationToken.None);
        await barcodeDb.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(firstScanId, secondScanId);
        Assert.Equal(1, await barcodeDb.ScanRecords.CountAsync(CancellationToken.None));
        Assert.Equal(2, await inventoryDb.StockMovements.CountAsync(CancellationToken.None));
        var issueMovement = await inventoryDb.StockMovements.SingleAsync(x => x.SourceService == "barcode-label", CancellationToken.None);
        Assert.Equal("outbound", issueMovement.MovementType);
        Assert.Equal(-2m, issueMovement.Quantity);
        Assert.Equal(8m, inventoryDb.StockLedgers.Single().OnHandQuantity);
    }

    private static RecordScanCommand NewInventoryIssueScanCommand(string idempotencyKey)
    {
        return new RecordScanCommand(
            "org-001",
            "env-dev",
            "PDA-01",
            "(01)09506000134352(10)LOT-A(21)SN-0001(30)2",
            "inventory.issue",
            "OUT-SCAN-001",
            idempotencyKey,
            "accepted",
            null,
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            "LOC-A-01",
            "qualified",
            "company",
            "owner-001",
            2m);
    }

    private static Task SeedInventoryAsync(InventoryMovementRequestedIntegrationEventHandlerForPostingMovement inventoryHandler)
    {
        var seedEvent = new InventoryMovementRequestedIntegrationEvent(
            "evt-barcode-seed",
            InventoryIntegrationEventTypes.InventoryMovementRequested,
            InventoryIntegrationEventVersions.V1,
            DateTimeOffset.UtcNow,
            InventoryIntegrationEventSources.BusinessInventory,
            "corr-barcode-seed",
            "cause-barcode-seed",
            "org-001",
            "env-dev",
            "system:test",
            "barcode-seed:org-001:env-dev:SKU-FG-1000",
            new InventoryMovementRequestedPayload(
                "inbound",
                "inventory",
                "SEED-SCAN-STOCK",
                "LINE-001",
                "seed-scan-stock-001",
                "SKU-FG-1000",
                "kg",
                "SITE-01",
                "LOC-A-01",
                "LOT-A",
                "SN-0001",
                "qualified",
                "company",
                "owner-001",
                10m,
                DateTimeOffset.UtcNow));
        return inventoryHandler.HandleAsync(seedEvent, CancellationToken.None);
    }

    private static InventoryMovementRequestedIntegrationEventHandlerForPostingMovement CreateInventoryHandler(InventoryDbContext inventoryDb)
    {
        return new InventoryMovementRequestedIntegrationEventHandlerForPostingMovement(
            NullLogger<InventoryMovementRequestedIntegrationEventHandlerForPostingMovement>.Instance,
            new InventoryCommandExecutingSender(inventoryDb),
            new InMemoryIntegrationEventDeadLetterStore(),
            new RecordingIntegrationEventPublisher());
    }

    private static BarcodeDbContext CreateBarcodeContext()
    {
        var options = new DbContextOptionsBuilder<BarcodeDbContext>()
            .UseInMemoryDatabase($"barcode-inventory-scan-{Guid.NewGuid():N}")
            .Options;
        return new BarcodeDbContext(options, new NoopMediator());
    }

    private static InventoryDbContext CreateInventoryContext()
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseInMemoryDatabase($"barcode-inventory-scan-{Guid.NewGuid():N}")
            .Options;
        return new InventoryDbContext(options, new NoopMediator());
    }

    private sealed class InventoryCommandExecutingSender(InventoryDbContext dbContext) : ISender
    {
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is PostStockMovementCommand command)
            {
                var result = await new PostStockMovementCommandHandler(dbContext).Handle(command, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                return (TResponse)(object)result;
            }

            throw new NotSupportedException($"Request type is not supported by this test sender: {request.GetType().FullName}");
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            throw new NotSupportedException("This test sender only supports command requests with responses.");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("This test sender only supports typed command requests.");
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("This test sender does not support streams.");
        }

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("This test sender does not support streams.");
        }
    }

    private sealed class RecordingIntegrationEventPublisher : IIntegrationEventPublisher
    {
        Task IIntegrationEventPublisher.PublishAsync<TIntegrationEvent>(TIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
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
