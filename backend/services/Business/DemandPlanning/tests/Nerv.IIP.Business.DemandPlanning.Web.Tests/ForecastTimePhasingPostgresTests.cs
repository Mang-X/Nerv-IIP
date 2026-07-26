using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.DemandPlanning.Domain;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.DemandSourceAggregate;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.ForecastInputAggregate;
using Nerv.IIP.Business.DemandPlanning.Infrastructure;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Planning;
using Nerv.IIP.Testing.PostgreSql;

namespace Nerv.IIP.Business.DemandPlanning.Web.Tests;

public sealed class ForecastTimePhasingPostgresTests
{
    [DemandPlanningRealPostgresFact]
    public async Task Forecast_time_phasing_postgres_conserves_adjacent_slices_and_preserves_ordinary_demand()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync(
            Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!,
            "nerv_demand_planning_forecast_phasing",
            async (connectionString, cancellationToken) =>
            {
                await using var migrationContext = CreateContext(connectionString);
                await migrationContext.Database.MigrateAsync(cancellationToken);
            });
        await using var context = CreateContext(database.ConnectionString);
        context.ForecastInputs.Add(ForecastInput.Create(
            "org-forecast",
            "env-test",
            "FC-POSTGRES-ADJACENT",
            "SKU-FG-1000",
            "pcs",
            "SITE-01",
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 6),
            6m,
            0,
            0));
        context.DemandSources.Add(DemandSource.CreateSalesOrderDemand(
            "org-forecast",
            "env-test",
            "sales-order-id-postgres",
            "SO-POSTGRES",
            "10",
            "CUST-001",
            "SKU-FG-1000",
            "pcs",
            "SITE-01",
            2m,
            new DateOnly(2026, 7, 5),
            1));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var provider = new DemandPlanningUpstreamInputSnapshotProvider(
            context,
            new EmptyEngineeringClient(),
            new EmptyInventoryClient());
        var first = await provider.GetSnapshotAsync(
            "org-forecast",
            "env-test",
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 3),
            CancellationToken.None);
        context.ChangeTracker.Clear();
        var second = await provider.GetSnapshotAsync(
            "org-forecast",
            "env-test",
            new DateOnly(2026, 7, 4),
            new DateOnly(2026, 7, 6),
            CancellationToken.None);

        var firstForecast = first.Demands.Where(x => x.SourceType == "forecast").ToArray();
        var secondForecast = second.Demands.Where(x => x.SourceType == "forecast").ToArray();
        Assert.Equal(
            [
                (new DateOnly(2026, 7, 1), 0.666667m),
                (new DateOnly(2026, 7, 2), 0.666666m),
                (new DateOnly(2026, 7, 3), 0.666667m),
            ],
            firstForecast.Select(x => (x.DueDate, x.Quantity)).ToArray());
        Assert.Equal(
            [
                (new DateOnly(2026, 7, 4), 0.666667m),
                (new DateOnly(2026, 7, 5), 0.666666m),
                (new DateOnly(2026, 7, 6), 0.666667m),
            ],
            secondForecast.Select(x => (x.DueDate, x.Quantity)).ToArray());
        Assert.Equal(4.000000m, firstForecast.Concat(secondForecast).Sum(x => x.Quantity));
        Assert.DoesNotContain(first.Demands, x => x.DemandSourceReference == "SO-POSTGRES");
        Assert.Equal(
            new DemandSnapshot(
                "SO-POSTGRES",
                "SKU-FG-1000",
                "pcs",
                "SITE-01",
                2m,
                new DateOnly(2026, 7, 5),
                "sales-order"),
            Assert.Single(second.Demands, x => x.DemandSourceReference == "SO-POSTGRES"));
    }

    private static ApplicationDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", DemandPlanningFacts.Schema))
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class EmptyEngineeringClient : IPlanningProductEngineeringSnapshotClient
    {
        public Task<PlanningProductEngineeringSnapshot> GetSnapshotAsync(
            string internalBearerToken,
            PlanningProductEngineeringSnapshotRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new PlanningProductEngineeringSnapshot("postgres-engineering:0", [], []));
        }
    }

    private sealed class EmptyInventoryClient : IPlanningInventorySnapshotClient
    {
        public Task<PlanningInventorySnapshot> GetAvailabilitySnapshotAsync(
            string internalBearerToken,
            PlanningInventorySnapshotRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new PlanningInventorySnapshot("postgres-inventory:0", []));
        }
    }

    private sealed class NoopMediator : IMediator
    {
        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(
            TRequest request,
            CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Publish(object notification, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task Publish<TNotification>(
            TNotification notification,
            CancellationToken cancellationToken = default)
            where TNotification : INotification =>
            Task.CompletedTask;
    }
}
