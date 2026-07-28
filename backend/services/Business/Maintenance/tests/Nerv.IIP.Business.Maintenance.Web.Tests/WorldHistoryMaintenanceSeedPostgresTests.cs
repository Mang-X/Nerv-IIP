using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Maintenance.Infrastructure;
using Nerv.IIP.Business.Maintenance.Web.Application.Seed;
using Npgsql;
using System.Diagnostics;
using Xunit.Abstractions;

namespace Nerv.IIP.Business.Maintenance.Web.Tests;

/// <summary>
/// 真实 PostgreSQL 下的 L1 设备域历史（Maintenance 侧）**全量**生成耗时与幂等实测。
/// 默认 skip；设置 <c>NERV_IIP_TEST_POSTGRES</c> 后运行（与 Maintenance 其余 Postgres 回归同一门禁与临时库配方）。
/// </summary>
public sealed class WorldHistoryMaintenanceSeedPostgresTests(ITestOutputHelper output)
{
    private const string PostgresConnectionStringEnvironmentVariable = "NERV_IIP_TEST_POSTGRES";

    private static readonly DateOnly AsOfDate = new(2026, 7, 26);

    /// <summary>Maintenance 侧只有千行级（工单/计划/点检），取 1 分钟预算。</summary>
    private const long BudgetMilliseconds = 60_000;

    [WorldHistoryRealPostgresFact]
    public async Task Full_scale_maintenance_history_seed_stays_within_the_startup_budget_and_reruns_clean()
    {
        var postgresConnectionString = Environment.GetEnvironmentVariable(PostgresConnectionStringEnvironmentVariable)!;
        await using var database = await TemporaryPostgresDatabase.CreateAsync(postgresConnectionString, "maintenance_world_history");

        await using (var migrationContext = CreatePostgresDbContext(database.ConnectionString))
        {
            await migrationContext.Database.MigrateAsync();
        }

        await using var db = CreatePostgresDbContext(database.ConnectionString);
        var seed = new WorldHistorySeedService(db);

        var stopwatch = Stopwatch.StartNew();
        var first = await seed.SeedAsync("org-001", "env-dev", AsOfDate, 1.0d);
        stopwatch.Stop();

        var rerun = Stopwatch.StartNew();
        var second = await seed.SeedAsync("org-001", "env-dev", AsOfDate, 1.0d);
        rerun.Stop();

        output.WriteLine($"maintenance-world-history-downtime-reasons={first.DowntimeReasonsWritten}");
        output.WriteLine($"maintenance-world-history-plans={first.MaintenancePlansWritten}");
        output.WriteLine($"maintenance-world-history-inspections={first.InspectionsWritten}");
        output.WriteLine($"maintenance-world-history-work-orders={first.WorkOrdersWritten}");
        output.WriteLine($"maintenance-world-history-spare-part-lines={first.SparePartLinesWritten}");
        output.WriteLine($"maintenance-world-history-device-states={first.DeviceStatesWritten}");
        output.WriteLine($"maintenance-world-history-open-work-orders={first.Validation.OpenWorkOrders}");
        output.WriteLine($"maintenance-world-history-completed-downtime-minutes={first.Validation.CompletedDowntimeMinutes}");
        output.WriteLine($"maintenance-world-history-first-run-ms={stopwatch.ElapsedMilliseconds}");
        output.WriteLine($"maintenance-world-history-idempotent-rerun-ms={rerun.ElapsedMilliseconds}");
        foreach (var line in first.Validation.Sample)
        {
            output.WriteLine($"maintenance-world-history-sample: {line}");
        }

        // 设定集 §7 量级：维修工单约 120 张；点检/保养计划 92 条；
        // 四期：备件消耗行约 120 条（约 1.1 行/完工单）；设备状态投影 46 台全覆盖。
        Assert.InRange(first.WorkOrdersWritten, 90, 150);
        Assert.Equal(92, first.MaintenancePlansWritten);
        Assert.InRange(first.SparePartLinesWritten, 90, 180);
        Assert.Equal(46, first.DeviceStatesWritten);

        Assert.Equal(0, second.DowntimeReasonsWritten);
        Assert.Equal(0, second.MaintenancePlansWritten);
        Assert.Equal(0, second.InspectionsWritten);
        Assert.Equal(0, second.WorkOrdersWritten);
        Assert.Equal(0, second.SparePartLinesWritten);
        Assert.Equal(0, second.DeviceStatesWritten);

        Assert.True(
            stopwatch.ElapsedMilliseconds < BudgetMilliseconds,
            $"Maintenance world-history seed took {stopwatch.ElapsedMilliseconds} ms, exceeding the {BudgetMilliseconds} ms budget.");
    }

    private static ApplicationDbContext CreatePostgresDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "maintenance"))
            .Options;
        return new ApplicationDbContext(options, new WorldHistoryPostgresNoopMediator());
    }

    private sealed class WorldHistoryPostgresNoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification => Task.CompletedTask;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class TemporaryPostgresDatabase : IAsyncDisposable
    {
        private readonly string adminConnectionString;
        private readonly string databaseName;

        private TemporaryPostgresDatabase(string adminConnectionString, string connectionString, string databaseName)
        {
            this.adminConnectionString = adminConnectionString;
            ConnectionString = connectionString;
            this.databaseName = databaseName;
        }

        public string ConnectionString { get; }

        public static async Task<TemporaryPostgresDatabase> CreateAsync(string baseConnectionString, string prefix)
        {
            var baseBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString);
            var adminBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString)
            {
                Database = string.IsNullOrWhiteSpace(baseBuilder.Database) ? "postgres" : baseBuilder.Database,
            };
            var databaseName = $"nerv_iip_{prefix}_{Guid.CreateVersion7():N}";
            var databaseBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString)
            {
                Database = databaseName,
            };

            await using var connection = new NpgsqlConnection(adminBuilder.ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"""CREATE DATABASE "{databaseName}";""", connection);
            await command.ExecuteNonQueryAsync();
            return new TemporaryPostgresDatabase(adminBuilder.ConnectionString, databaseBuilder.ConnectionString, databaseName);
        }

        public async ValueTask DisposeAsync()
        {
            NpgsqlConnection.ClearAllPools();
            await using var connection = new NpgsqlConnection(adminConnectionString);
            await connection.OpenAsync();
            await using (var terminate = new NpgsqlCommand(
                "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @databaseName AND pid <> pg_backend_pid();",
                connection))
            {
                terminate.Parameters.AddWithValue("databaseName", databaseName);
                await terminate.ExecuteNonQueryAsync();
            }

            await using var drop = new NpgsqlCommand($"""DROP DATABASE IF EXISTS "{databaseName}";""", connection);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private sealed class WorldHistoryRealPostgresFactAttribute : FactAttribute
    {
        public WorldHistoryRealPostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgresConnectionStringEnvironmentVariable)))
            {
                Skip = $"Set {PostgresConnectionStringEnvironmentVariable} to run the real PostgreSQL Maintenance world-history seed test.";
            }
        }
    }
}
