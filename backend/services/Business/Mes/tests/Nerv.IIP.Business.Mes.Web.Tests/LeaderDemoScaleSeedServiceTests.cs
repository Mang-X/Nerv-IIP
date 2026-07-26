using System.Net;
using System.Text;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Seed;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class LeaderDemoScaleSeedServiceTests
{
    private const string InternalServiceToken = "leader-demo-internal-token";
    private static readonly DateTimeOffset NowUtc = new(2026, 7, 26, 3, 14, 15, TimeSpan.Zero);

    [Fact]
    public async Task Scale_seed_releases_the_configured_work_orders_with_four_chained_operations_once()
    {
        await using var db = CreateDbContext();
        var handler = new ScaleProductionVersionHandler();
        var seed = CreateSeed(db, handler);

        await seed.SeedAsync("org-001", "env-dev", 250, NowUtc);
        await seed.SeedAsync("org-001", "env-dev", 250, NowUtc);

        var workOrders = await db.WorkOrders.Where(x => x.WorkOrderIdValue.StartsWith("WO-SCALE-")).ToArrayAsync();
        Assert.Equal(250, workOrders.Length);
        Assert.All(workOrders, workOrder =>
        {
            Assert.Equal(WorkOrder.ReleasedStatus, workOrder.Status);
            Assert.False(string.IsNullOrWhiteSpace(workOrder.ProductionVersionId));
            // 只写「工单下达」前置事实，不产生任何结果事实。
            Assert.Equal(0m, workOrder.CompletedQuantity);
            Assert.Equal(0m, workOrder.ScrapQuantity);
        });
        Assert.Equal(1000, await db.OperationTasks.CountAsync(x => x.WorkOrderId.StartsWith("WO-SCALE-")));
        Assert.Empty(await db.ProductionReports.ToArrayAsync());
        Assert.Empty(await db.FinishedGoodsReceiptRequests.ToArrayAsync());

        var first = await db.WorkOrders.SingleAsync(x => x.WorkOrderIdValue == "WO-SCALE-00001");
        Assert.Equal("SKU-SCALE-001", first.SkuId);
        Assert.Equal(20m, first.Quantity);
        Assert.Equal(new DateTimeOffset(2026, 8, 9, 18, 0, 0, TimeSpan.Zero), first.DueUtc);

        var operations = await db.OperationTasks
            .Where(x => x.WorkOrderId == "WO-SCALE-00001")
            .OrderBy(x => x.OperationSequence)
            .ToArrayAsync();
        Assert.Equal([10, 20, 30, 40], operations.Select(x => x.OperationSequence));
        Assert.Equal(
            ["WC-SCALE-WELD", "WC-SCALE-ROD", "WC-SCALE-SEAL", "WC-SCALE-TEST"],
            operations.Select(x => x.WorkCenterId));
        Assert.All(operations, operation => Assert.False(operation.RequiresQualityInspection));
        // 20 件 × 单件 1 分钟 + 各工序收尾 5/4/6/3 分钟。
        Assert.Equal(
            [TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(24), TimeSpan.FromMinutes(26), TimeSpan.FromMinutes(23)],
            operations.Select(x => x.Duration));

        // 每次 seed 对每个规模 SKU 只解析一次生产版本（6 个 SKU × 2 次 seed）。
        Assert.Equal(LeaderDemoScaleSpec.FinishedSkuCodes.Length * 2, handler.RequestCount);
    }

    [Fact]
    public async Task Scale_seed_is_disabled_when_the_configured_order_count_is_not_positive()
    {
        await using var db = CreateDbContext();
        var handler = new ScaleProductionVersionHandler();

        await CreateSeed(db, handler).SeedAsync("org-001", "env-dev", 0, NowUtc);

        Assert.Empty(await db.WorkOrders.ToArrayAsync());
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task Scale_seed_leaves_the_frozen_leader_demo_work_order_untouched()
    {
        await using var db = CreateDbContext();
        var frozen = WorkOrder.Create(
            "org-001", "env-dev", "WO-DEMO-Q01", "SKU-DEMO-001", "019b03d4-fac4-7000-8000-000000000099", 10m, 1,
            new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero), "pcs");
        var frozenOperations = frozen.Release(
            new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero),
            [new RoutingStepSnapshot("OP-DEMO-Q01-010", 10, "WC-CNC-DEMO", [], TimeSpan.FromMinutes(30), true, "OP-CNC-DEMO")]);
        db.WorkOrders.Add(frozen);
        db.OperationTasks.AddRange(frozenOperations);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await CreateSeed(db, new ScaleProductionVersionHandler()).SeedAsync("org-001", "env-dev", 120, NowUtc);

        var preserved = await db.WorkOrders.SingleAsync(x => x.WorkOrderIdValue == "WO-DEMO-Q01");
        Assert.Equal("SKU-DEMO-001", preserved.SkuId);
        Assert.Equal("019b03d4-fac4-7000-8000-000000000099", preserved.ProductionVersionId);
        Assert.Single(await db.OperationTasks.Where(x => x.WorkOrderId == "WO-DEMO-Q01").ToArrayAsync());
        Assert.Equal(120, await db.WorkOrders.CountAsync(x => x.WorkOrderIdValue.StartsWith("WO-SCALE-")));
    }

    [Fact]
    public async Task Scale_seed_fails_closed_when_the_scale_production_version_never_converges()
    {
        await using var db = CreateDbContext();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateSeed(db, new ScaleProductionVersionHandler(alwaysFail: true)).SeedAsync("org-001", "env-dev", 10, NowUtc));

        Assert.Contains("SKU-SCALE-001", exception.Message, StringComparison.Ordinal);
        Assert.Empty(await db.WorkOrders.ToArrayAsync());
    }

    [Theory]
    // 黄金向量：MES 与 ERP 必须对同一序号派生完全相同的 SKU / 数量 / 交期偏移 / 优先级。
    [InlineData(1, "SKU-SCALE-001", 20, 14, 2)]
    [InlineData(2, "SKU-SCALE-002", 30, 15, 3)]
    [InlineData(29, "SKU-SCALE-005", 50, 42, 100)]
    [InlineData(1000, "SKU-SCALE-004", 60, 27, 2)]
    public void Scale_order_distribution_stays_on_the_shared_golden_vector(
        int index,
        string skuCode,
        int quantity,
        int dueDayOffset,
        int priority)
    {
        Assert.Equal(skuCode, LeaderDemoScaleSpec.SkuCode(index));
        Assert.Equal(quantity, LeaderDemoScaleSpec.Quantity(index));
        Assert.Equal(dueDayOffset, LeaderDemoScaleSpec.DueDayOffset(index));
        Assert.Equal(priority, LeaderDemoScaleSpec.Priority(index));
    }

    private static LeaderDemoScaleSeedService CreateSeed(ApplicationDbContext db, ScaleProductionVersionHandler handler)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://product-engineering") };
        return new LeaderDemoScaleSeedService(
            db,
            new MesProductEngineeringHttpClient(client),
            new TestInternalServiceTokenProvider(InternalServiceToken));
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-leader-demo-scale-{Guid.CreateVersion7():N}")
            .Options;
        return new ApplicationDbContext(options, new ScaleSeedTestMediator());
    }

    private sealed class ScaleProductionVersionHandler(bool alwaysFail = false) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            Assert.Equal($"Bearer {InternalServiceToken}", request.Headers.Authorization?.ToString());
            if (alwaysFail)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }

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

    private sealed class ScaleSeedTestMediator : IMediator
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
