using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.ProductEngineering.Infrastructure;
using Nerv.IIP.Business.ProductEngineering.Web.Application.Seed;
using Npgsql;
using System.Diagnostics;
using Xunit.Abstractions;

namespace Nerv.IIP.Business.ProductEngineering.Web.Tests;

/// <summary>
/// 真实 PostgreSQL 下的《工厂世界观设定集》§4 工程主数据 seed 耗时与幂等实测。默认 skip；
/// 设置 <c>NERV_IIP_TEST_POSTGRES</c> 后运行。这是 leader-demo 启动耗时预算的第二个证据来源
/// （32 条 EBOM + 32 条 MBOM + 24 条 8 工序路线 + 32 个生产版本）。
/// </summary>
public sealed class WorldBibleSeedPostgresTests(ITestOutputHelper output)
{
    [WorldBiblePostgresFact]
    public async Task World_bible_seed_publishes_all_engineering_versions_within_the_startup_budget()
    {
        await using var database = await TemporaryDatabase.CreateAsync(
            Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(database.ConnectionString)
            .Options;
        await using var db = new ApplicationDbContext(options, new WorldBiblePostgresTestMediator());
        await db.Database.MigrateAsync(CancellationToken.None);

        var seed = new WorldBibleSeedService(db);
        var stopwatch = Stopwatch.StartNew();
        await seed.SeedAsync("org-001", "env-dev");
        stopwatch.Stop();
        var firstRunMilliseconds = stopwatch.ElapsedMilliseconds;

        var rerun = Stopwatch.StartNew();
        await seed.SeedAsync("org-001", "env-dev");
        rerun.Stop();

        output.WriteLine($"product-engineering-world-bible-seed-first-run-ms={firstRunMilliseconds}");
        output.WriteLine($"product-engineering-world-bible-seed-idempotent-rerun-ms={rerun.ElapsedMilliseconds}");

        Assert.Equal(32, await db.EngineeringBoms.CountAsync());
        Assert.Equal(32, await db.ManufacturingBoms.CountAsync());
        Assert.Equal(24, await db.Routings.CountAsync());
        Assert.Equal(32, await db.ProductionVersions.CountAsync());
        Assert.True(
            firstRunMilliseconds < 30_000,
            $"World-bible engineering seed took {firstRunMilliseconds} ms, which exceeds the 30 s budget for this block.");
    }

    private sealed class WorldBiblePostgresTestMediator : IMediator
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
            var databaseName = $"nerv_pe_world_bible_{Guid.CreateVersion7():N}";
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

internal sealed class WorldBiblePostgresFactAttribute : FactAttribute
{
    public WorldBiblePostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")))
        {
            Skip = "Set NERV_IIP_TEST_POSTGRES to run the real PostgreSQL world-bible engineering seed timing proof.";
        }
    }
}
