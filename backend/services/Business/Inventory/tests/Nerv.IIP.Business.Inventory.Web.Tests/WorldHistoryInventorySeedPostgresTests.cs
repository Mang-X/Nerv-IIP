using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Inventory.Domain;
using Nerv.IIP.Business.Inventory.Infrastructure;
using Nerv.IIP.Business.Inventory.Web.Application.Seed;
using System.Diagnostics;
using Xunit.Abstractions;

namespace Nerv.IIP.Business.Inventory.Web.Tests;

/// <summary>
/// 真实 PostgreSQL 下的 L1 背景历史（库存域侧）**全量**生成耗时与幂等实测。默认 skip；
/// 设置 <c>NERV_IIP_TEST_POSTGRES</c> 后运行（与其余 Inventory Postgres profile 测试同一门禁）。
/// </summary>
public sealed class WorldHistoryInventorySeedPostgresTests(ITestOutputHelper output)
{
    private static readonly DateOnly AsOfDate = new(2026, 7, 26);

    /// <summary>库存侧的耗时预算：全量约 5 万笔流水（领料两笔 + 倒冲），整条 L1 链路里最重的一段。</summary>
    private const long BudgetMilliseconds = 300_000;

    [InventoryPostgresFact]
    public async Task Full_scale_history_seed_stays_within_the_startup_budget_and_reruns_clean()
    {
        var connectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!;
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddInventoryPostgreSqlPersistence(connectionString);

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await DropInventorySchemaAsync(db);
        await db.Database.MigrateAsync();

        var seed = new WorldHistorySeedService(db);

        var stopwatch = Stopwatch.StartNew();
        var first = await seed.SeedAsync("org-001", "env-dev", AsOfDate, 1.0d);
        stopwatch.Stop();

        var rerun = Stopwatch.StartNew();
        var second = await seed.SeedAsync("org-001", "env-dev", AsOfDate, 1.0d);
        rerun.Stop();

        output.WriteLine($"inventory-world-history-locations={first.StockLocationsWritten}");
        output.WriteLine($"inventory-world-history-movements={first.StockMovementsWritten}");
        output.WriteLine($"inventory-world-history-ledgers={first.StockLedgersCreated}");
        output.WriteLine($"inventory-world-history-distinct-lots={first.Validation.DistinctLotsChecked}");
        output.WriteLine(FormattableString.Invariant($"inventory-world-history-opening={first.Validation.OpeningQuantityTotal}"));
        output.WriteLine(FormattableString.Invariant($"inventory-world-history-inbound={first.Validation.InboundQuantityTotal}"));
        output.WriteLine(FormattableString.Invariant($"inventory-world-history-outbound={first.Validation.OutboundQuantityTotal}"));
        output.WriteLine(FormattableString.Invariant($"inventory-world-history-closing={first.Validation.ClosingQuantityTotal}"));
        output.WriteLine($"inventory-world-history-first-run-ms={stopwatch.ElapsedMilliseconds}");
        output.WriteLine($"inventory-world-history-idempotent-rerun-ms={rerun.ElapsedMilliseconds}");
        foreach (var line in first.Validation.Sample)
        {
            output.WriteLine($"inventory-world-history-sample: {line}");
        }

        Assert.Equal(WorldHistoryPhase2Spec.StockLocations.Count, first.StockLocationsWritten);
        Assert.Equal(0, second.StockLocationsWritten);
        Assert.Equal(0, second.StockMovementsWritten);
        Assert.Equal(0, second.StockLedgersCreated);
        Assert.Equal(first.StockMovementsWritten, await db.StockMovements.CountAsync());
        Assert.Equal(
            first.Validation.InboundQuantityTotal - first.Validation.OutboundQuantityTotal,
            first.Validation.ClosingQuantityTotal);
        Assert.Equal(WorldHistoryConsistencyValidator.SampleSize, first.Validation.Sample.Count);
        Assert.True(
            stopwatch.ElapsedMilliseconds < BudgetMilliseconds,
            $"Inventory world-history seed took {stopwatch.ElapsedMilliseconds} ms, exceeding the {BudgetMilliseconds} ms budget.");
    }

    private static async Task DropInventorySchemaAsync(ApplicationDbContext db)
    {
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"DROP SCHEMA IF EXISTS \"{InventoryFacts.Schema}\" CASCADE";
        await command.ExecuteNonQueryAsync();
    }
}
