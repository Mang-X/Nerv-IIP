using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.QuotationAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Seed;

namespace Nerv.IIP.Business.Erp.Web.Tests;

public sealed class WalkthroughSeedServiceTests
{
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
        Assert.Single(await db.RequestForQuotations.ToArrayAsync());
        Assert.Equal(5, await db.SupplierQuotations.CountAsync());
        Assert.Empty(await db.PurchaseOrders.ToArrayAsync());
        Assert.Empty(await db.SalesOrders.ToArrayAsync());
        Assert.Empty(await db.PurchaseReceipts.ToArrayAsync());
        Assert.Empty(await db.DeliveryOrders.ToArrayAsync());
        Assert.True(WalkthroughSeedSpec.SalesUnitPrice > WalkthroughSeedSpec.AuditablePurchaseCost);
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
