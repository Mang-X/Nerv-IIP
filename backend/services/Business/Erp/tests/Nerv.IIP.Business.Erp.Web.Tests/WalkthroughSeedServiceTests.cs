using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.QuotationAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.RequestForQuotationAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Seed;

namespace Nerv.IIP.Business.Erp.Web.Tests;

public sealed class WalkthroughSeedServiceTests
{
    [Fact]
    public async Task Seed_provides_quotes_for_all_purchased_finished_materials_and_rod_raw_material()
    {
        await using var db = CreateDbContext();
        await new WalkthroughSeedService(db).SeedAsync("org-001", "env-dev");

        // NERV-2113：FG 的 11 项外购需求，以及混合场景的活塞杆原料。
        string[] requiredSkus =
        [
            "SF-ROD-01", "SF-TUB-01", "SF-VLV-01", "RM-SPR-05", "RM-SEL-01",
            "RM-OIL-01", "RM-ACC-01", "RM-ACC-04", "RM-ACC-07", "PK-BOX-01",
            "PK-LBL-03", "RM-BAR-01",
        ];
        var quotes = await db.SupplierQuotations.Include(x => x.Lines).ToArrayAsync();
        foreach (var sku in requiredSkus)
        {
            var line = Assert.Single(quotes.SelectMany(x => x.Lines), x => x.SkuCode == sku);
            Assert.True(line.UnitPrice > 0);
            Assert.Equal(sku == "RM-BAR-01" ? "kg" : sku == "RM-OIL-01" ? "l" : "pcs", line.UomCode);
        }
    }

    [Fact]
    public async Task Seed_creates_only_auditable_price_sources_and_is_idempotent()
    {
        await using var db = CreateDbContext();
        var seed = new WalkthroughSeedService(db);

        await seed.SeedAsync("org-001", "env-dev");
        await seed.SeedAsync("org-001", "env-dev");

        var salesQuote = Assert.Single(await db.Quotations.Include(x => x.Lines).ToArrayAsync());
        Assert.Equal(QuotationStatus.Approved, salesQuote.Status);
        Assert.Equal(WalkthroughSeedSpec.SalesUnitPrice, Assert.Single(salesQuote.Lines).UnitPrice);
        Assert.Equal(2, await db.RequestForQuotations.CountAsync());
        Assert.Equal(13, await db.SupplierQuotations.CountAsync());
        Assert.Empty(await db.PurchaseOrders.ToArrayAsync());
        Assert.Empty(await db.SalesOrders.ToArrayAsync());
        Assert.Empty(await db.PurchaseReceipts.ToArrayAsync());
        Assert.Empty(await db.DeliveryOrders.ToArrayAsync());
        Assert.True(WalkthroughSeedSpec.SalesUnitPrice > WalkthroughSeedSpec.AuditablePurchaseCost);
    }

    [Fact]
    public async Task Seed_existing_rfq_with_drifted_line_rejects_collision()
    {
        await using var db = CreateDbContext();
        db.RequestForQuotations.Add(RequestForQuotation.Create(
            "org-001",
            "env-dev",
            WalkthroughSeedSpec.RfqNo,
            WalkthroughSeedSpec.PurchasePrices.Select(x => x.SupplierCode),
            WalkthroughSeedSpec.PurchasePrices.Select((price, index) => new RfqLineDraft(
                $"{(index + 1) * 10}",
                index == 0 ? "RM-DRIFT-01" : price.SkuCode,
                price.UomCode,
                price.Quantity,
                WalkthroughSeedSpec.SiteCode,
                WalkthroughSeedSpec.ValidUntil))));
        await db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new WalkthroughSeedService(db).SeedAsync("org-001", "env-dev"));

        Assert.Contains(WalkthroughSeedSpec.RfqNo, exception.Message, StringComparison.Ordinal);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"erp-walkthrough-{Guid.CreateVersion7():N}")
            .Options;
        return new ApplicationDbContext(options, new TestMediator());
    }

    private sealed class TestMediator : IMediator
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
