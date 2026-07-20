using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLedgerAggregate;
using Nerv.IIP.Business.Inventory.Infrastructure;
using Nerv.IIP.Business.Inventory.Web.Application.Seed;

namespace Nerv.IIP.Business.Inventory.Web.Tests;

public sealed class LeaderDemoSeedServiceTests
{
    [Fact]
    public async Task Seed_creates_available_raw_stock_once_and_no_finished_stock()
    {
        await using var db = CreateDbContext();
        var seed = new LeaderDemoSeedService(db);

        await seed.SeedAsync("org-001", "env-dev");
        await seed.SeedAsync("org-001", "env-dev");

        var ledger = Assert.Single(await db.StockLedgers.ToArrayAsync());
        Assert.Equal("SKU-DEMO-RM-001", ledger.SkuCode);
        Assert.Equal(100m, ledger.AvailableQuantity);
        Assert.Equal(0m, ledger.ReservedQuantity);
        Assert.Single(await db.StockMovements.ToArrayAsync());
        Assert.Empty(await db.StockLedgers.Where(x => x.SkuCode == "SKU-DEMO-001").ToArrayAsync());
    }

    [Fact]
    public async Task Seed_rejects_an_incompatible_reserved_stock_dimension()
    {
        await using var db = CreateDbContext();
        db.StockLedgers.Add(StockLedger.Create(
            "org-001", "env-dev", "SKU-DEMO-RM-001", "pcs", "SITE-001", LeaderDemoSeedService.LocationCode,
            null, null, StockQualityStatus.Blocked, StockOwnerType.Company, null));
        await db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new LeaderDemoSeedService(db).SeedAsync("org-001", "env-dev"));

        Assert.Contains("SKU-DEMO-RM-001", exception.Message, StringComparison.Ordinal);
        Assert.Equal(StockQualityStatus.Blocked, (await db.StockLedgers.SingleAsync()).QualityStatus);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"inventory-leader-demo-{Guid.CreateVersion7():N}")
            .Options;
        return new ApplicationDbContext(options, new InventorySeedTestMediator());
    }

    private sealed class InventorySeedTestMediator : IMediator
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
