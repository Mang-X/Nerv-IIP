using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Erp.Web.Application.Seed;
using Nerv.IIP.Contracts.Erp;
using NetCorePal.Extensions.DependencyInjection;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Erp.Web.Tests;

public sealed class SalesOrderDemandDemoSeedServiceTests
{
    [Fact]
    public async Task Seed_creates_released_real_site_sales_order_and_is_idempotent()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var seed = new SalesOrderDemandDemoSeedService(
            dbContext,
            scope.ServiceProvider.GetRequiredService<IErpIntegrationEventContextAccessor>());

        await seed.SeedAsync("org-001", "env-dev");
        await seed.SeedAsync("org-001", "env-dev");

        var order = await dbContext.SalesOrders.Include(x => x.Lines).SingleAsync();
        Assert.Equal(SalesOrderDemandDemoSeedService.SalesOrderNo, order.SalesOrderNo);
        Assert.Equal(SalesOrderDemandDemoSeedService.SiteCode, order.SiteCode);
        Assert.Equal("released", order.Status);
        Assert.Equal(1, order.Version);
        Assert.Equal(SalesOrderDemandDemoSeedService.SkuCode, Assert.Single(order.Lines).SkuCode);
        Assert.Single(await dbContext.Quotations.ToArrayAsync());

        var published = Assert.Single(scope.ServiceProvider
            .GetRequiredService<RecordingIntegrationEventPublisher>()
            .Published
            .OfType<SalesOrderReleasedIntegrationEvent>());
        Assert.Equal(SalesOrderDemandDemoSeedService.SalesOrderNo, published.Payload.SalesOrderNo);
        Assert.Equal("seed:man-517-demo", published.CausationId);
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddIntegrationEvents(typeof(Program));
        services.AddSingleton<RecordingIntegrationEventPublisher>();
        services.AddSingleton<IIntegrationEventPublisher>(provider =>
            provider.GetRequiredService<RecordingIntegrationEventPublisher>());
        services.AddSingleton<IErpIntegrationEventContextAccessor, SeedTestIntegrationEventContextAccessor>();
        services.AddDbContext<ApplicationDbContext>(options =>
            options
                .UseInMemoryDatabase($"erp-sales-order-demand-seed-{Guid.CreateVersion7():N}")
                .ConfigureWarnings(warnings => warnings.Ignore(
                    Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)));
        services.AddUnitOfWork<ApplicationDbContext>();
        return services.BuildServiceProvider();
    }

    private sealed class SeedTestIntegrationEventContextAccessor : IErpIntegrationEventContextAccessor
    {
        private ErpIntegrationEventContext? context;

        public ErpIntegrationEventContext GetContext() => context
            ?? throw new InvalidOperationException("The seed must establish an explicit integration-event context scope.");

        public IDisposable BeginScope(string causationId, string? correlationId = null, string? actor = null)
        {
            context = new ErpIntegrationEventContext(
                correlationId ?? causationId,
                causationId,
                actor ?? "system:test");
            return new SeedTestScope(this);
        }

        private sealed class SeedTestScope(SeedTestIntegrationEventContextAccessor owner) : IDisposable
        {
            public void Dispose()
            {
                owner.context = null;
            }
        }
    }

    private sealed class RecordingIntegrationEventPublisher : IIntegrationEventPublisher
    {
        public List<object> Published { get; } = [];

        Task IIntegrationEventPublisher.PublishAsync<TIntegrationEvent>(
            TIntegrationEvent integrationEvent,
            CancellationToken cancellationToken)
        {
            Published.Add(integrationEvent!);
            return Task.CompletedTask;
        }
    }
}
