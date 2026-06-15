using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InventoryMovementRequestAggregate;
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
                null,
                null,
                DateTimeOffset.UtcNow));
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
