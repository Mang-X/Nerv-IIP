using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.QuotationAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SalesOrderAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Seed;
using NetCorePal.Extensions.DependencyInjection;

namespace Nerv.IIP.Business.Erp.Web.Tests;

/// <summary>
/// 《工厂世界观设定集》L1 背景历史引擎（ERP 侧）的形状、确定性、隔离性与幂等性证据。
/// 真实 PostgreSQL 的全量耗时实测在 <c>WorldHistorySeedPostgresTests</c>。
/// </summary>
public sealed class WorldHistorySeedServiceTests
{
    private static readonly DateOnly AsOfDate = new(2026, 7, 26);

    /// <summary>0.02 缩放约 60 单，够覆盖全部五种阶段又能在内存库里秒级跑完。</summary>
    private const double TestScale = 0.02d;

    [Fact]
    public async Task History_seed_writes_the_full_order_to_cash_chain_and_passes_its_own_validator()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var report = await new WorldHistorySeedService(dbContext).SeedAsync("org-001", "env-dev", AsOfDate, TestScale);

        var plans = WorldHistorySpec.BuildOrderPlans(AsOfDate, TestScale);
        Assert.Equal(plans.Count, report.SalesOrdersWritten);
        Assert.Equal(plans.Count, report.Validation.OrdersChecked);
        Assert.NotEmpty(report.Validation.Sample);

        // 已发货的单必有发货单 + 应收 + 收入凭证；已收款的单再加收款单 + 收款凭证。
        var delivered = plans.Count(plan => plan.HasDelivery);
        var collected = plans.Count(plan => plan.IsCollected);
        Assert.Equal(delivered, await dbContext.DeliveryOrders.CountAsync());
        Assert.Equal(delivered, await dbContext.AccountReceivables.CountAsync());
        Assert.Equal(collected, await dbContext.CashReceipts.CountAsync());
        Assert.Equal(delivered + collected, await dbContext.JournalVouchers.CountAsync());

        // 采购侧节奏跟着生产量走。
        Assert.Equal(
            WorldHistoryErpSpec.TotalPurchaseOrders(AsOfDate, TestScale),
            report.PurchaseOrdersWritten);
    }

    [Fact]
    public async Task History_seed_backdates_every_timestamp_into_the_go_live_window()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await new WorldHistorySeedService(dbContext).SeedAsync("org-001", "env-dev", AsOfDate, TestScale);

        var lowerBound = WorldHistoryCalendar.GoLiveDate.ToDateTime(TimeOnly.MinValue).AddDays(-1);
        var upperBound = AsOfDate.ToDateTime(TimeOnly.MaxValue);

        // 若聚合构造函数里的 DateTime.UtcNow 没被改写，这些断言会立刻抓住「今天生成的历史」。
        Assert.All(
            await dbContext.SalesOrders.ToArrayAsync(),
            order => Assert.InRange(order.CreatedAtUtc, lowerBound, upperBound));
        Assert.All(
            await dbContext.DeliveryOrders.ToArrayAsync(),
            delivery => Assert.InRange(delivery.ReleasedAtUtc, lowerBound, upperBound));
        Assert.All(
            await dbContext.AccountReceivables.ToArrayAsync(),
            receivable => Assert.InRange(receivable.CreatedAtUtc, lowerBound, upperBound));
        Assert.All(
            await dbContext.JournalVouchers.ToArrayAsync(),
            voucher => Assert.InRange(voucher.PostedAtUtc, lowerBound, upperBound));
        Assert.All(
            await dbContext.CashReceipts.ToArrayAsync(),
            receipt => Assert.InRange(receipt.RegisteredAtUtc, lowerBound, upperBound));

        // 报价单的有效期在开单后被改回历史值，不能残留「2027 年才过期」的穿帮值。
        Assert.All(
            await dbContext.Quotations.ToArrayAsync(),
            quotation => Assert.True(quotation.ExpiresOn <= AsOfDate.AddDays(30)));
    }

    [Fact]
    public async Task History_seed_is_idempotent_on_rerun()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var seed = new WorldHistorySeedService(dbContext);

        var first = await seed.SeedAsync("org-001", "env-dev", AsOfDate, TestScale);
        var second = await seed.SeedAsync("org-001", "env-dev", AsOfDate, TestScale);

        Assert.True(first.SalesOrdersWritten > 0);
        Assert.Equal(0, second.SalesOrdersWritten);
        Assert.Equal(0, second.PurchaseOrdersWritten);
        Assert.Equal(first.SalesOrdersWritten, await dbContext.SalesOrders.CountAsync());
    }

    [Fact]
    public async Task History_seed_never_touches_the_reserved_demo_or_scale_number_segments()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await new WorldHistorySeedService(dbContext).SeedAsync("org-001", "env-dev", AsOfDate, TestScale);

        // MAN-519 的 demo health-check 按 salesOrderNo 精确匹配唯一性；本引擎不得进入那两个号段。
        var documentNumbers = (await dbContext.SalesOrders.Select(x => x.SalesOrderNo).ToArrayAsync())
            .Concat(await dbContext.Quotations.Select(x => x.QuotationNo).ToArrayAsync())
            .Concat(await dbContext.DeliveryOrders.Select(x => x.DeliveryOrderNo).ToArrayAsync())
            .Concat(await dbContext.AccountReceivables.Select(x => x.ReceivableNo).ToArrayAsync())
            .Concat(await dbContext.CashReceipts.Select(x => x.CashReceiptNo).ToArrayAsync())
            .Concat(await dbContext.JournalVouchers.Select(x => x.VoucherNo).ToArrayAsync())
            .Concat(await dbContext.PurchaseOrders.Select(x => x.PurchaseOrderNo).ToArrayAsync())
            .Concat(await dbContext.PurchaseReceipts.Select(x => x.PurchaseReceiptNo).ToArrayAsync())
            .ToArray();

        Assert.NotEmpty(documentNumbers);
        Assert.All(documentNumbers, number =>
        {
            Assert.DoesNotContain("-DEMO-", number, StringComparison.Ordinal);
            Assert.DoesNotContain("-SCALE-", number, StringComparison.Ordinal);
            Assert.Contains(
                WorldHistorySpec.NumberSegmentPrefixes,
                prefix => number.StartsWith(prefix, StringComparison.Ordinal));
        });
    }

    [Fact]
    public async Task Bulk_history_writes_do_not_dispatch_domain_events()
    {
        // 整个引擎的性能与副作用前提：SaveChangesAsync 是静默路径，SaveEntitiesAsync 才派发事件。
        // 若 netcorepal 某天改变这一点，三千单 seed 会引爆下游事件风暴——这条测试是那个前提的锁。
        var mediator = new CountingMediator();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"erp-world-history-events-{Guid.CreateVersion7():N}")
            .ConfigureWarnings(warnings => warnings.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var dbContext = new ApplicationDbContext(options, mediator);

        var quotation = Quotation.Create(
            "org-001",
            "env-dev",
            "QUO-2026-90001",
            "CUST-WB-001",
            DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30),
            [new QuotationLineDraft("10", "FG-QJ-P1-L", "pcs", 10m, 320m, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(20))]);
        quotation.Approve();
        dbContext.Quotations.Add(quotation);
        dbContext.SalesOrders.Add(SalesOrder.CreateFromQuotation("SO-2026-90001", "SITE-001", quotation));

        await dbContext.SaveChangesAsync();

        Assert.Equal(0, mediator.PublishCount);
    }

    [Theory]
    // 黄金向量：ERP 与 MES 两侧必须对同一序号派生完全相同的订单计划。
    // MES 侧 WorldHistorySpecGoldenVectorTests 有一份逐字段相同的副本。
    [InlineData(1, "FG-HJ-E1-R", "CUST-WB-001", 80, 300)]
    [InlineData(42, "FG-HJ-S1-L", "CUST-WB-003", 100, 264)]
    [InlineData(500, "FG-QJ-S1-R", "CUST-WB-002", 100, 350)]
    public void Order_plan_stays_on_the_shared_golden_vector(
        int index,
        string skuCode,
        string customerCode,
        int quantity,
        int unitPrice)
    {
        var plans = WorldHistorySpec.BuildOrderPlans(AsOfDate, 1.0d);
        var plan = plans.Single(x => x.Index == index);

        Assert.Equal(WorldHistorySpec.SalesOrderNo(index), plan.SalesOrderNo);
        Assert.Equal(WorldHistorySpec.WorkOrderNo(index), plan.WorkOrderNo);
        Assert.Equal(skuCode, plan.SkuCode);
        Assert.Equal(customerCode, plan.CustomerCode);
        Assert.Equal(quantity, plan.Quantity);
        Assert.Equal(unitPrice, plan.UnitPrice);
    }

    [Fact]
    public void Order_plans_follow_the_world_bible_volume_and_status_shape()
    {
        var plans = WorldHistorySpec.BuildOrderPlans(AsOfDate, 1.0d);

        // 设定集 §7：约 3200 单，29 周。
        Assert.InRange(plans.Count, 2900, 3500);
        Assert.Equal(29, WorldHistoryCalendar.WeekCount(AsOfDate));

        // 状态分布落在设定集比例 ±3 个百分点内。
        AssertShare(plans, WorldHistoryOrderStage.Settled, 0.78);
        AssertShare(plans, WorldHistoryOrderStage.Shipped, 0.08);
        AssertShare(plans, WorldHistoryOrderStage.InProgress, 0.09);
        AssertShare(plans, WorldHistoryOrderStage.Released, 0.03);
        AssertShare(plans, WorldHistoryOrderStage.Cancelled, 0.02);

        // 全部落在上线日与今天之间，且没有一张单开在周日。
        Assert.All(plans, plan =>
        {
            Assert.InRange(plan.OrderDate, WorldHistoryCalendar.GoLiveDate, AsOfDate);
            Assert.NotEqual(DayOfWeek.Sunday, plan.OrderDate.DayOfWeek);
        });

        // 时间轴单调：下单 ≤ 下达 ≤ 开工 ≤ 完工 ≤ 发货 ≤ 收款。
        Assert.All(plans, plan =>
        {
            var timeline = WorldHistoryTimeline.For(plan, AsOfDate);
            Assert.True(timeline.OrderDate <= timeline.WorkOrderReleaseDate);
            Assert.True(timeline.WorkOrderReleaseDate <= timeline.ProductionStartDate);
            Assert.True(timeline.ProductionStartDate <= timeline.ProductionCompletionDate);
            Assert.True(timeline.ProductionCompletionDate <= timeline.ShipDate);
            Assert.True(timeline.ShipDate <= timeline.CollectionDate);
            Assert.True(timeline.CollectionDate <= AsOfDate);
        });
    }

    [Fact]
    public void Weekly_volume_reflects_the_spring_festival_trough_and_month_end_surge()
    {
        // 春节 2026-02-09–02-22 的两周明显低于基准，月末冲量周明显高于基准。
        var springFestivalWeek = Enumerable.Range(0, 29)
            .First(week => WorldHistoryCalendar.WeekOverlapsSpringFestival(WorldHistoryCalendar.WeekStart(week)));
        Assert.True(WorldHistoryCalendar.WeeklyOrderVolume(springFestivalWeek, 1.0d) < WorldHistoryCalendar.BaseWeeklyOrders - WorldHistoryCalendar.WeeklyJitter);

        var monthEndWeeks = Enumerable.Range(0, 29)
            .Where(week => WorldHistoryCalendar.WeekContainsMonthEnd(WorldHistoryCalendar.WeekStart(week)) &&
                !WorldHistoryCalendar.WeekOverlapsSpringFestival(WorldHistoryCalendar.WeekStart(week)))
            .ToArray();
        Assert.NotEmpty(monthEndWeeks);
        Assert.All(monthEndWeeks, week =>
            Assert.True(WorldHistoryCalendar.WeeklyOrderVolume(week, 1.0d) > WorldHistoryCalendar.BaseWeeklyOrders - WorldHistoryCalendar.WeeklyJitter));
    }

    [Fact]
    public void Scaling_down_keeps_each_document_identical_to_its_full_run_counterpart()
    {
        // 按流键取随机数的直接好处：0.1 缩放跑出来的单据内容与全量跑一致，快速验证才有意义。
        var full = WorldHistorySpec.BuildOrderPlans(AsOfDate, 1.0d).ToDictionary(x => x.SalesOrderNo, StringComparer.Ordinal);
        var scaled = WorldHistorySpec.BuildOrderPlans(AsOfDate, 0.1d);

        Assert.NotEmpty(scaled);
        Assert.All(scaled, plan =>
        {
            var counterpart = full[plan.SalesOrderNo];
            Assert.Equal(counterpart.SkuCode, plan.SkuCode);
            Assert.Equal(counterpart.CustomerCode, plan.CustomerCode);
            Assert.Equal(counterpart.Quantity, plan.Quantity);
            Assert.Equal(counterpart.UnitPrice, plan.UnitPrice);
        });
    }

    [Fact]
    public void Deterministic_random_is_stable_across_runs_and_independent_per_stream()
    {
        Assert.Equal(
            new WorldHistoryRandom("SO-2026-00042").NextUInt64(),
            new WorldHistoryRandom("SO-2026-00042").NextUInt64());
        Assert.NotEqual(
            new WorldHistoryRandom("SO-2026-00042").NextUInt64(),
            new WorldHistoryRandom("SO-2026-00043").NextUInt64());

        // 黄金向量：根种子或算法一旦改动，整段历史都会改写，这里必须先失败。
        Assert.Equal(0xCFC630FB3054AF1EUL, WorldHistoryRandom.Fnv1a64("SO-2026-00001"));
    }

    private static void AssertShare(IReadOnlyList<WorldHistoryOrderPlan> plans, WorldHistoryOrderStage stage, double expected)
    {
        var actual = (double)plans.Count(plan => plan.Stage == stage) / plans.Count;
        Assert.InRange(actual, expected - 0.03, expected + 0.03);
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options =>
            options
                .UseInMemoryDatabase($"erp-world-history-seed-{Guid.CreateVersion7():N}")
                .ConfigureWarnings(warnings => warnings.Ignore(
                    Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)));
        services.AddUnitOfWork<ApplicationDbContext>();
        return services.BuildServiceProvider();
    }

    private sealed class CountingMediator : IMediator
    {
        public int PublishCount { get; private set; }

        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            PublishCount++;
            return Task.CompletedTask;
        }

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
        {
            PublishCount++;
            return Task.CompletedTask;
        }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
