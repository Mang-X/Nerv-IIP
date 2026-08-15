using System.Diagnostics;
using System.Net;
using System.Text;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Seed;
using Nerv.IIP.ServiceAuth;
using Nerv.IIP.Testing.PostgreSql;
using Xunit.Abstractions;

namespace Nerv.IIP.Business.Mes.Web.Tests;

/// <summary>
/// 真实 PostgreSQL 下的领导演示规模块 seed 耗时实测。默认 skip；设置 NERV_IIP_TEST_POSTGRES 后运行。
/// 该测试同时是启动耗时的可复现证据来源：MES 规模块是整个规模块里最重的一段
/// （1000 张工单 + 4000 条工序任务）。
/// </summary>
public sealed class LeaderDemoScaleSeedPostgresTests(ITestOutputHelper output)
{
    private const string InternalServiceToken = "leader-demo-internal-token";
    private static readonly DateTimeOffset NowUtc = new(2026, 7, 26, 3, 14, 15, TimeSpan.Zero);

    [MesRealPostgresFact]
    public async Task Scale_seed_persists_one_thousand_released_work_orders_within_the_startup_budget()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync(
            Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!,
            "nerv_mes_scale_seed");
        try
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(database.ConnectionString)
                .Options;
            await using var db = new ApplicationDbContext(options, new ScalePostgresTestMediator());
            database.AssertOwns(db.Database.GetConnectionString());
            await db.Database.MigrateAsync(CancellationToken.None);

            var client = new HttpClient(new ScaleProductionVersionHandler())
            {
                BaseAddress = new Uri("http://product-engineering")
            };
            var seed = new LeaderDemoScaleSeedService(
                db,
                new MesProductEngineeringHttpClient(client),
                new TestInternalServiceTokenProvider(InternalServiceToken));

            var stopwatch = Stopwatch.StartNew();
            await seed.SeedAsync("org-001", "env-dev", 1000, NowUtc);
            stopwatch.Stop();
            var firstRunMilliseconds = stopwatch.ElapsedMilliseconds;

            var idempotentStopwatch = Stopwatch.StartNew();
            await seed.SeedAsync("org-001", "env-dev", 1000, NowUtc);
            idempotentStopwatch.Stop();

            output.WriteLine($"mes-scale-seed-first-run-ms={firstRunMilliseconds}");
            output.WriteLine($"mes-scale-seed-idempotent-rerun-ms={idempotentStopwatch.ElapsedMilliseconds}");

            Assert.Equal(1000, await db.WorkOrders.CountAsync(x => x.WorkOrderIdValue.StartsWith("WO-SCALE-")));
            Assert.Equal(4000, await db.OperationTasks.CountAsync(x => x.WorkOrderId.StartsWith("WO-SCALE-")));
            Assert.Empty(await db.ProductionReports.ToArrayAsync());
            // 90 秒是任务书给出的启动耗时上限；超过就必须调低默认订单数。
            Assert.True(
                firstRunMilliseconds < 90_000,
                $"MES scale seed took {firstRunMilliseconds} ms, which exceeds the 90 s leader-demo startup budget.");
        }
        finally
        {
            await database.DropAsync();
        }
    }

    private sealed class ScaleProductionVersionHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var query = request.RequestUri!.Query;
            var skuCode = LeaderDemoScaleSpec.FinishedSkuCodes
                .Single(code => query.Contains($"skuCode={code}", StringComparison.Ordinal));
            var index = Array.IndexOf(LeaderDemoScaleSpec.FinishedSkuCodes, skuCode) + 1;
            var json = $$"""
                {
                  "data": {
                    "productionVersionId": "019b03d4-fac4-7000-8000-00000000000{{index}}",
                    "organizationId": "org-001",
                    "environmentId": "env-dev",
                    "skuCode": "{{skuCode}}",
                    "mbomVersionId": "MBOM-SCALE-00{{index}}:1",
                    "routingVersionId": "ROUTING-SCALE-00{{index}}:1",
                    "effectiveDate": "2026-07-01",
                    "lotSize": 1,
                    "status": "active"
                  },
                  "success": true,
                  "message": "",
                  "code": 0
                }
                """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed record TestInternalServiceTokenProvider(string BearerToken) : IInternalServiceTokenProvider;

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

}
