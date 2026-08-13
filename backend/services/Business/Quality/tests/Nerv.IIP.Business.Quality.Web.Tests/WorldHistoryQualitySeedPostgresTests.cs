using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Quality.Domain;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Web.Application.Seed;
using Nerv.IIP.Testing.PostgreSql;
using System.Diagnostics;
using Xunit.Abstractions;

namespace Nerv.IIP.Business.Quality.Web.Tests;

/// <summary>
/// 真实 PostgreSQL 下的 L1 背景历史（质量域侧）**全量**生成耗时与幂等实测。默认 skip；
/// 设置 <c>NERV_IIP_TEST_POSTGRES</c> 后运行（与其余 Quality Postgres profile 测试同一门禁）。
/// </summary>
public sealed class WorldHistoryQualitySeedPostgresTests(ITestOutputHelper output)
{
    private static readonly DateOnly AsOfDate = new(2026, 7, 26);

    /// <summary>质量侧的耗时预算：整条 L1 链路预算 5 分钟，质量域（约 7000 条检验）取 3 分钟。</summary>
    private const long BudgetMilliseconds = 180_000;

    [QualityPostgresFact]
    public async Task Full_scale_history_seed_stays_within_the_startup_budget_and_reruns_clean()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync(
            Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!,
            "nerv_quality_world_history");
        var connectionString = database.ConnectionString;
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddQualityPostgreSqlPersistence(connectionString);

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await DropQualitySchemaAsync(db);
        await db.Database.MigrateAsync();

        var seed = new WorldHistorySeedService(db);

        var stopwatch = Stopwatch.StartNew();
        var first = await seed.SeedAsync("org-001", "env-dev", AsOfDate, 1.0d);
        stopwatch.Stop();

        var rerun = Stopwatch.StartNew();
        var second = await seed.SeedAsync("org-001", "env-dev", AsOfDate, 1.0d);
        rerun.Stop();

        output.WriteLine($"quality-world-history-inspection-plans={first.InspectionPlansWritten}");
        output.WriteLine($"quality-world-history-inspection-tasks={first.InspectionTasksWritten}");
        output.WriteLine($"quality-world-history-inspection-records={first.InspectionRecordsWritten}");
        output.WriteLine($"quality-world-history-reinspections={first.ReinspectionRecordsWritten}");
        output.WriteLine($"quality-world-history-ncrs={first.NonconformanceReportsWritten}");
        output.WriteLine(FormattableString.Invariant(
            $"quality-world-history-nonconforming-rate={first.Validation.NonconformingRate:P3}"));
        output.WriteLine($"quality-world-history-first-run-ms={stopwatch.ElapsedMilliseconds}");
        output.WriteLine($"quality-world-history-idempotent-rerun-ms={rerun.ElapsedMilliseconds}");
        foreach (var line in first.Validation.Sample)
        {
            output.WriteLine($"quality-world-history-sample: {line}");
        }

        // 设定集 §7：三条检验来源合计约 7000 条检验任务。
        Assert.InRange(first.InspectionTasksWritten, 6000, 8000);
        Assert.Equal(3, first.InspectionPlansWritten);
        Assert.Equal(0, second.InspectionPlansWritten);
        Assert.Equal(0, second.InspectionTasksWritten);
        Assert.Equal(0, second.InspectionRecordsWritten);
        Assert.Equal(0, second.NonconformanceReportsWritten);
        Assert.Equal(first.InspectionTasksWritten, await db.InspectionTasks.CountAsync());
        Assert.Equal(WorldHistoryConsistencyValidator.SampleSize, first.Validation.Sample.Count);
        Assert.True(
            stopwatch.ElapsedMilliseconds < BudgetMilliseconds,
            $"Quality world-history seed took {stopwatch.ElapsedMilliseconds} ms, exceeding the {BudgetMilliseconds} ms budget.");
    }

    private static async Task DropQualitySchemaAsync(ApplicationDbContext db)
    {
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"DROP SCHEMA IF EXISTS \"{QualityFacts.Schema}\" CASCADE";
        await command.ExecuteNonQueryAsync();
    }
}
