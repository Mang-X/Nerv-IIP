using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.MasterData.Infrastructure;
using Nerv.IIP.Business.MasterData.Web.Application.Seed;

namespace Nerv.IIP.Business.MasterData.Web.Tests;

public sealed class WalkthroughSeedServiceTests
{
    [Fact]
    public async Task Walkthrough_projection_is_small_explicit_and_idempotent()
    {
        Assert.Equal(20, WalkthroughSeedSpec.SkuCodes.Count);
        Assert.Equal(5, WalkthroughSeedSpec.CustomerCodes.Count);
        Assert.Equal(5, WalkthroughSeedSpec.SupplierCodes.Count);
        Assert.Equal(WalkthroughSeedSpec.SkuCodes.Count, WalkthroughSeedSpec.SkuCodes.Distinct(StringComparer.Ordinal).Count());

        await using var db = CreateDbContext();
        var seed = new WorldBibleSeedService(db);
        await seed.SeedWalkthroughAsync("org-001", "env-dev");
        await seed.SeedWalkthroughAsync("org-001", "env-dev");

        Assert.Equal(20, await db.Skus.CountAsync());
        Assert.Equal(10, await db.BusinessPartners.CountAsync());
        Assert.Equal(5, await db.BusinessPartners.CountAsync(x => x.PartnerType == "customer"));
        Assert.Equal(5, await db.BusinessPartners.CountAsync(x => x.PartnerType == "supplier"));
        Assert.Empty(await db.DeviceAssets.ToArrayAsync());
        Assert.Empty(await db.Workers.ToArrayAsync());
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"master-data-walkthrough-{Guid.CreateVersion7():N}")
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
