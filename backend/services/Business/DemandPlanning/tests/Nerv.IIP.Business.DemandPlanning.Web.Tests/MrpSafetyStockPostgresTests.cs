using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.DemandPlanning.Domain;
using Nerv.IIP.Business.DemandPlanning.Infrastructure;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Commands;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Planning;
using Nerv.IIP.Testing.PostgreSql;

namespace Nerv.IIP.Business.DemandPlanning.Web.Tests;

public sealed class MrpSafetyStockPostgresTests
{
    [DemandPlanningRealPostgresFact]
    public async Task Run_mrp_persists_safety_stock_replenishment_and_pegging()
    {
        await using var database = await CreateDatabaseAsync();
        await using var context = CreateContext(database.ConnectionString);
        var handler = new RunMrpCommandHandler(context, new FixedSnapshotProvider());

        var result = await handler.Handle(
            new RunMrpCommand(
                "org-safety",
                "env-test",
                new DateOnly(2026, 6, 1),
                new DateOnly(2026, 6, 30)),
            CancellationToken.None);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        Assert.Equal(1, result.SuggestionCount);
        var suggestion = await context.PlanningSuggestions
            .AsNoTracking()
            .Include(x => x.PeggingLinks)
            .SingleAsync();
        Assert.Equal("planned-work-order", suggestion.SuggestionType);
        Assert.Equal("SKU-FG-1000", suggestion.SkuCode);
        Assert.Equal(14m, suggestion.Quantity);
        Assert.Equal(10m, suggestion.GrossDemandQuantity);
        Assert.Equal(8m, suggestion.OnHandQuantity);
        Assert.Equal(0m, suggestion.AvailableToNetQuantity);
        Assert.Equal(12m, suggestion.SafetyStockQuantity);
        Assert.Equal(14m, suggestion.NetRequirementQuantity);
        Assert.Equal(14m, suggestion.PlannedQuantity);
        Assert.Equal("10 - 0 - 0 + 4 safety-stock = 14", suggestion.Formula);

        var safetyPegging = Assert.Single(suggestion.PeggingLinks, x => x.PeggingType == "safety-stock");
        Assert.Equal("safety-stock", safetyPegging.SourceType);
        Assert.Equal(4m, safetyPegging.Quantity);
        Assert.Equal(0m, safetyPegging.GrossDemandQuantity);
    }

    [DemandPlanningRealPostgresFact]
    public async Task Run_mrp_persists_bounded_stable_safety_reference_for_maximum_identifiers()
    {
        var skuCode = new string('S', 64);
        var siteCode = new string('T', 64);
        await using var database = await CreateDatabaseAsync();
        await using var context = CreateContext(database.ConnectionString);
        var handler = new RunMrpCommandHandler(context, new FixedSnapshotProvider(skuCode, siteCode));

        await handler.Handle(
            new RunMrpCommand(
                "org-safety",
                "env-test",
                new DateOnly(2026, 6, 1),
                new DateOnly(2026, 6, 30)),
            CancellationToken.None);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var suggestion = await context.PlanningSuggestions
            .AsNoTracking()
            .Include(x => x.PeggingLinks)
            .SingleAsync();
        var safetyPegging = Assert.Single(suggestion.PeggingLinks, x => x.SourceType == "safety-stock");
        Assert.Equal(
            "safety-stock:b3dd7569784af7a2f7f80e08edd192c0c6c85f3ab3fdeb1ec12a848dad29be43",
            safetyPegging.DemandSourceReference);
        Assert.StartsWith("safety-stock:", safetyPegging.DemandSourceReference, StringComparison.Ordinal);
        Assert.NotEmpty(safetyPegging.DemandSourceReference);
        Assert.True(safetyPegging.DemandSourceReference.Length <= 128);
    }

    private static Task<PostgreSqlTestDatabase> CreateDatabaseAsync()
    {
        return PostgreSqlTestDatabase.CreateAsync(
            Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!,
            "nerv_demand_planning_safety",
            async (connectionString, cancellationToken) =>
            {
                await using var migrationContext = CreateContext(connectionString);
                await migrationContext.Database.MigrateAsync(cancellationToken);
            });
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

    private sealed class FixedSnapshotProvider : IPlanningInputSnapshotProvider
    {
        private readonly string skuCode;
        private readonly string siteCode;

        public FixedSnapshotProvider(
            string skuCode = "SKU-FG-1000",
            string siteCode = "SITE-01")
        {
            this.skuCode = skuCode;
            this.siteCode = siteCode;
        }

        public Task<PlanningInputSnapshotResult> GetSnapshotAsync(
            string organizationId,
            string environmentId,
            DateOnly horizonStart,
            DateOnly horizonEnd,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new PlanningInputSnapshotResult(
                "test-production-engineering",
                "test-inventory",
                [
                    new DemandSnapshot(
                        "SO-SAFETY-001",
                        skuCode,
                        "PCS",
                        siteCode,
                        10m,
                        new DateOnly(2026, 6, 10),
                        "sales-order"),
                ],
                [
                    new InventoryAvailabilitySnapshot(
                        skuCode,
                        "PCS",
                        siteCode,
                        8m,
                        8m,
                        0m),
                ],
                [
                    new ProductionVersionSnapshot(
                        skuCode,
                        "PV-SAFETY-001",
                        "BOM-SAFETY-001",
                        "ROUTING-SAFETY-001"),
                ],
                [],
                [],
                [
                    new PlanningParameterSnapshot(
                        skuCode,
                        "PCS",
                        siteCode,
                        0,
                        12m,
                        null,
                        null,
                        null,
                        ProcurementType: "make",
                        LotSizingPolicy: "lot-for-lot"),
                ],
                []));
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
