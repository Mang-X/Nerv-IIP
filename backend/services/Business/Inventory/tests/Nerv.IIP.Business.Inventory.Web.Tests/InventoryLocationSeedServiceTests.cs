using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLedgerAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLocationAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockMovementAggregate;
using Nerv.IIP.Business.Inventory.Infrastructure;
using Nerv.IIP.Business.Inventory.Web.Application.Queries;
using Nerv.IIP.Business.Inventory.Web.Application.Seed;

namespace Nerv.IIP.Business.Inventory.Web.Tests;

public sealed class InventoryLocationSeedServiceTests
{
    [Fact]
    public async Task Seed_adds_the_four_mainline_locations_once_with_line_side_type()
    {
        await using var db = CreateDbContext();

        var firstWritten = await new InventoryLocationSeedService(db).SeedAsync("org-001", "env-dev");
        var secondWritten = await new InventoryLocationSeedService(db).SeedAsync("org-001", "env-dev");

        Assert.Equal(4, firstWritten);
        Assert.Equal(0, secondWritten);
        var locations = await db.StockLocations.OrderBy(x => x.LocationCode).ToArrayAsync();
        Assert.Equal(
            [
                ("loc-fg-01", "storage"),
                ("loc-line-01", "line-side"),
                ("loc-raw-01", "storage"),
                ("loc-semi-01", "storage"),
            ],
            locations.Select(x => (x.LocationCode, x.LocationType)).ToArray());
        Assert.All(locations, x =>
        {
            Assert.Equal("SITE-001", x.SiteCode);
            Assert.Equal("active", x.Status);
        });
    }

    [Fact]
    public async Task Seed_keeps_a_location_the_tenant_already_changed()
    {
        await using var db = CreateDbContext();
        db.StockLocations.Add(StockLocation.CreateOrUpdate(
            null, "org-001", "env-dev", "loc-line-01", "storage", "SITE-002", null, "inactive"));
        await db.SaveChangesAsync();

        var written = await new InventoryLocationSeedService(db).SeedAsync("org-001", "env-dev");

        Assert.Equal(3, written);
        var kept = await db.StockLocations.SingleAsync(x => x.LocationCode == "loc-line-01");
        Assert.Equal("storage", kept.LocationType);
        Assert.Equal("SITE-002", kept.SiteCode);
        Assert.Equal("inactive", kept.Status);
    }

    [Fact]
    public async Task Line_side_balance_query_reads_stock_on_the_seeded_line_side_location()
    {
        await using var db = CreateDbContext();
        await new InventoryLocationSeedService(db).SeedAsync("org-001", "env-dev");
        var ledger = StockLedger.Create(
            "org-001", "env-dev", "RM-001", "EA", "SITE-001", "loc-line-01",
            "LOT-1", null, "unrestricted", "company", null);
        ledger.ApplyMovement(StockMovement.Post(
            "org-001", "env-dev", "inbound", "test", "IN-1", null, "idem-1",
            "RM-001", "EA", "SITE-001", "loc-line-01", "LOT-1", null,
            "unrestricted", "company", null, 5m));
        db.StockLedgers.Add(ledger);
        await db.SaveChangesAsync();

        var result = await new ListLineSideInventoryBalancesQueryHandler(db, TimeProvider.System).Handle(
            new ListLineSideInventoryBalancesQuery("org-001", "env-dev"),
            CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal("loc-line-01", item.LocationCode);
        Assert.Equal(5m, item.OnHandQuantity);
    }

    [Fact]
    public async Task Location_list_returns_inactive_locations_with_type_and_status_scoped_to_the_tenant()
    {
        await using var db = CreateDbContext();
        db.StockLocations.AddRange(
            StockLocation.CreateOrUpdate(null, "org-001", "env-dev", "loc-line-01", "line-side", "SITE-001", null, "inactive"),
            StockLocation.CreateOrUpdate(null, "org-001", "env-dev", "loc-raw-01", "storage", "SITE-001", null, "active"),
            StockLocation.CreateOrUpdate(null, "org-001", "env-other", "loc-line-09", "line-side", "SITE-001", null, "active"));
        await db.SaveChangesAsync();

        var result = await new ListStockLocationsQueryHandler(db).Handle(
            new ListStockLocationsQuery("org-001", "env-dev", Keyword: "LINE"),
            CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal("loc-line-01", item.LocationCode);
        Assert.Equal("line-side", item.LocationType);
        Assert.Equal("inactive", item.Status);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"inventory-location-seed-{Guid.CreateVersion7():N}")
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification => Task.CompletedTask;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
