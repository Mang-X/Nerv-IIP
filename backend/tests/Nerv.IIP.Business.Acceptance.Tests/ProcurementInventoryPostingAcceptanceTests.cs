using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseReceiptAggregate;
using Nerv.IIP.Business.Erp.Domain.DomainEvents;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockMovements;
using Nerv.IIP.Business.Inventory.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;
using InventoryDbContext = Nerv.IIP.Business.Inventory.Infrastructure.ApplicationDbContext;

namespace Nerv.IIP.Business.Acceptance.Tests;

public sealed class ProcurementInventoryPostingAcceptanceTests
{
    [Fact]
    public async Task Erp_purchase_receipt_inventory_event_posts_inventory_on_hand()
    {
        await using var inventoryDb = CreateInventoryContext();
        var publisher = new RecordingIntegrationEventPublisher();
        var handler = CreateInventoryHandler(inventoryDb, publisher);
        var movementEvent = CreateErpReceiptMovementEvents(
                [new PurchaseReceiptLineDraft("LINE-001", 2m, "accepted", "RAW-A-01", "LOT-001")])
            .Single();

        await handler.HandleAsync(movementEvent, CancellationToken.None);

        var movement = Assert.Single(inventoryDb.StockMovements);
        Assert.Equal("inbound", movement.MovementType);
        Assert.Equal(InventoryIntegrationEventSources.BusinessErp, movement.SourceService);
        Assert.Equal("RCV-001", movement.SourceDocumentId);
        Assert.Equal("unrestricted", movement.QualityStatus);
        Assert.Equal(2m, movement.Quantity);
        var ledger = Assert.Single(inventoryDb.StockLedgers);
        Assert.Equal(2m, ledger.OnHandQuantity);
        Assert.Equal("unrestricted", ledger.QualityStatus);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task Erp_mixed_quality_purchase_receipt_posts_each_line_with_inventory_quality_status()
    {
        await using var inventoryDb = CreateInventoryContext();
        var publisher = new RecordingIntegrationEventPublisher();
        var handler = CreateInventoryHandler(inventoryDb, publisher);
        var movementEvents = CreateErpReceiptMovementEvents(
                [
                    new PurchaseReceiptLineDraft("LINE-001", 2m, "accepted", "RAW-A-01", "LOT-001"),
                    new PurchaseReceiptLineDraft("LINE-002", 3m, "rejected", "RAW-A-02", "LOT-002"),
                ],
                receiptNo: "RCV-MIXED")
            .OrderBy(x => x.Payload.SourceDocumentLineId, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["unrestricted", "blocked"], movementEvents.Select(x => x.Payload.QualityStatus).ToArray());

        foreach (var movementEvent in movementEvents)
        {
            await handler.HandleAsync(movementEvent, CancellationToken.None);
        }

        Assert.Equal(2, inventoryDb.StockMovements.Count());
        Assert.Equal(2m, inventoryDb.StockLedgers.Single(x => x.QualityStatus == "unrestricted").OnHandQuantity);
        Assert.Equal(3m, inventoryDb.StockLedgers.Single(x => x.QualityStatus == "blocked").OnHandQuantity);
        Assert.Empty(publisher.Published);
    }

    [Theory]
    [InlineData("purchase-receipt", "qualified")]
    [InlineData("inbound", "mixed")]
    public async Task Inventory_consumer_publishes_posting_failed_for_unknown_movement_or_quality(string movementType, string qualityStatus)
    {
        await using var inventoryDb = CreateInventoryContext();
        var publisher = new RecordingIntegrationEventPublisher();
        var handler = CreateInventoryHandler(inventoryDb, publisher);
        var movementEvent = CreateInventoryMovementRequestedEvent(movementType, qualityStatus);

        await handler.HandleAsync(movementEvent, CancellationToken.None);

        Assert.Empty(inventoryDb.StockMovements);
        Assert.Empty(inventoryDb.StockLedgers);
        var failedEvent = Assert.IsType<StockMovementPostingFailedIntegrationEvent>(Assert.Single(publisher.Published));
        Assert.Equal(InventoryIntegrationEventTypes.StockMovementPostingFailed, failedEvent.EventType);
        Assert.Equal(InventoryPostingFailureCodes.PostingRejected, failedEvent.Payload.FailureCode);
        Assert.Equal(movementType, failedEvent.Payload.MovementType);
        Assert.Equal(qualityStatus, failedEvent.Payload.QualityStatus);
    }

    private static InventoryMovementRequestedIntegrationEventHandlerForPostingMovement CreateInventoryHandler(
        InventoryDbContext inventoryDb,
        RecordingIntegrationEventPublisher publisher)
    {
        return new InventoryMovementRequestedIntegrationEventHandlerForPostingMovement(
            NullLogger<InventoryMovementRequestedIntegrationEventHandlerForPostingMovement>.Instance,
            new CommandExecutingSender(inventoryDb),
            new InMemoryIntegrationEventDeadLetterStore(),
            publisher);
    }

    private static IReadOnlyCollection<InventoryMovementRequestedIntegrationEvent> CreateErpReceiptMovementEvents(
        IReadOnlyCollection<PurchaseReceiptLineDraft> receiptLines,
        string receiptNo = "RCV-001")
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
        var receipt = PurchaseReceipt.Record(order, receiptNo, receiptLines);
        var converter = new PurchaseReceiptInventoryMovementRequestedIntegrationEventConverter();

        return receipt.GetDomainEvents()
            .OfType<PurchaseReceiptInventoryMovementRequestedDomainEvent>()
            .Select(converter.Convert)
            .ToArray();
    }

    private static InventoryMovementRequestedIntegrationEvent CreateInventoryMovementRequestedEvent(string movementType, string qualityStatus)
    {
        return new InventoryMovementRequestedIntegrationEvent(
            "evt-invalid-input",
            InventoryIntegrationEventTypes.InventoryMovementRequested,
            InventoryIntegrationEventVersions.V1,
            DateTimeOffset.UtcNow,
            InventoryIntegrationEventSources.BusinessErp,
            "corr-001",
            "cause-001",
            "org-001",
            "env-dev",
            "system:erp",
            $"erp:invalid:{movementType}:{qualityStatus}",
            new InventoryMovementRequestedPayload(
                movementType,
                InventoryIntegrationEventSources.BusinessErp,
                "RCV-INVALID",
                "LINE-001",
                $"idem-invalid-{movementType}-{qualityStatus}",
                "SKU-RM-1000",
                "kg",
                "SITE-01",
                "RAW-A-01",
                "LOT-001",
                null,
                qualityStatus,
                "company",
                null,
                2m,
                DateTimeOffset.UtcNow));
    }

    private static InventoryDbContext CreateInventoryContext()
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseInMemoryDatabase($"procurement-inventory-posting-{Guid.NewGuid():N}")
            .Options;
        return new InventoryDbContext(options, new NoopMediator());
    }

    private sealed class CommandExecutingSender(InventoryDbContext dbContext) : ISender
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
        public List<object> Published { get; } = [];

        Task IIntegrationEventPublisher.PublishAsync<TIntegrationEvent>(TIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        {
            Published.Add(integrationEvent!);
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
