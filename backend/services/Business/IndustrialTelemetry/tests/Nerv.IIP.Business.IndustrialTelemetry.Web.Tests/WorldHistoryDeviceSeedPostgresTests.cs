using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.IndustrialTelemetry.Infrastructure;
using System.Diagnostics;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Seed;
using Nerv.IIP.Testing.PostgreSql;
using Xunit.Abstractions;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Tests;

/// <summary>
/// 真实 PostgreSQL 下的 L1 设备域历史（IndustrialTelemetry 侧）**全量**生成耗时与幂等实测。
/// 默认 skip；设置 <c>NERV_IIP_TEST_POSTGRES</c> 后运行（与其余 IIoT Postgres 回归同一门禁与临时库配方）。
/// </summary>
public sealed class WorldHistoryDeviceSeedPostgresTests(ITestOutputHelper output)
{
    private static readonly DateOnly AsOfDate = new(2026, 7, 26);

    /// <summary>整条 L1 链路预算 5 分钟，设备域遥测三层十万行级，取 3 分钟。</summary>
    private const long BudgetMilliseconds = 180_000;

    [RealPostgresFact]
    public async Task Full_scale_device_history_seed_stays_within_the_startup_budget_and_reruns_clean()
    {
        var postgresConnectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(postgresConnectionString, "nerv_iip_it");
        try
        {
            await using (var migrationContext = CreatePostgresDbContext(database.ConnectionString))
            {
                database.AssertOwns(migrationContext.Database.GetConnectionString());
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

            output.WriteLine($"device-world-history-alarm-rules={first.AlarmRulesWritten}");
            output.WriteLine($"device-world-history-alarm-events={first.AlarmEventsWritten}");
            output.WriteLine($"device-world-history-daily-rollups={first.DailyRollupsWritten}");
            output.WriteLine($"device-world-history-hourly-rollups={first.HourlyRollupsWritten}");
            output.WriteLine($"device-world-history-raw-samples={first.RawSamplesWritten}");
            output.WriteLine($"device-world-history-summaries={first.SummariesWritten}");
            output.WriteLine($"device-world-history-device-states={first.DeviceStateSnapshotsWritten}");
            output.WriteLine($"device-world-history-oee-facts={first.OeeFactsWritten}");
            output.WriteLine($"device-world-history-open-alarms={first.Validation.OpenAlarms}");
            output.WriteLine($"device-world-history-first-run-ms={stopwatch.ElapsedMilliseconds}");
            output.WriteLine($"device-world-history-idempotent-rerun-ms={rerun.ElapsedMilliseconds}");
            foreach (var line in first.Validation.Sample)
            {
                output.WriteLine($"device-world-history-sample: {line}");
            }

            // 设定集 §7 量级：报警约 400 起；96 个点位每个都有报警规则的仅限带规则点位。
            Assert.InRange(first.AlarmEventsWritten, 320, 460);
            Assert.True(first.DailyRollupsWritten > 10_000);

            Assert.Equal(0, second.AlarmRulesWritten);
            Assert.Equal(0, second.AlarmEventsWritten);
            Assert.Equal(0, second.DailyRollupsWritten);
            Assert.Equal(0, second.HourlyRollupsWritten);
            Assert.Equal(0, second.RawSamplesWritten);
            Assert.Equal(0, second.SummariesWritten);
            Assert.Equal(0, second.DeviceStateSnapshotsWritten);
            Assert.Equal(0, second.OeeFactsWritten);

            Assert.True(
                stopwatch.ElapsedMilliseconds < BudgetMilliseconds,
                $"IndustrialTelemetry world-history seed took {stopwatch.ElapsedMilliseconds} ms, exceeding the {BudgetMilliseconds} ms budget.");
        }
        finally
        {
            await database.DropAsync();
        }
    }

    private static ApplicationDbContext CreatePostgresDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "industrial_telemetry"))
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
}
