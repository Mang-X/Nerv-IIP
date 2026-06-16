using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLedgerAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockMovementAggregate;
using Nerv.IIP.Business.Inventory.Infrastructure;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockMovements;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockStatusTransfers;
using Nerv.IIP.Business.Inventory.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Inventory.Web.Tests;

public sealed class InventoryMovementRequestedConsumerTests
{
    [Fact]
    public async Task Movement_requested_consumer_executes_post_stock_movement_command()
    {
        await using var dbContext = CreateContext();
        var sender = new CommandExecutingSender(dbContext);
        var handler = new InventoryMovementRequestedIntegrationEventHandlerForPostingMovement(
            sender,
            new InMemoryIntegrationEventDeadLetterStore());

        await handler.HandleAsync(CreateRequestedEvent("evt-001"), CancellationToken.None);

        var movement = Assert.Single(dbContext.StockMovements);
        Assert.Equal("wms", movement.SourceService);
        Assert.Equal("IN-001", movement.SourceDocumentId);
        Assert.Equal("idem-in-001", movement.IdempotencyKey);
        Assert.Equal(5m, movement.Quantity);
    }

    [Fact]
    public async Task Duplicate_movement_requested_event_uses_inventory_command_idempotency()
    {
        await using var dbContext = CreateContext();
        var sender = new CommandExecutingSender(dbContext);
        var handler = new InventoryMovementRequestedIntegrationEventHandlerForPostingMovement(
            sender,
            new InMemoryIntegrationEventDeadLetterStore());
        var integrationEvent = CreateRequestedEvent("evt-duplicate");

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await handler.HandleAsync(integrationEvent, CancellationToken.None);

        Assert.Single(dbContext.StockMovements);
        Assert.Single(dbContext.StockLedgers);
        Assert.Equal(5m, dbContext.StockLedgers.Single().OnHandQuantity);
    }

    [Theory]
    [InlineData(QualityIntegrationEventTypes.InspectionPassed, "unrestricted")]
    [InlineData(QualityIntegrationEventTypes.InspectionRejected, "blocked")]
    public async Task Quality_inspection_result_consumer_transfers_quality_stock(string eventType, string targetStatus)
    {
        await using var dbContext = CreateContext();
        var ledger = StockLedger.Create(
            "org-001",
            "env-dev",
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            "LOC-A-01",
            "LOT-001",
            null,
            "quality",
            "company",
            "owner-001");
        ledger.ApplyMovement(StockMovement.Post(
            "org-001",
            "env-dev",
            "inbound",
            "wms",
            "IN-001",
            "LINE-001",
            "idem-quality-in-001",
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            "LOC-A-01",
            "LOT-001",
            null,
            "quality",
            "company",
            "owner-001",
            5m,
            2m));
        dbContext.StockLedgers.Add(ledger);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var sender = new CommandExecutingSender(dbContext);
        var handler = new QualityInspectionResultIntegrationEventHandlerForStockStatusTransfer(
            sender,
            dbContext,
            new InMemoryIntegrationEventDeadLetterStore());

        await handler.HandleAsync(CreateInspectionEvent(eventType), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Contains(dbContext.StockLedgers, x => x.QualityStatus == targetStatus && x.OnHandQuantity == 3m);
        Assert.Contains(dbContext.StockLedgers, x => x.QualityStatus == "quality" && x.OnHandQuantity == 2m);
        Assert.Equal(2, dbContext.StockMovements.Count(x => x.MovementType.StartsWith("status-transfer")));
    }

    [Fact]
    public async Task Quality_inspection_result_consumer_rejects_unsupported_event_type_to_dead_letter_store()
    {
        await using var dbContext = CreateContext();
        var sender = new CommandExecutingSender(dbContext);
        var deadLetterStore = new InMemoryIntegrationEventDeadLetterStore();
        var handler = new QualityInspectionResultIntegrationEventHandlerForStockStatusTransfer(
            sender,
            dbContext,
            deadLetterStore);

        await handler.HandleAsync(CreateInspectionEvent("quality.UnknownInspectionResult"), CancellationToken.None);

        Assert.Empty(dbContext.StockMovements);
        var deadLetters = await deadLetterStore.ListAsync(
            QualityInspectionResultIntegrationEventHandlerForStockStatusTransfer.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None);
        var deadLetter = Assert.Single(deadLetters);
        Assert.Equal("unexpected-event-type", deadLetter.FailureCode);
    }

    [Fact]
    public async Task Quality_inspection_result_consumer_returns_before_candidate_lookup_when_status_transfer_was_already_processed()
    {
        await using var dbContext = CreateContext();
        dbContext.StockMovements.Add(StockMovement.Post(
            "org-001",
            "env-dev",
            "status-transfer-out",
            "quality",
            "QI-001",
            "QI-001",
            "quality:inspection-result:org-001:env-dev:QI-001:quality.InspectionPassed:out",
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            "LOC-A-01",
            "LOT-001",
            null,
            "quality",
            "company",
            "owner-001",
            -3m));
        dbContext.StockMovements.Add(StockMovement.Post(
            "org-001",
            "env-dev",
            "status-transfer-in",
            "quality",
            "QI-001",
            "QI-001",
            "quality:inspection-result:org-001:env-dev:QI-001:quality.InspectionPassed:in",
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            "LOC-A-01",
            "LOT-001",
            null,
            "unrestricted",
            "company",
            "owner-001",
            3m));
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var sender = new CommandExecutingSender(dbContext);
        var handler = new QualityInspectionResultIntegrationEventHandlerForStockStatusTransfer(
            sender,
            dbContext,
            new InMemoryIntegrationEventDeadLetterStore());

        await handler.HandleAsync(CreateInspectionEvent(QualityIntegrationEventTypes.InspectionPassed), CancellationToken.None);

        Assert.Equal(2, dbContext.StockMovements.Count());
    }

    private static InventoryMovementRequestedIntegrationEvent CreateRequestedEvent(string eventId)
    {
        return new InventoryMovementRequestedIntegrationEvent(
            eventId,
            InventoryIntegrationEventTypes.InventoryMovementRequested,
            InventoryIntegrationEventVersions.V1,
            DateTimeOffset.UtcNow,
            InventoryIntegrationEventSources.BusinessWms,
            "corr-001",
            "cause-001",
            "org-001",
            "env-dev",
            "system:wms",
            "wms:inventory-movement-requested:org-001:env-dev:IN-001:idem-in-001",
            new InventoryMovementRequestedPayload(
                "inbound",
                "wms",
                "IN-001",
                "LINE-001",
                "idem-in-001",
                "SKU-FG-1000",
                "kg",
                "SITE-01",
                "LOC-A-01",
                "LOT-001",
                null,
                "qualified",
                "company",
                "owner-001",
                5m,
                DateTimeOffset.UtcNow));
    }

    private static InspectionResultIntegrationEvent CreateInspectionEvent(string eventType)
    {
        return new InspectionResultIntegrationEvent(
            "quality-event-001",
            eventType,
            QualityIntegrationEventVersions.V1,
            DateTimeOffset.UtcNow,
            QualityIntegrationEventSources.BusinessQuality,
            "corr-001",
            "cause-001",
            "org-001",
            "env-dev",
            "system:quality",
            $"quality:inspection-result:org-001:env-dev:QI-001:{eventType}",
            new InspectionResultPayload(
                "QI-001",
                "PLAN-001",
                "receiving",
                "quality",
                "QI-001",
                "SKU-FG-1000",
                3m,
                eventType == QualityIntegrationEventTypes.InspectionPassed ? "passed" : "rejected",
                null,
                [],
                DateTimeOffset.UtcNow));
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"inventory-movement-requested-{Guid.NewGuid():N}")
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class CommandExecutingSender(ApplicationDbContext dbContext) : ISender
    {
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is PostStockMovementCommand command)
            {
                var result = await new PostStockMovementCommandHandler(dbContext).Handle(command, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                return (TResponse)(object)result;
            }

            if (request is PostStockStatusTransferCommand statusTransferCommand)
            {
                var result = await new PostStockStatusTransferCommandHandler(dbContext).Handle(statusTransferCommand, cancellationToken);
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
