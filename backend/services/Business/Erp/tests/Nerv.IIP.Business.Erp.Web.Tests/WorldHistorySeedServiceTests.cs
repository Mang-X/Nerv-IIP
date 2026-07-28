using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseRequisitionAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.QuotationAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SalesOrderAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Seed;
using NetCorePal.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Nerv.IIP.Business.Erp.Web.Tests;

/// <summary>
/// 《工厂世界观设定集》L1 背景历史引擎（ERP 侧）的形状、确定性、隔离性与幂等性证据。
/// 真实 PostgreSQL 的全量耗时实测在 <c>WorldHistorySeedPostgresTests</c>。
/// </summary>
public sealed class WorldHistorySeedServiceTests(ITestOutputHelper output)
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

    /// <summary>
    /// 经营五页（采购申请/询价/供应商报价/销售机会/成本候选，演示走查缺口）：
    /// 历史链路必须为五个聚合落数且与采购/销售计划配对，对任意 asOfDate 成立
    /// （含周日后首日与春节段，防 #1151 单日期盲区）。校验器 fail-closed 已在 SeedAsync 内跑过，
    /// 这里再抽形状断言防校验器与引擎同错。
    /// </summary>
    [Theory]
    [InlineData(2026, 7, 27)]
    [InlineData(2026, 7, 26)]
    [InlineData(2026, 8, 2)]
    [InlineData(2026, 2, 16)]
    [InlineData(2026, 7, 31)]
    public async Task History_seed_writes_the_business_objects_for_any_as_of_date(int year, int month, int day)
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var asOfDate = new DateOnly(year, month, day);

        await new WorldHistorySeedService(dbContext).SeedAsync("org-001", "env-dev", asOfDate, TestScale);

        var purchasePlans = WorldHistorySeedService.BuildPurchasePlans(asOfDate, TestScale);
        var salesPlans = WorldHistorySpec.BuildOrderPlans(asOfDate, TestScale);

        // 采购申请：每张采购单一条已转化 + 公式化的在途条数。
        Assert.Equal(
            purchasePlans.Count + WorldHistoryErpSpec.OpenRequisitionCount(purchasePlans.Count),
            await dbContext.PurchaseRequisitions.CountAsync());
        var openRequisitions = await dbContext.PurchaseRequisitions
            .Where(x => x.Status == PurchaseRequisitionStatus.Open)
            .ToArrayAsync();
        Assert.Equal(WorldHistoryErpSpec.OpenRequisitionCount(purchasePlans.Count), openRequisitions.Length);
        Assert.All(openRequisitions, requisition => Assert.True(requisition.RequiredDate > asOfDate));

        // 询比价：每 6 张采购单一单 RFQ，品类内每家供应商一份报价，中标价 == 采购单价。
        var expectedRfqs = purchasePlans.Count(x => x.Index % WorldHistoryErpSpec.RfqEveryNthPurchase == 1);
        Assert.Equal(expectedRfqs, await dbContext.RequestForQuotations.CountAsync());
        var sampledPlan = purchasePlans.First(x => x.Index % WorldHistoryErpSpec.RfqEveryNthPurchase == 1);
        var sampledQuotes = await dbContext.SupplierQuotations
            .Include(x => x.Lines)
            .Where(x => x.RfqNo == WorldHistoryErpSpec.RfqNo(sampledPlan.Index))
            .ToArrayAsync();
        Assert.Equal(WorldHistoryErpSpec.CategoryOf(sampledPlan.SkuCode).SupplierCodes.Count, sampledQuotes.Length);
        var winning = Assert.Single(sampledQuotes, x => x.SupplierCode == sampledPlan.SupplierCode);
        Assert.Equal(sampledPlan.UnitPrice, winning.Lines.Single().UnitPrice);

        // 成本候选：每 8 张已收货采购单一条，引用真实收货单号。
        Assert.Equal(
            purchasePlans.Count(x => x.IsReceived && x.Index % WorldHistoryErpSpec.CostCandidateEveryNthReceipt == 0),
            await dbContext.CostCandidates.CountAsync());

        // 销售机会：每 40 单一条，客户与订单计划一致。
        Assert.Equal(
            salesPlans.Count(x => x.Index % WorldHistoryErpSpec.OpportunityEveryNthSalesOrder == 1),
            await dbContext.Opportunities.CountAsync());
    }

    /// <summary>
    /// 应付账款（<c>erp.account_payables</c>）：应收应付页两栏必须对称——AR 走销售发货、AP 走采购收货。
    /// 5 个 asOfDate 覆盖周一 / 周日次日 / 未来周日 / 春节段 / 月末冲量段（防 #1151 单日期盲区）。
    /// </summary>
    [Theory]
    [InlineData(2026, 7, 27)]
    [InlineData(2026, 7, 26)]
    [InlineData(2026, 8, 2)]
    [InlineData(2026, 2, 16)]
    [InlineData(2026, 7, 31)]
    public async Task History_seed_writes_payables_for_every_received_purchase_order(int year, int month, int day)
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var asOfDate = new DateOnly(year, month, day);
        var seed = new WorldHistorySeedService(dbContext);

        var first = await seed.SeedAsync("org-001", "env-dev", asOfDate, TestScale);
        var second = await seed.SeedAsync("org-001", "env-dev", asOfDate, TestScale);

        var purchasePlans = WorldHistorySeedService.BuildPurchasePlans(asOfDate, TestScale);
        var payablePlans = WorldHistorySeedService.BuildPayablePlans(asOfDate, TestScale);
        var received = purchasePlans.Count(x => x.IsReceived);

        // 一张已收货采购单一条应付；未收货的一条都不能有。
        Assert.Equal(received, payablePlans.Count);
        Assert.True(received > 0, "所选 asOfDate 下应至少有一张已收货采购单。");
        Assert.Equal(received, first.PayablesWritten);
        Assert.Equal(0, second.PayablesWritten);
        Assert.Equal(received, await dbContext.AccountPayables.CountAsync());

        var payables = await dbContext.AccountPayables.AsNoTracking().ToArrayAsync();
        var receiptNos = purchasePlans.Where(x => x.IsReceived)
            .ToDictionary(x => WorldHistoryErpSpec.PayableNo(x.Index), StringComparer.Ordinal);
        var lowerBound = WorldHistoryCalendar.GoLiveDate.ToDateTime(TimeOnly.MinValue).AddDays(-1);
        var upperBound = asOfDate.ToDateTime(TimeOnly.MaxValue);

        Assert.All(payables, payable =>
        {
            // 号段隔离：AP-2026-* 不与固定演示 / 规模块相交。
            Assert.StartsWith(WorldHistoryErpSpec.PayableNumberPrefix, payable.PayableNo, StringComparison.Ordinal);
            Assert.DoesNotContain("-DEMO-", payable.PayableNo, StringComparison.Ordinal);
            Assert.DoesNotContain("-SCALE-", payable.PayableNo, StringComparison.Ordinal);

            // 跨单据对账：来源单据号 = 真实收货单号，金额 / 供应商与采购单逐字对上。
            var purchase = receiptNos[payable.PayableNo];
            Assert.Equal(purchase.PurchaseReceiptNo, payable.SourceDocumentNo);
            Assert.Equal(purchase.SupplierCode, payable.SupplierCode);
            Assert.Equal(decimal.Round(purchase.TotalAmount, 2), payable.Amount);
            Assert.Equal("CNY", payable.CurrencyCode);

            // 账期与已付：付款条件与到期日一致，已付不越界，时间戳回填到历史窗内。
            Assert.Contains(payable.PaymentTermCode, new[] { "NET30", "NET45", "NET60" });
            Assert.InRange(payable.PaidAmount, 0m, payable.Amount);
            Assert.True(payable.DueDate > payable.InvoiceDate);
            Assert.InRange(payable.InvoiceDate, WorldHistoryCalendar.GoLiveDate, asOfDate);
            Assert.InRange(payable.CreatedAtUtc, lowerBound, upperBound);
        });

        // 未收货的采购单没有应付。
        var unreceived = purchasePlans.Where(x => !x.IsReceived).Select(x => WorldHistoryErpSpec.PayableNo(x.Index));
        var payableNos = payables.Select(x => x.PayableNo).ToHashSet(StringComparer.Ordinal);
        Assert.All(unreceived, no => Assert.DoesNotContain(no, payableNos));
    }

    [Fact]
    public void Payable_payment_progress_spreads_across_settled_open_and_overdue()
    {
        // 全量规模下应付账龄必须有层次：已付清为主、未付一批、还有少量提前预付——
        // 否则应付页的账龄卡会是一根光秃秃的柱子。
        var plans = WorldHistorySeedService.BuildPayablePlans(AsOfDate, 1.0d);

        Assert.InRange(plans.Count, 350, 550);
        output.WriteLine($"erp-world-history-payables={plans.Count}");
        output.WriteLine($"erp-world-history-payables-settled={plans.Count(x => x.IsSettled)}");
        output.WriteLine($"erp-world-history-payables-open={plans.Count(x => x.PaidAmount == 0m)}");
        output.WriteLine($"erp-world-history-payables-partial={plans.Count(x => x.IsPartiallyPaid)}");
        output.WriteLine(FormattableString.Invariant($"erp-world-history-payables-amount={plans.Sum(x => x.Amount):0.00}"));
        var settled = plans.Count(x => x.IsSettled);
        var open = plans.Count(x => x.PaidAmount == 0m);
        var partial = plans.Count(x => x.IsPartiallyPaid);
        var overdueUnpaid = plans.Count(x => x.PaidAmount == 0m && x.DueDate < AsOfDate);

        Assert.True(settled > plans.Count / 2, $"已付清应占多数，实际 {settled}/{plans.Count}。");
        Assert.True(open > 0, "应付里必须留有未付的账。");
        Assert.True(partial > 0, "应付里必须留有部分预付的账。");
        Assert.True(overdueUnpaid > 0, "应付账龄表需要逾期未付的样本。");

        // 应付合计 = 已收货采购单金额合计（应付页金额卡的对账口径）。
        var purchaseTotal = WorldHistorySeedService.BuildPurchasePlans(AsOfDate, 1.0d)
            .Where(x => x.IsReceived)
            .Sum(x => decimal.Round(x.TotalAmount, 2));
        Assert.Equal(purchaseTotal, plans.Sum(x => x.Amount));
        Assert.All(plans, plan => Assert.Equal(plan.Amount - plan.PaidAmount, plan.OpenAmount));
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
    public async Task Validator_still_passes_when_the_same_database_is_reseeded_on_a_later_date()
    {
        // 同一个库在更晚的日期重跑：订单总数变大会把老单的**计划**阶段往前推（在制 → 已结案），
        // 但库里已写定的行不会重写。校验器必须按库内既有事实分流，否则这里会误报「该发货却没发货单」。
        var laterDate = AsOfDate.AddDays(21);

        // 先证明这个测试确实在考验那件事：同一张订单的计划阶段在两个日期下不同。
        // 否则本测试可能在某次调参后悄悄退化成一次普通的幂等重跑。
        var before = WorldHistorySpec.BuildOrderPlans(AsOfDate, TestScale).ToDictionary(x => x.SalesOrderNo, StringComparer.Ordinal);
        var after = WorldHistorySpec.BuildOrderPlans(laterDate, TestScale);
        Assert.Contains(after, plan => before.TryGetValue(plan.SalesOrderNo, out var original) && original.Stage != plan.Stage);

        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var seed = new WorldHistorySeedService(dbContext);

        await seed.SeedAsync("org-001", "env-dev", AsOfDate, TestScale);
        var later = await seed.SeedAsync("org-001", "env-dev", laterDate, TestScale);

        // 晚三周重跑会补出新一批订单，并且新旧两批一起通过校验。
        Assert.True(later.SalesOrdersWritten > 0);
        Assert.Equal(WorldHistorySpec.TotalOrders(laterDate, TestScale), later.Validation.OrdersChecked);
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
