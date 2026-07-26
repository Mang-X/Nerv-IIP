using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Seed;
using Npgsql;
using System.Diagnostics;
using Xunit.Abstractions;

namespace Nerv.IIP.Business.Mes.Web.Tests;

/// <summary>
/// 真实 PostgreSQL 下的 L1 背景历史（MES 侧）**全量**生成耗时与幂等实测。默认 skip；
/// 设置 <c>NERV_IIP_TEST_POSTGRES</c> 后运行。
///
/// 生产版本解析走 HTTP，在这里换成确定性桩：本测试量的是数据库写入吞吐与一致性校验器的开销，
/// 解析路径本身由规模块既有测试覆盖。
/// </summary>
public sealed class WorldHistorySeedPostgresTests(ITestOutputHelper output)
{
    private static readonly DateOnly AsOfDate = new(2026, 7, 26);

    /// <summary>MES 侧的耗时预算：整条 L1 链路预算 5 分钟，工单侧（行数最多）取 4 分钟。</summary>
    private const long BudgetMilliseconds = 240_000;

    [WorldHistoryPostgresFact]
    public async Task Full_scale_history_seed_stays_within_the_startup_budget_and_reruns_clean()
    {
        await using var database = await WorldHistoryTemporaryDatabase.CreateAsync(
            Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!,
            "nerv_mes_world_history");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(database.ConnectionString)
            .Options;
        await using var db = new ApplicationDbContext(options, new WorldHistoryPostgresTestMediator());
        await db.Database.MigrateAsync(CancellationToken.None);

        var seed = new WorldHistorySeedService(db, new PostgresStubProductionVersionResolver());

        var stopwatch = Stopwatch.StartNew();
        var first = await seed.SeedAsync("org-001", "env-dev", AsOfDate, 1.0d);
        stopwatch.Stop();

        var rerun = Stopwatch.StartNew();
        var second = await seed.SeedAsync("org-001", "env-dev", AsOfDate, 1.0d);
        rerun.Stop();

        var totalWorkOrders = first.OrderWorkOrdersWritten + first.ReworkWorkOrdersWritten;
        output.WriteLine($"mes-world-history-order-work-orders={first.OrderWorkOrdersWritten}");
        output.WriteLine($"mes-world-history-rework-work-orders={first.ReworkWorkOrdersWritten}");
        output.WriteLine($"mes-world-history-total-work-orders={totalWorkOrders}");
        output.WriteLine($"mes-world-history-operation-tasks={first.Validation.OperationTasksChecked}");
        output.WriteLine($"mes-world-history-production-reports={first.Validation.ProductionReportsChecked}");
        output.WriteLine($"mes-world-history-finished-goods-receipts={first.Validation.FinishedGoodsReceiptsChecked}");
        output.WriteLine($"mes-world-history-material-issue-requests={await db.MaterialIssueRequests.CountAsync()}");
        output.WriteLine($"mes-world-history-material-requirements={await db.MaterialRequirements.CountAsync()}");
        output.WriteLine($"mes-world-history-first-run-ms={stopwatch.ElapsedMilliseconds}");
        output.WriteLine($"mes-world-history-idempotent-rerun-ms={rerun.ElapsedMilliseconds}");
        foreach (var line in first.Validation.Sample)
        {
            output.WriteLine($"mes-world-history-sample: {line}");
        }

        // 设定集 §7：约 3600 张工单（含内部补产）。
        Assert.InRange(totalWorkOrders, 3200, 4000);
        Assert.Equal(0, second.OrderWorkOrdersWritten);
        Assert.Equal(0, second.ReworkWorkOrdersWritten);
        Assert.Equal(totalWorkOrders, await db.WorkOrders.CountAsync());
        Assert.Equal(WorldHistoryConsistencyValidator.SampleSize, first.Validation.Sample.Count);
        Assert.True(
            stopwatch.ElapsedMilliseconds < BudgetMilliseconds,
            $"MES world-history seed took {stopwatch.ElapsedMilliseconds} ms, exceeding the {BudgetMilliseconds} ms budget.");
    }

    private sealed class PostgresStubProductionVersionResolver : IWorldHistoryProductionVersionResolver
    {
        public Task<IReadOnlyDictionary<string, string>> ResolveAsync(
            string organizationId,
            string environmentId,
            IReadOnlyCollection<string> skuCodes,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(
                skuCodes.ToDictionary(sku => sku, sku => $"PV-{sku}", StringComparer.Ordinal));
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

internal sealed class WorldHistoryTemporaryDatabase(string adminConnectionString, string databaseName, string connectionString)
    : IAsyncDisposable
{
    public string ConnectionString { get; } = connectionString;

    public static async Task<WorldHistoryTemporaryDatabase> CreateAsync(string baseConnectionString, string prefix)
    {
        var databaseName = $"{prefix}_{Guid.CreateVersion7():N}";
        var adminConnectionString = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            Database = "postgres"
        }.ConnectionString;
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", connection);
        await command.ExecuteNonQueryAsync();
        var testConnectionString = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            Database = databaseName
        }.ConnectionString;
        return new WorldHistoryTemporaryDatabase(adminConnectionString, databaseName, testConnectionString);
    }

    public async ValueTask DisposeAsync()
    {
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
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
