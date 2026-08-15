using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Seed;
using Nerv.IIP.Testing.PostgreSql;
using System.Diagnostics;
using Xunit.Abstractions;

namespace Nerv.IIP.Business.Erp.Web.Tests;

/// <summary>
/// 真实 PostgreSQL 下的 L1 背景历史（ERP 侧）**全量**生成耗时与幂等实测。默认 skip；
/// 设置 <c>NERV_IIP_TEST_POSTGRES</c> 后运行。这是设定集 §8「生成耗时实测并支持缩放」的证据来源。
/// </summary>
public sealed class WorldHistorySeedPostgresTests(ITestOutputHelper output)
{
    private static readonly DateOnly AsOfDate = new(2026, 7, 26);

    /// <summary>ERP 侧的耗时预算：整条 L1 链路预算 5 分钟，销售 + 采购这段取 3 分钟。</summary>
    private const long BudgetMilliseconds = 180_000;

    [WorldHistoryPostgresFact]
    public async Task Full_scale_history_seed_stays_within_the_startup_budget_and_reruns_clean()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync(
            Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!,
            "nerv_erp_world_history");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(database.ConnectionString)
            .Options;
        await using var db = new ApplicationDbContext(options, new WorldHistoryPostgresTestMediator());
        await db.Database.MigrateAsync(CancellationToken.None);

        var seed = new WorldHistorySeedService(db);

        var stopwatch = Stopwatch.StartNew();
        var first = await seed.SeedAsync("org-001", "env-dev", AsOfDate, 1.0d);
        stopwatch.Stop();

        var rerun = Stopwatch.StartNew();
        var second = await seed.SeedAsync("org-001", "env-dev", AsOfDate, 1.0d);
        rerun.Stop();

        output.WriteLine($"erp-world-history-orders={first.SalesOrdersWritten}");
        output.WriteLine($"erp-world-history-purchase-orders={first.PurchaseOrdersWritten}");
        output.WriteLine($"erp-world-history-first-run-ms={stopwatch.ElapsedMilliseconds}");
        output.WriteLine($"erp-world-history-idempotent-rerun-ms={rerun.ElapsedMilliseconds}");
        output.WriteLine($"erp-world-history-deliveries={first.Validation.DeliveriesChecked}");
        output.WriteLine($"erp-world-history-receivables={first.Validation.ReceivablesChecked}");
        output.WriteLine($"erp-world-history-cash-receipts={first.Validation.CashReceiptsChecked}");
        output.WriteLine($"erp-world-history-vouchers={first.Validation.VouchersChecked}");
        foreach (var line in first.Validation.Sample)
        {
            output.WriteLine($"erp-world-history-sample: {line}");
        }

        // 设定集 §7：约 3200 单。
        Assert.InRange(first.SalesOrdersWritten, 2900, 3500);
        Assert.Equal(0, second.SalesOrdersWritten);
        Assert.Equal(0, second.PurchaseOrdersWritten);
        Assert.Equal(first.SalesOrdersWritten, await db.SalesOrders.CountAsync());
        Assert.Equal(WorldHistoryConsistencyValidator.SampleSize, first.Validation.Sample.Count);
        Assert.True(
            stopwatch.ElapsedMilliseconds < BudgetMilliseconds,
            $"ERP world-history seed took {stopwatch.ElapsedMilliseconds} ms, exceeding the {BudgetMilliseconds} ms budget.");
    }

    [WorldHistoryPostgresFact]
    public async Task Scaled_down_history_seed_produces_a_tenth_of_the_volume()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync(
            Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!,
            "nerv_erp_world_history_scaled");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(database.ConnectionString)
            .Options;
        await using var db = new ApplicationDbContext(options, new WorldHistoryPostgresTestMediator());
        await db.Database.MigrateAsync(CancellationToken.None);

        var stopwatch = Stopwatch.StartNew();
        var report = await new WorldHistorySeedService(db).SeedAsync("org-001", "env-dev", AsOfDate, 0.1d);
        stopwatch.Stop();

        output.WriteLine($"erp-world-history-scaled-orders={report.SalesOrdersWritten}");
        output.WriteLine($"erp-world-history-scaled-first-run-ms={stopwatch.ElapsedMilliseconds}");

        var full = WorldHistorySpec.TotalOrders(AsOfDate, 1.0d);
        Assert.InRange(report.SalesOrdersWritten, full / 15, full / 6);
    }

    private sealed class WorldHistoryPostgresTestMediator : IMediator
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

internal sealed class WorldHistoryPostgresFactAttribute : FactAttribute
{
    public WorldHistoryPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")))
        {
            Skip = "Set NERV_IIP_TEST_POSTGRES to run the real PostgreSQL world-history seed timing proof.";
        }
    }
}
