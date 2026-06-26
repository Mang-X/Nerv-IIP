using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockMovements;
using Nerv.IIP.Business.Inventory.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Inventory.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InventoryMovementRequestAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.DomainEvents;
using Nerv.IIP.Business.Wms.Web.Application.Commands;
using Nerv.IIP.Business.Wms.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Wms.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;
using InventoryDbContext = Nerv.IIP.Business.Inventory.Infrastructure.ApplicationDbContext;
using WmsDbContext = Nerv.IIP.Business.Wms.Infrastructure.ApplicationDbContext;

namespace Nerv.IIP.Business.Acceptance.Tests;

public sealed class WmsInventoryMultilinePostingAcceptanceTests
{
    [Fact]
    public async Task Wms_multiline_inbound_completion_posts_every_line_to_inventory_and_marks_each_wms_request_posted()
    {
        await using var wmsDb = CreateWmsContext();
        await using var inventoryDb = CreateInventoryContext();
        var inventoryHandler = CreateInventoryHandler(inventoryDb);
        var wmsPostedHandler = CreateWmsPostedHandler(wmsDb);
        var inbound = InboundOrder.Create(
            "org-001",
            "env-dev",
            "IN-MULTI-001",
            "purchase-receipt",
            "PO-001",
            "SITE-01",
            [
                new InboundOrderLineDraft("LINE-001", "SKU-FG-1000", "kg", 5m, "LOC-A-01", "LOT-001", null, "qualified", "company", "owner-001"),
                new InboundOrderLineDraft("LINE-002", "SKU-RM-2000", "kg", 3m, "LOC-A-02", "LOT-002", null, "qualified", "company", "owner-001"),
            ]);
        wmsDb.InboundOrders.Add(inbound);
        await wmsDb.SaveChangesAsync(CancellationToken.None);

        await new CompleteInboundOrderCommandHandler(wmsDb).Handle(
            new CompleteInboundOrderCommand(inbound.Id, "idem-in-multi-001"),
            CancellationToken.None);
        await wmsDb.SaveChangesAsync(CancellationToken.None);

        var wmsRequests = wmsDb.InventoryMovementRequests
            .OrderBy(x => x.SourceDocumentLineId)
            .ToArray();
        Assert.Equal(2, wmsRequests.Length);
        Assert.Equal(new[] { "LINE-001", "LINE-002" }, wmsRequests.Select(x => x.SourceDocumentLineId).ToArray());
        await PostWmsRequestsToInventoryAndAckWmsAsync(wmsRequests, inventoryHandler, inventoryDb, wmsPostedHandler);

        Assert.Equal(2, inventoryDb.StockMovements.Count());
        Assert.Equal(5m, inventoryDb.StockLedgers.Single(x => x.SkuCode == "SKU-FG-1000" && x.LocationCode == "LOC-A-01").OnHandQuantity);
        Assert.Equal(3m, inventoryDb.StockLedgers.Single(x => x.SkuCode == "SKU-RM-2000" && x.LocationCode == "LOC-A-02").OnHandQuantity);
        Assert.All(wmsDb.InventoryMovementRequests, x => Assert.Equal(InventoryMovementRequestStatus.Posted, x.Status));
    }

    [Fact]
    public async Task Wms_multiline_outbound_completion_posts_every_line_to_inventory_and_marks_each_wms_request_posted()
    {
        await using var wmsDb = CreateWmsContext();
        await using var inventoryDb = CreateInventoryContext();
        var inventoryHandler = CreateInventoryHandler(inventoryDb);
        var wmsPostedHandler = CreateWmsPostedHandler(wmsDb);
        await SeedInventoryAsync(inventoryHandler, "SKU-FG-1000", "LOC-A-01", "LOT-001", 10m, "seed-out-001");
        await SeedInventoryAsync(inventoryHandler, "SKU-RM-2000", "LOC-A-02", "LOT-002", 7m, "seed-out-002");
        var outbound = OutboundOrder.Create(
            "org-001",
            "env-dev",
            "OUT-MULTI-001",
            "sales-delivery",
            "SO-001",
            "SITE-01",
            [
                new OutboundOrderLineDraft("LINE-001", "SKU-FG-1000", "kg", 4m, "LOC-A-01", "LOT-001", null, "qualified", "company", "owner-001"),
                new OutboundOrderLineDraft("LINE-002", "SKU-RM-2000", "kg", 2m, "LOC-A-02", "LOT-002", null, "qualified", "company", "owner-001"),
            ]);
        wmsDb.OutboundOrders.Add(outbound);
        await wmsDb.SaveChangesAsync(CancellationToken.None);

        await new CompleteOutboundOrderCommandHandler(wmsDb).Handle(
            new CompleteOutboundOrderCommand(outbound.Id, "PACK-001", true, "idem-out-multi-001"),
            CancellationToken.None);
        await wmsDb.SaveChangesAsync(CancellationToken.None);

        var wmsRequests = wmsDb.InventoryMovementRequests
            .Where(x => x.SourceDocumentId == "OUT-MULTI-001")
            .OrderBy(x => x.SourceDocumentLineId)
            .ToArray();
        Assert.Equal(2, wmsRequests.Length);
        Assert.Equal(new[] { "LINE-001", "LINE-002" }, wmsRequests.Select(x => x.SourceDocumentLineId).ToArray());
        await PostWmsRequestsToInventoryAndAckWmsAsync(wmsRequests, inventoryHandler, inventoryDb, wmsPostedHandler);

        Assert.Equal(4, inventoryDb.StockMovements.Count());
        Assert.Equal(6m, inventoryDb.StockLedgers.Single(x => x.SkuCode == "SKU-FG-1000" && x.LocationCode == "LOC-A-01").OnHandQuantity);
        Assert.Equal(5m, inventoryDb.StockLedgers.Single(x => x.SkuCode == "SKU-RM-2000" && x.LocationCode == "LOC-A-02").OnHandQuantity);
        Assert.All(wmsDb.InventoryMovementRequests.Where(x => x.SourceDocumentId == "OUT-MULTI-001"), x => Assert.Equal(InventoryMovementRequestStatus.Posted, x.Status));
    }

    private static async Task PostWmsRequestsToInventoryAndAckWmsAsync(
        IReadOnlyCollection<InventoryMovementRequest> wmsRequests,
        InventoryMovementRequestedIntegrationEventHandlerForPostingMovement inventoryHandler,
        InventoryDbContext inventoryDb,
        StockMovementPostedIntegrationEventHandlerForMarkWmsRequestPosted wmsPostedHandler)
    {
        var wmsConverter = new InventoryMovementRequestCreatedIntegrationEventConverter();
        foreach (var request in wmsRequests)
        {
            var inventoryRequestEvent = wmsConverter.Convert(new InventoryMovementRequestCreatedDomainEvent(request));
            await inventoryHandler.HandleAsync(inventoryRequestEvent, CancellationToken.None);
        }

        var inventoryPostedConverter = new StockMovementPostedIntegrationEventConverter(new StubInventoryIntegrationEventContextAccessor());
        foreach (var stockMovement in inventoryDb.StockMovements.OrderBy(x => x.SourceDocumentLineId))
        {
            if (!string.Equals(stockMovement.SourceService, "wms", StringComparison.OrdinalIgnoreCase)
                || !wmsRequests.Any(x => x.SourceDocumentId == stockMovement.SourceDocumentId && x.SourceDocumentLineId == stockMovement.SourceDocumentLineId))
            {
                continue;
            }

            var postedEvent = inventoryPostedConverter.Convert(new Nerv.IIP.Business.Inventory.Domain.DomainEvents.StockMovementPostedDomainEvent(stockMovement));
            await wmsPostedHandler.HandleAsync(postedEvent, CancellationToken.None);
        }
    }

    private static Task SeedInventoryAsync(
        InventoryMovementRequestedIntegrationEventHandlerForPostingMovement inventoryHandler,
        string skuCode,
        string locationCode,
        string lotNo,
        decimal quantity,
        string idempotencyKey)
    {
        var seedEvent = new InventoryMovementRequestedIntegrationEvent(
            $"evt-{idempotencyKey}",
            InventoryIntegrationEventTypes.InventoryMovementRequested,
            InventoryIntegrationEventVersions.V1,
            DateTimeOffset.UtcNow,
            InventoryIntegrationEventSources.BusinessWms,
            "corr-seed",
            "cause-seed",
            "org-001",
            "env-dev",
            "system:test",
            $"seed:{idempotencyKey}",
            new InventoryMovementRequestedPayload(
                "inbound",
                "wms",
                "SEED",
                idempotencyKey,
                idempotencyKey,
                skuCode,
                "kg",
                "SITE-01",
                locationCode,
                lotNo,
                null,
                "qualified",
                "company",
                "owner-001",
                quantity,
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

    private static StockMovementPostedIntegrationEventHandlerForMarkWmsRequestPosted CreateWmsPostedHandler(WmsDbContext wmsDb)
    {
        return new StockMovementPostedIntegrationEventHandlerForMarkWmsRequestPosted(
            new WmsCommandExecutingSender(wmsDb),
            new InMemoryIntegrationEventDeadLetterStore());
    }

    private static WmsDbContext CreateWmsContext()
    {
        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase($"wms-inventory-multiline-{Guid.NewGuid():N}")
            .Options;
        return new WmsDbContext(options, new NoopMediator());
    }

    private static InventoryDbContext CreateInventoryContext()
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseInMemoryDatabase($"wms-inventory-multiline-{Guid.NewGuid():N}")
            .Options;
        return new InventoryDbContext(options, new NoopMediator());
    }

    private sealed class InventoryCommandExecutingSender(InventoryDbContext dbContext) : ISender
    {
        public InventoryDbContext DbContext { get; } = dbContext;

        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is PostStockMovementCommand command)
            {
                var result = await new PostStockMovementCommandHandler(DbContext).Handle(command, cancellationToken);
                await DbContext.SaveChangesAsync(cancellationToken);
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

    private sealed class WmsCommandExecutingSender(WmsDbContext dbContext) : ISender
    {
        public async Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            if (request is MarkInventoryMovementRequestPostedCommand command)
            {
                await new MarkInventoryMovementRequestPostedCommandHandler(dbContext).Handle(command, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            throw new NotSupportedException($"Request type is not supported by this test sender: {request?.GetType().FullName}");
        }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("This test sender only supports commands without responses.");
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

    private sealed class StubInventoryIntegrationEventContextAccessor : IInventoryIntegrationEventContextAccessor
    {
        public InventoryIntegrationEventContext GetContext() => new("corr-001", "cause-001", "system:test");
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
