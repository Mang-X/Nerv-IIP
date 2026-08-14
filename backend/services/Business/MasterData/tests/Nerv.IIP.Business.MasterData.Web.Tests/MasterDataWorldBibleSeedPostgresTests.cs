using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.MasterData.Infrastructure;
using Nerv.IIP.Business.MasterData.Web.Application.Seed;
using Nerv.IIP.Testing.PostgreSql;
using System.Diagnostics;
using Xunit.Abstractions;

namespace Nerv.IIP.Business.MasterData.Web.Tests;

/// <summary>
/// 真实 PostgreSQL 下的《工厂世界观设定集》L0 主数据 seed 耗时与幂等实测。默认 skip；
/// 设置 <c>NERV_IIP_TEST_POSTGRES</c> 后运行。该测试是 leader-demo 启动耗时预算的证据来源：
/// L0 块是启动期写入行数最多的一段（约 250 行结构性主数据 + 58 人的班组/技能绑定）。
/// </summary>
public sealed class MasterDataWorldBibleSeedPostgresTests(ITestOutputHelper output)
{
    [PostgresFact]
    public async Task World_bible_seed_persists_the_full_l0_master_data_within_the_startup_budget()
    {
        var postgresConnectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(
            postgresConnectionString,
            "nerv_master_data_world_bible");
        try
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(database.ConnectionString)
                .Options;
            await using var db = new ApplicationDbContext(options, new WorldBiblePostgresTestMediator());
            database.AssertOwns(db.Database.GetConnectionString());
            await db.Database.MigrateAsync(CancellationToken.None);

            await new MasterDataSeedService(db).SeedAsync("org-001", "env-dev");
            await new LeaderDemoSeedService(db).SeedAsync("org-001", "env-dev");

            var seed = new WorldBibleSeedService(db);
            var stopwatch = Stopwatch.StartNew();
            await seed.SeedAsync("org-001", "env-dev");
            stopwatch.Stop();
            var firstRunMilliseconds = stopwatch.ElapsedMilliseconds;

            var rerun = Stopwatch.StartNew();
            await seed.SeedAsync("org-001", "env-dev");
            rerun.Stop();

            output.WriteLine($"master-data-world-bible-seed-first-run-ms={firstRunMilliseconds}");
            output.WriteLine($"master-data-world-bible-seed-idempotent-rerun-ms={rerun.ElapsedMilliseconds}");

            var worldBibleWorkshopCodes = WorldBibleSpec.Workshops.Select(x => x.Code).ToArray();
            Assert.Equal(
                worldBibleWorkshopCodes.Length,
                await db.Workshops.CountAsync(x => worldBibleWorkshopCodes.Contains(x.Code)));
            Assert.Equal(14, await db.ProductionLines.CountAsync(x => x.Code.StartsWith("LINE-WB-")));
            Assert.Equal(46, await db.DeviceAssets.CountAsync(x => !x.Code.Contains("DEMO") && !x.Code.Contains("SCALE")));
            Assert.Equal(84, await db.Skus.CountAsync(x => !x.Code.StartsWith("SKU-")));
            Assert.Equal(25, await db.TeamMembers.CountAsync(x => x.UserId.StartsWith("user-emp-")));

            // 固定演示事实不受影响。
            Assert.Null((await db.ProductionLines.SingleAsync(x => x.Code == "LINE-DEMO-01")).WorkshopCode);
            Assert.Single(await db.DeviceAssets.Where(x => x.Code == "DEV-CNC-DEMO").ToArrayAsync());

            // 30 秒是 L0 块单独占用的启动预算上限（整体 leader-demo 启动预算为 90 秒）。
            Assert.True(
                firstRunMilliseconds < 30_000,
                $"World-bible L0 seed took {firstRunMilliseconds} ms, which exceeds the 30 s budget for this block.");
        }
        finally
        {
            await database.DropAsync();
        }
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

}
