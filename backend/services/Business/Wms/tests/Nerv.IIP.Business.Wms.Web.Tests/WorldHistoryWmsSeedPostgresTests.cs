using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Wms.Domain;
using Nerv.IIP.Business.Wms.Infrastructure;
using Nerv.IIP.Business.Wms.Web.Application.Seed;
using System.Diagnostics;
using Xunit.Abstractions;

namespace Nerv.IIP.Business.Wms.Web.Tests;

/// <summary>
/// 真实 PostgreSQL 下的 L1 背景历史（仓储域侧）**全量**生成耗时与幂等实测。默认 skip；
/// 设置 <c>NERV_IIP_TEST_POSTGRES</c> 后运行（与其余 WMS Postgres profile 测试同一门禁）。
/// </summary>
public sealed class WorldHistoryWmsSeedPostgresTests(ITestOutputHelper output)
{
    private static readonly DateOnly AsOfDate = new(2026, 7, 26);

    /// <summary>仓储侧的耗时预算：全量约 2.6 万张单据（每张一行一任务一过账请求）。</summary>
    private const long BudgetMilliseconds = 300_000;

    [WmsWorldHistoryPostgresFact]
    public async Task Full_scale_history_seed_stays_within_the_startup_budget_and_reruns_clean()
    {
        var connectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!;
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddWmsPostgreSqlPersistence(connectionString);

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await DropWmsSchemaAsync(db);
        await db.Database.MigrateAsync();

        var seed = new WorldHistorySeedService(db);

        var stopwatch = Stopwatch.StartNew();
        var first = await seed.SeedAsync("org-001", "env-dev", AsOfDate, 1.0d);
        stopwatch.Stop();

        var rerun = Stopwatch.StartNew();
        var second = await seed.SeedAsync("org-001", "env-dev", AsOfDate, 1.0d);
        rerun.Stop();

        output.WriteLine($"wms-world-history-inbound-orders={first.InboundOrdersWritten}");
        output.WriteLine($"wms-world-history-outbound-orders={first.OutboundOrdersWritten}");
        output.WriteLine($"wms-world-history-warehouse-tasks={first.WarehouseTasksWritten}");
        output.WriteLine($"wms-world-history-movement-requests={first.InventoryMovementRequestsWritten}");
        output.WriteLine($"wms-world-history-putaway-tasks={first.Validation.PutawayTasksChecked}");
        output.WriteLine($"wms-world-history-picking-tasks={first.Validation.PickingTasksChecked}");
        output.WriteLine($"wms-world-history-first-run-ms={stopwatch.ElapsedMilliseconds}");
        output.WriteLine($"wms-world-history-idempotent-rerun-ms={rerun.ElapsedMilliseconds}");
        foreach (var line in first.Validation.Sample)
        {
            output.WriteLine($"wms-world-history-sample: {line}");
        }

        Assert.Equal(0, second.InboundOrdersWritten);
        Assert.Equal(0, second.OutboundOrdersWritten);
        Assert.Equal(0, second.WarehouseTasksWritten);
        Assert.Equal(0, second.InventoryMovementRequestsWritten);
        Assert.Equal(first.InboundOrdersWritten, await db.InboundOrders.CountAsync());
        Assert.Equal(first.OutboundOrdersWritten, await db.OutboundOrders.CountAsync());
        Assert.Equal(first.WarehouseTasksWritten, await db.WarehouseTasks.CountAsync());
        Assert.Equal(WorldHistoryConsistencyValidator.SampleSize, first.Validation.Sample.Count);
        Assert.True(
            stopwatch.ElapsedMilliseconds < BudgetMilliseconds,
            $"WMS world-history seed took {stopwatch.ElapsedMilliseconds} ms, exceeding the {BudgetMilliseconds} ms budget.");
    }

    private static async Task DropWmsSchemaAsync(ApplicationDbContext db)
    {
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"DROP SCHEMA IF EXISTS \"{WmsFacts.Schema}\" CASCADE";
        await command.ExecuteNonQueryAsync();
    }
}

/// <summary>与其余 WMS Postgres 用例同一门禁：未设置 <c>NERV_IIP_TEST_POSTGRES</c> 时跳过。</summary>
public sealed class WmsWorldHistoryPostgresFactAttribute : FactAttribute
{
    public WmsWorldHistoryPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")))
        {
            Skip = "Set NERV_IIP_TEST_POSTGRES to run WMS PostgreSQL profile tests.";
        }
    }
}
