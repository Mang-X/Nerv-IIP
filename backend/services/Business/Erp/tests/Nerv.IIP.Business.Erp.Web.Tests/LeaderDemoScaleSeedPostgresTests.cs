using System.Diagnostics;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Seed;
using Npgsql;
using Xunit.Abstractions;

namespace Nerv.IIP.Business.Erp.Web.Tests;

/// <summary>
/// 真实 PostgreSQL 下的领导演示规模块 ERP seed 耗时实测。默认 skip；设置 NERV_IIP_TEST_POSTGRES 后运行。
/// </summary>
public sealed class LeaderDemoScaleSeedPostgresTests(ITestOutputHelper output)
{
    private static readonly DateTimeOffset NowUtc = new(2026, 7, 26, 3, 14, 15, TimeSpan.Zero);

    [ErpScaleSeedRealPostgresFact]
    public async Task Scale_seed_persists_one_thousand_released_sales_orders_within_the_startup_budget()
    {
        await using var database = await TemporaryDatabase.CreateAsync(
            Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(database.ConnectionString)
            .Options;
        await using var db = new ApplicationDbContext(options, new ScalePostgresTestMediator());
        await db.Database.MigrateAsync(CancellationToken.None);
        var seed = new LeaderDemoScaleSeedService(db);

        var stopwatch = Stopwatch.StartNew();
        await seed.SeedAsync("org-001", "env-dev", 1000, NowUtc);
        stopwatch.Stop();
        var firstRunMilliseconds = stopwatch.ElapsedMilliseconds;

        var idempotentStopwatch = Stopwatch.StartNew();
        await seed.SeedAsync("org-001", "env-dev", 1000, NowUtc);
        idempotentStopwatch.Stop();

        output.WriteLine($"erp-scale-seed-first-run-ms={firstRunMilliseconds}");
        output.WriteLine($"erp-scale-seed-idempotent-rerun-ms={idempotentStopwatch.ElapsedMilliseconds}");

        Assert.Equal(1000, await db.SalesOrders.CountAsync(x => x.SalesOrderNo.StartsWith("SO-SCALE-")));
        Assert.Equal(1000, await db.Quotations.CountAsync(x => x.QuotationNo.StartsWith("QUO-SCALE-")));
        Assert.True(
            firstRunMilliseconds < 90_000,
            $"ERP scale seed took {firstRunMilliseconds} ms, which exceeds the 90 s leader-demo startup budget.");
    }

    private sealed class ScalePostgresTestMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification => Task.CompletedTask;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class TemporaryDatabase(string adminConnectionString, string databaseName, string connectionString)
        : IAsyncDisposable
    {
        public string ConnectionString { get; } = connectionString;

        public static async Task<TemporaryDatabase> CreateAsync(string baseConnectionString)
        {
            var databaseName = $"nerv_erp_scale_seed_{Guid.CreateVersion7():N}";
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
            return new TemporaryDatabase(adminConnectionString, databaseName, testConnectionString);
        }

        public async ValueTask DisposeAsync()
        {
            await using var connection = new NpgsqlConnection(adminConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)",
                connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}

internal sealed class ErpScaleSeedRealPostgresFactAttribute : FactAttribute
{
    public ErpScaleSeedRealPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")))
        {
            Skip = "Set NERV_IIP_TEST_POSTGRES to run the real PostgreSQL leader-demo scale seed measurement.";
        }
    }
}
