using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InventoryMovementRequestAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate;
using Nerv.IIP.Business.Wms.Infrastructure;
using Nerv.IIP.Business.Wms.Web.Application.Commands;
using Nerv.IIP.Business.Wms.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Wms.Web.Tests;

public sealed class WmsStockMovementPostedConsumerTests
{
    [Fact]
    public async Task Stock_movement_posted_consumer_marks_matching_wms_request_posted()
    {
        var databaseName = $"wms-stock-movement-posted-{Guid.NewGuid():N}";
        await using var dbContext = CreateContext(databaseName);
        var request = PendingRequest();
        dbContext.InventoryMovementRequests.Add(request);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new StockMovementPostedIntegrationEventHandlerForMarkWmsRequestPosted(
            new CommandExecutingSender(databaseName),
            new InMemoryIntegrationEventDeadLetterStore());
        var integrationEvent = CreatePostedEvent("inventory-movement-001");

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await handler.HandleAsync(integrationEvent, CancellationToken.None);

        await using var assertionContext = CreateContext(databaseName);
        var persistedRequest = await assertionContext.InventoryMovementRequests.SingleAsync(CancellationToken.None);
        Assert.Equal(InventoryMovementRequestStatus.Posted, persistedRequest.Status);
        Assert.Equal("inventory-movement-001", persistedRequest.InventoryMovementId);
        Assert.NotNull(persistedRequest.PostedAtUtc);
    }

    [Fact]
    public async Task Stock_movement_posted_consumer_completes_outbound_only_after_inventory_posts()
    {
        var databaseName = $"wms-stock-movement-outbound-posted-{Guid.NewGuid():N}";
        await using var dbContext = CreateContext(databaseName);
        var outbound = OutboundOrder.Create(
            "org-001",
            "env-dev",
            "DO-001",
            "erp-delivery-order",
            "DO-001",
            "finished-goods",
            [
                new OutboundOrderLineDraft(
                    "SO-LINE-001",
                    "SKU-FG-1000",
                    "kg",
                    4m,
                    "receiving",
                    "LOT-001",
                    null,
                    "unrestricted",
                    "production",
                    null),
            ]);
        outbound.CreatePickingTask("TASK-OUT-001", "SO-LINE-001", "receiving", "PACK-01", 4m);
        var movementRequest = Assert.Single(outbound.CompletePackReview("PACK-001", true, "idem-out-001"));

        Assert.Equal((OutboundOrderStatus)4, outbound.Status);
        Assert.Null(outbound.CompletedAtUtc);

        dbContext.OutboundOrders.Add(outbound);
        dbContext.InventoryMovementRequests.Add(movementRequest);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new StockMovementPostedIntegrationEventHandlerForMarkWmsRequestPosted(
            new CommandExecutingSender(databaseName),
            new InMemoryIntegrationEventDeadLetterStore());

        await handler.HandleAsync(CreateOutboundPostedEvent(), CancellationToken.None);

        await using var assertionContext = CreateContext(databaseName);
        var persistedOrder = await assertionContext.OutboundOrders.SingleAsync(CancellationToken.None);
        var persistedRequest = await assertionContext.InventoryMovementRequests.SingleAsync(CancellationToken.None);
        Assert.Equal(InventoryMovementRequestStatus.Posted, persistedRequest.Status);
        Assert.Equal(OutboundOrderStatus.Completed, persistedOrder.Status);
        Assert.NotNull(persistedOrder.CompletedAtUtc);
    }

    [Fact]
    public async Task Stock_movement_posted_consumer_ignores_non_wms_sources()
    {
        var databaseName = $"wms-stock-movement-posted-{Guid.NewGuid():N}";
        await using var dbContext = CreateContext(databaseName);
        var request = PendingRequest();
        dbContext.InventoryMovementRequests.Add(request);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new StockMovementPostedIntegrationEventHandlerForMarkWmsRequestPosted(
            new CommandExecutingSender(databaseName),
            new InMemoryIntegrationEventDeadLetterStore());

        await handler.HandleAsync(CreatePostedEvent("inventory-movement-erp", sourceService: "erp"), CancellationToken.None);

        await using var assertionContext = CreateContext(databaseName);
        var persistedRequest = await assertionContext.InventoryMovementRequests.SingleAsync(CancellationToken.None);
        Assert.Equal(InventoryMovementRequestStatus.Pending, persistedRequest.Status);
        Assert.Null(persistedRequest.InventoryMovementId);
    }

    [Fact]
    public async Task Stock_movement_posting_failed_consumer_marks_matching_wms_request_failed()
    {
        var databaseName = $"wms-stock-movement-failed-{Guid.NewGuid():N}";
        await using var dbContext = CreateContext(databaseName);
        var request = PendingRequest();
        dbContext.InventoryMovementRequests.Add(request);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new StockMovementPostingFailedIntegrationEventHandlerForMarkWmsRequestFailed(
            new CommandExecutingSender(databaseName),
            new InMemoryIntegrationEventDeadLetterStore());
        var integrationEvent = CreateFailedEvent("NEGATIVE_ON_HAND");

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await handler.HandleAsync(integrationEvent, CancellationToken.None);

        await using var assertionContext = CreateContext(databaseName);
        var persistedRequest = await assertionContext.InventoryMovementRequests.SingleAsync(CancellationToken.None);
        Assert.Equal(InventoryMovementRequestStatus.Failed, persistedRequest.Status);
        Assert.Equal("NEGATIVE_ON_HAND", persistedRequest.FailureCode);
        Assert.Contains("negative", persistedRequest.FailureMessage, StringComparison.OrdinalIgnoreCase);
    }

    private static InventoryMovementRequest PendingRequest()
    {
        return InventoryMovementRequest.Create(
            "org-001",
            "env-dev",
            "inbound",
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
            5m);
    }

    private static StockMovementPostedIntegrationEvent CreatePostedEvent(
        string inventoryMovementId,
        string sourceService = "wms")
    {
        return new StockMovementPostedIntegrationEvent(
            "evt-posted-001",
            InventoryIntegrationEventTypes.StockMovementPosted,
            InventoryIntegrationEventVersions.V1,
            DateTimeOffset.UtcNow,
            InventoryIntegrationEventSources.BusinessInventory,
            "corr-001",
            "cause-001",
            "org-001",
            "env-dev",
            "system:business-inventory",
            "inventory:stock-movement-posted:org-001:env-dev:wms:IN-001:idem-in-001",
            new StockMovementPostedPayload(
                inventoryMovementId,
                "inbound",
                sourceService,
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
                DateTimeOffset.UtcNow,
                null,
                null));
    }

    private static StockMovementPostingFailedIntegrationEvent CreateFailedEvent(string failureCode)
    {
        return new StockMovementPostingFailedIntegrationEvent(
            "evt-failed-001",
            InventoryIntegrationEventTypes.StockMovementPostingFailed,
            InventoryIntegrationEventVersions.V1,
            DateTimeOffset.UtcNow,
            InventoryIntegrationEventSources.BusinessInventory,
            "corr-001",
            "cause-001",
            "org-001",
            "env-dev",
            "system:business-inventory",
            "inventory:stock-movement-posting-failed:org-001:env-dev:wms:IN-001:idem-in-001",
            new StockMovementPostingFailedPayload(
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
                failureCode,
                "Stock movement would make on-hand quantity negative.",
                DateTimeOffset.UtcNow));
    }

    private static StockMovementPostedIntegrationEvent CreateOutboundPostedEvent()
    {
        return new StockMovementPostedIntegrationEvent(
            "evt-outbound-posted-001",
            InventoryIntegrationEventTypes.StockMovementPosted,
            InventoryIntegrationEventVersions.V1,
            DateTimeOffset.UtcNow,
            InventoryIntegrationEventSources.BusinessInventory,
            "corr-outbound-001",
            "cause-outbound-001",
            "org-001",
            "env-dev",
            "system:business-inventory",
            "inventory:stock-movement-posted:org-001:env-dev:wms:DO-001:idem-out-001",
            new StockMovementPostedPayload(
                "inventory-movement-outbound-001",
                "outbound",
                "wms",
                "DO-001",
                "SO-LINE-001",
                "idem-out-001",
                "SKU-FG-1000",
                "kg",
                "finished-goods",
                "receiving",
                "LOT-001",
                null,
                "unrestricted",
                "production",
                null,
                -4m,
                DateTimeOffset.UtcNow,
                null,
                null));
    }

    private static ApplicationDbContext CreateContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class CommandExecutingSender(string databaseName) : ISender
    {
        public async Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            if (request is MarkInventoryMovementRequestPostedCommand command)
            {
                await using var dbContext = CreateContext(databaseName);
                await new MarkInventoryMovementRequestPostedCommandHandler(dbContext).Handle(command, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            if (request is MarkInventoryMovementRequestFailedCommand failedCommand)
            {
                await using var dbContext = CreateContext(databaseName);
                await new MarkInventoryMovementRequestFailedCommandHandler(dbContext).Handle(failedCommand, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            throw new NotSupportedException($"Request type is not supported by this test sender: {request?.GetType().FullName}");
        }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("This test sender only supports command requests without responses.");
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
}
