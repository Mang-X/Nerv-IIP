using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InventoryMovementRequestAggregate;
using Nerv.IIP.Business.Wms.Infrastructure;
using Nerv.IIP.Business.Wms.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Wms.Web.Tests;

public sealed class WmsStockMovementPostedConsumerTests
{
    [Fact]
    public async Task Stock_movement_posted_consumer_marks_matching_wms_request_posted()
    {
        await using var dbContext = CreateContext();
        var request = PendingRequest();
        dbContext.InventoryMovementRequests.Add(request);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new StockMovementPostedIntegrationEventHandlerForMarkWmsRequestPosted(
            dbContext,
            new InMemoryIntegrationEventDeadLetterStore());
        var integrationEvent = CreatePostedEvent("inventory-movement-001");

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await handler.HandleAsync(integrationEvent, CancellationToken.None);

        Assert.Equal(InventoryMovementRequestStatus.Posted, request.Status);
        Assert.Equal("inventory-movement-001", request.InventoryMovementId);
        Assert.NotNull(request.PostedAtUtc);
    }

    [Fact]
    public async Task Stock_movement_posted_consumer_ignores_non_wms_sources()
    {
        await using var dbContext = CreateContext();
        var request = PendingRequest();
        dbContext.InventoryMovementRequests.Add(request);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new StockMovementPostedIntegrationEventHandlerForMarkWmsRequestPosted(
            dbContext,
            new InMemoryIntegrationEventDeadLetterStore());

        await handler.HandleAsync(CreatePostedEvent("inventory-movement-erp", sourceService: "erp"), CancellationToken.None);

        Assert.Equal(InventoryMovementRequestStatus.Pending, request.Status);
        Assert.Null(request.InventoryMovementId);
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
                DateTimeOffset.UtcNow));
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"wms-stock-movement-posted-{Guid.NewGuid():N}")
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }
}
