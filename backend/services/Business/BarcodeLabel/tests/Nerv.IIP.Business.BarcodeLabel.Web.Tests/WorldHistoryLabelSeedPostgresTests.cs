using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.BarcodeLabel.Domain;
using Nerv.IIP.Business.BarcodeLabel.Infrastructure;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Seed;
using Npgsql;
using System.Diagnostics;
using Xunit.Abstractions;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Tests;

/// <summary>
/// 真实 PostgreSQL 下的 L1 背景历史（条码标签域侧）**全量**生成耗时与幂等实测。默认 skip；
/// 设置 <c>NERV_IIP_TEST_POSTGRES</c> 后运行（与 <c>BarcodeLabelPostgresProfileTests</c> 同一门禁与临时库配方）。
/// </summary>
public sealed class WorldHistoryLabelSeedPostgresTests(ITestOutputHelper output)
{
    private const string PostgresConnectionStringEnvironmentVariable = "NERV_IIP_TEST_POSTGRES";

    private static readonly DateOnly AsOfDate = new(2026, 7, 26);

    /// <summary>条码域的耗时预算：整条 L1 链路预算 5 分钟，条码域（约 900 批次 + 3000 扫码）取 2 分钟。</summary>
    private const long BudgetMilliseconds = 120_000;

    [RealPostgresFact]
    public async Task Full_scale_history_seed_stays_within_the_startup_budget_and_reruns_clean()
    {
        var postgresConnectionString = Environment.GetEnvironmentVariable(PostgresConnectionStringEnvironmentVariable)!;
        await using var database = await TemporaryPostgresDatabase.CreateAsync(postgresConnectionString, "barcode_world_history");

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

        output.WriteLine($"label-world-history-templates={first.LabelTemplatesWritten}");
        output.WriteLine($"label-world-history-rules={first.BarcodeRulesWritten}");
        output.WriteLine($"label-world-history-print-batches={first.PrintBatchesWritten}");
        output.WriteLine($"label-world-history-print-items={first.PrintItemsWritten}");
        output.WriteLine($"label-world-history-epcis-events={first.EpcisEventsWritten}");
        output.WriteLine($"label-world-history-scans={first.ScanRecordsWritten}");
        output.WriteLine($"label-world-history-printed-batches={first.Validation.PrintedBatchesChecked}");
        output.WriteLine($"label-world-history-failed-batches={first.Validation.FailedBatchesChecked}");
        output.WriteLine($"label-world-history-accepted-scans={first.Validation.AcceptedScansChecked}");
        output.WriteLine($"label-world-history-rejected-scans={first.Validation.RejectedScansChecked}");
        output.WriteLine($"label-world-history-devices={first.Validation.DeviceFleetSize}");
        output.WriteLine($"label-world-history-first-run-ms={stopwatch.ElapsedMilliseconds}");
        output.WriteLine($"label-world-history-idempotent-rerun-ms={rerun.ElapsedMilliseconds}");
        foreach (var line in first.Validation.Sample)
        {
            output.WriteLine($"label-world-history-sample: {line}");
        }

        // 设定集 §7：标签模板 4 套、打印批次约 900。
        Assert.Equal(4, first.LabelTemplatesWritten);
        Assert.Equal(4, first.BarcodeRulesWritten);
        Assert.Equal(WorldHistoryLabelSpec.PrintBatchTarget, first.PrintBatchesWritten);
        Assert.Equal(WorldHistoryLabelSpec.ScanRecordTarget, first.ScanRecordsWritten);

        Assert.Equal(0, second.LabelTemplatesWritten);
        Assert.Equal(0, second.BarcodeRulesWritten);
        Assert.Equal(0, second.PrintBatchesWritten);
        Assert.Equal(0, second.ScanRecordsWritten);
        Assert.Equal(first.PrintBatchesWritten, await db.LabelPrintBatches.CountAsync());
        Assert.Equal(first.ScanRecordsWritten, await db.ScanRecords.CountAsync());
        Assert.Equal(WorldHistoryConsistencyValidator.SampleSize, first.Validation.Sample.Count);
        Assert.True(
            stopwatch.ElapsedMilliseconds < BudgetMilliseconds,
            $"BarcodeLabel world-history seed took {stopwatch.ElapsedMilliseconds} ms, exceeding the {BudgetMilliseconds} ms budget.");
    }

    private static ApplicationDbContext CreatePostgresDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", BarcodeLabelFacts.Schema))
            .Options;

        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class TemporaryPostgresDatabase : IAsyncDisposable
    {
        private TemporaryPostgresDatabase(string adminConnectionString, string connectionString, string databaseName)
        {
            AdminConnectionString = adminConnectionString;
            ConnectionString = connectionString;
            DatabaseName = databaseName;
        }

        public string ConnectionString { get; }

        private string AdminConnectionString { get; }

        private string DatabaseName { get; }

        public static async Task<TemporaryPostgresDatabase> CreateAsync(string baseConnectionString, string prefix)
        {
            var baseBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString);
            var adminBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString)
            {
                Database = string.IsNullOrWhiteSpace(baseBuilder.Database) ? "postgres" : baseBuilder.Database
            };
            var databaseName = $"{prefix}_{Guid.NewGuid():N}";
            var databaseBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString)
            {
                Database = databaseName
            };

            await using var connection = new NpgsqlConnection(adminBuilder.ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"""CREATE DATABASE "{databaseName}";""", connection);
            await command.ExecuteNonQueryAsync();

            return new TemporaryPostgresDatabase(adminBuilder.ConnectionString, databaseBuilder.ConnectionString, databaseName);
        }

        public async ValueTask DisposeAsync()
        {
            await using var connection = new NpgsqlConnection(AdminConnectionString);
            await connection.OpenAsync();
            await using (var terminate = new NpgsqlCommand(
                """
                SELECT pg_terminate_backend(pid)
                FROM pg_stat_activity
                WHERE datname = @databaseName AND pid <> pg_backend_pid();
                """,
                connection))
            {
                terminate.Parameters.AddWithValue("databaseName", DatabaseName);
                await terminate.ExecuteNonQueryAsync();
            }

            await using var drop = new NpgsqlCommand($"""DROP DATABASE IF EXISTS "{DatabaseName}";""", connection);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private sealed class RealPostgresFactAttribute : FactAttribute
    {
        public RealPostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgresConnectionStringEnvironmentVariable)))
            {
                Skip = $"Set {PostgresConnectionStringEnvironmentVariable} to run this real PostgreSQL BarcodeLabel world-history test.";
            }
        }
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("PostgreSQL world-history mediator cannot send requests.");

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException("PostgreSQL world-history mediator cannot send requests.");

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("PostgreSQL world-history mediator cannot send requests.");

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("PostgreSQL world-history mediator cannot stream requests.");

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("PostgreSQL world-history mediator cannot stream requests.");
    }
}
