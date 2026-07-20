using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionPlanAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Web.Application.Seed;

namespace Nerv.IIP.Business.Quality.Web.Tests;

public sealed class QualityLeaderDemoSeedServiceTests
{
    [Fact]
    public async Task Seed_creates_active_variable_operation_plan_once_without_results()
    {
        await using var db = CreateDbContext();
        var seed = new LeaderDemoSeedService(db);

        await seed.SeedAsync("org-001", "env-dev");
        await seed.SeedAsync("org-001", "env-dev");

        var plan = Assert.Single(await db.InspectionPlans.Include(x => x.Characteristics).ToArrayAsync());
        Assert.Equal("active", plan.Status);
        Assert.Equal("operation", plan.Category);
        Assert.Equal("SKU-DEMO-001", plan.SkuCode);
        Assert.Equal("WC-CNC-DEMO", plan.WorkCenterId);
        var characteristic = Assert.Single(plan.Characteristics);
        Assert.Equal(InspectionCharacteristicTypes.Variable, characteristic.CharacteristicType);
        Assert.Equal(50m, characteristic.NominalValue);
        Assert.Equal(49.5m, characteristic.LowerSpecLimit);
        Assert.Equal(50.5m, characteristic.UpperSpecLimit);
        Assert.Empty(await db.InspectionRecords.ToArrayAsync());
        Assert.Empty(await db.NonconformanceReports.ToArrayAsync());
    }

    [Fact]
    public async Task Seed_rejects_an_incompatible_reserved_plan()
    {
        await using var db = CreateDbContext();
        db.InspectionPlans.Add(InspectionPlan.Create(
            "org-001", "env-dev", LeaderDemoSeedService.PlanCode, "operation", "OTHER-SKU", null, "WC-CNC-DEMO", null, null));
        await db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new LeaderDemoSeedService(db).SeedAsync("org-001", "env-dev"));

        Assert.Contains(LeaderDemoSeedService.PlanCode, exception.Message, StringComparison.Ordinal);
        Assert.Equal("OTHER-SKU", (await db.InspectionPlans.SingleAsync()).SkuCode);
    }

    [Fact]
    public async Task Ordinary_quality_seed_does_not_create_leader_demo_plan()
    {
        await using var db = CreateDbContext();

        await new QualitySeedService(db).SeedAsync("org-001", "env-dev");

        Assert.Empty(await db.InspectionPlans.ToArrayAsync());
        Assert.Equal(7, await db.QualityReasons.CountAsync());
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"quality-leader-demo-{Guid.CreateVersion7():N}")
            .Options;
        return new ApplicationDbContext(options, new QualitySeedTestMediator());
    }

    private sealed class QualitySeedTestMediator : IMediator
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
