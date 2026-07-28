using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.CountExecutionAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WcsTaskAggregate;
using Nerv.IIP.Business.Wms.Infrastructure;
using Nerv.IIP.Business.Wms.Web.Application.Seed;

namespace Nerv.IIP.Business.Wms.Web.Tests;

/// <summary>
/// 《工厂世界观设定集》L1「仓储自动化 / 盘点执行 / 来料退货」块的形状与幂等性证据。
///
/// 四张表此前恒为 0 行，业务前端「WCS 任务」「盘点执行」「入库 · 退货」三页全空。
/// 断言覆盖：条数区间、号段格式、状态分布、引用完整性（WCS 绑真实仓储作业任务、
/// 退货挂真实入库单）、熔断链路收敛闭合、幂等，以及 **5 个 asOfDate 边界**
/// （上线日 / 上线日+1 / 年中 / 演示当天 / 未来日）——单日期测试假绿的教训见 #1151。
/// </summary>
public sealed class WorldHistoryWarehouseOpsSeedServiceTests
{
    /// <summary>库写入类用例的规模：足够跑出四档盘点结局与四档 WCS 结局，又不让 InMemory provider 变慢。</summary>
    private const double TestScale = 0.3d;

    /// <summary>低于该条数不做分布类断言（上线日附近的缩放样本只有个位数事实）。</summary>
    private const int MinimumDistributionSample = 12;

    /// <summary>5 个 asOfDate 边界：上线日、上线日+1、年中、演示当天、未来日。</summary>
    public static TheoryData<int, int, int> AsOfDates =>
        new()
        {
            { 2026, 1, 5 },
            { 2026, 1, 6 },
            { 2026, 4, 15 },
            { 2026, 7, 27 },
            { 2026, 12, 31 },
        };

    [Theory]
    [MemberData(nameof(AsOfDates))]
    public async Task Warehouse_ops_seed_fills_all_four_tables_for_any_as_of_date(int year, int month, int day)
    {
        var asOfDate = new DateOnly(year, month, day);
        await using var db = CreateDbContext();
        await SeedDocumentChainAsync(db, asOfDate);

        var report = await new WorldHistoryWarehouseOpsSeedService(db)
            .SeedAsync("org-001", "env-dev", asOfDate, TestScale);

        // 四张表都不再是 0 行——这正是三页空白的直接原因。
        // WCS 与退货是**派生**事实：上线日附近上游单据链本来就只有个位数，
        // 因此断言的是「与上游派生出的条数逐条相等」而不是「一定大于 0」。
        Assert.True(report.CountExecutionsWritten > 0);
        Assert.Equal(WorldHistoryWarehouseOpsSpec.Devices.Count, report.WcsDispatchCircuitsWritten);

        var expectedWcsTasks = (await db.WarehouseTasks.Select(x => x.TaskNo).ToArrayAsync())
            .Count(WorldHistoryWarehouseOpsSpec.IsDispatched);
        var realInboundOrders = (await db.InboundOrders.Select(x => x.InboundOrderNo).ToArrayAsync())
            .ToHashSet(StringComparer.Ordinal);
        var expectedReturns = WorldHistoryWarehouseOpsSeedService
            .BuildSupplierReturnDrafts(asOfDate, TestScale)
            .Count(draft => realInboundOrders.Contains(draft.InboundOrderNo));

        Assert.Equal(expectedWcsTasks, report.WcsTasksWritten);
        Assert.Equal(expectedReturns, report.SupplierReturnRequestsWritten);
        Assert.Equal(
            WorldHistoryCountSpec.CountPlanCount(asOfDate, TestScale),
            await db.CountExecutions.CountAsync());
        Assert.Equal(report.WcsTasksWritten, await db.WcsTasks.CountAsync());
        Assert.Equal(report.SupplierReturnRequestsWritten, await db.SupplierReturnRequests.CountAsync());

        // 校验器（fail-closed）跑过并如实回报条数。
        Assert.Equal(report.CountExecutionsWritten, report.Validation.CountExecutionsChecked);
        Assert.Equal(report.WcsTasksWritten, report.Validation.WcsTasksChecked);
        Assert.Equal(report.SupplierReturnRequestsWritten, report.Validation.SupplierReturnRequestsChecked);
    }

    [Theory]
    [MemberData(nameof(AsOfDates))]
    public async Task Warehouse_ops_seed_keeps_number_segments_and_status_distribution(int year, int month, int day)
    {
        var asOfDate = new DateOnly(year, month, day);
        await using var db = CreateDbContext();
        await SeedDocumentChainAsync(db, asOfDate);
        await new WorldHistoryWarehouseOpsSeedService(db).SeedAsync("org-001", "env-dev", asOfDate, TestScale);

        // 号段格式（设定集 §9 的仓储作业补充段），且不侵占 L2/规模块。
        var countNumbers = await db.CountExecutions.Select(x => x.CountNo).ToArrayAsync();
        Assert.All(countNumbers, no => Assert.Matches(@"^CNT-2026-\d{4}$", no));
        var externalTaskIds = await db.WcsTasks.Select(x => x.ExternalTaskId).ToArrayAsync();
        Assert.All(externalTaskIds, id => Assert.StartsWith("WCS-WT-", id, StringComparison.Ordinal));
        var returnNumbers = await db.SupplierReturnRequests.Select(x => x.SupplierReturnNo).ToArrayAsync();
        Assert.All(returnNumbers, no => Assert.StartsWith("RTS-IB-", no, StringComparison.Ordinal));
        Assert.All(
            countNumbers.Concat(externalTaskIds).Concat(returnNumbers),
            no =>
            {
                Assert.DoesNotContain("-DEMO-", no, StringComparison.Ordinal);
                Assert.DoesNotContain("-SCALE-", no, StringComparison.Ordinal);
            });

        // 盘点：绝大多数已回单，最近一批仍在盘 + 作废的从未实盘（页面的「进行中盘点」）。
        var plans = WorldHistoryCountSpec.BuildCountPlans(asOfDate, TestScale);
        var executions = await db.CountExecutions.ToArrayAsync();
        var open = executions.Count(x => x.Status == CountExecutionStatus.Open);
        Assert.Equal(plans.Count(x => !x.IsCompleted), open);
        Assert.True(open > 0);
        Assert.True(open < executions.Length || executions.Length == 1);
        Assert.All(executions, execution =>
        {
            Assert.True(execution.ExpectedQuantity > 0m);
            Assert.Contains(execution.LocationCode, WorldHistoryCountSpec.Dimensions.Select(x => x.LocationCode));
            if (execution.Status == CountExecutionStatus.Completed)
            {
                Assert.NotNull(execution.CountedQuantity);
                Assert.Equal(execution.CountedQuantity!.Value - execution.ExpectedQuantity, execution.VarianceQuantity);
            }
        });

        // WCS：已完成为主，另有执行中与异常。
        var wcsTasks = await db.WcsTasks.ToArrayAsync();
        Assert.All(wcsTasks, task =>
        {
            Assert.Contains(task.DeviceId, WorldHistoryWarehouseOpsSpec.Devices.Select(x => x.DeviceId));
            Assert.Contains(task.AdapterType, WorldHistoryWarehouseOpsSpec.Devices.Select(x => x.AdapterType));
            Assert.True(task.CompletedAtUtc is null || task.CompletedAtUtc >= task.DispatchedAtUtc);
        });
        if (wcsTasks.Length >= MinimumDistributionSample)
        {
            var completed = wcsTasks.Count(x => x.Status == WcsTaskStatus.Completed);
            Assert.True(completed * 2 > wcsTasks.Length, "已完成的 WCS 任务应当占多数。");
            Assert.Contains(wcsTasks, x => x.Status != WcsTaskStatus.Completed);
            Assert.All(
                wcsTasks.Where(x => x.Status == WcsTaskStatus.Failed),
                x => Assert.Contains(x.FailureCode, WorldHistoryWarehouseOpsSpec.Failures.Select(f => f.Code)));
        }

        // 熔断链路必须收敛在闭合态，否则会挡住演示当场的真实下发。
        var circuits = await db.WcsDispatchCircuits.ToArrayAsync();
        Assert.Equal(WorldHistoryWarehouseOpsSpec.Devices.Count, circuits.Length);
        Assert.All(circuits, circuit => Assert.False(circuit.IsOpen));

        // 退货：数量为正、原因是中文文案。
        var returns = await db.SupplierReturnRequests.ToArrayAsync();
        Assert.All(returns, request =>
        {
            Assert.True(request.Quantity > 0m);
            Assert.Contains(request.DispositionReason!, WorldHistoryWarehouseOpsSpec.ReturnReasons);
            Assert.Equal(WorldHistoryPhase2Spec.QualityHoldLocationCode, request.LocationCode);
        });
    }

    /// <summary>WCS 任务必须绑**库里真实存在**的仓储作业任务——这是「WCS 任务」页可下钻的前提。</summary>
    [Theory]
    [MemberData(nameof(AsOfDates))]
    public async Task Wcs_tasks_anchor_on_real_warehouse_tasks_and_returns_on_real_inbound_orders(
        int year, int month, int day)
    {
        var asOfDate = new DateOnly(year, month, day);
        await using var db = CreateDbContext();
        await SeedDocumentChainAsync(db, asOfDate);
        await new WorldHistoryWarehouseOpsSeedService(db).SeedAsync("org-001", "env-dev", asOfDate, TestScale);

        var warehouseTaskIds = (await db.WarehouseTasks.Select(x => x.Id).ToArrayAsync()).ToHashSet();
        var wcsTasks = await db.WcsTasks.ToArrayAsync();
        Assert.All(wcsTasks, task => Assert.Contains(task.WarehouseTaskId, warehouseTaskIds));

        var inboundOrderNumbers = (await db.InboundOrders.Select(x => x.InboundOrderNo).ToArrayAsync())
            .ToHashSet(StringComparer.Ordinal);
        var returns = await db.SupplierReturnRequests.ToArrayAsync();
        Assert.All(returns, request => Assert.Contains(request.InboundOrderNo, inboundOrderNumbers));
    }

    /// <summary>历史铺开之后（非上线日边界），WCS 任务与退货申请必须实际存在，否则两页仍然是空的。</summary>
    [Fact]
    public async Task Wcs_tasks_and_supplier_returns_are_populated_once_history_has_unrolled()
    {
        var asOfDate = new DateOnly(2026, 7, 27);
        await using var db = CreateDbContext();
        await SeedDocumentChainAsync(db, asOfDate);

        var report = await new WorldHistoryWarehouseOpsSeedService(db)
            .SeedAsync("org-001", "env-dev", asOfDate, TestScale);

        Assert.True(report.WcsTasksWritten > 0, "WCS 任务页不能再是空的。");
        Assert.True(report.SupplierReturnRequestsWritten > 0, "入库 · 退货页不能再是空的。");
        Assert.True(report.Validation.FailedWcsTasksChecked > 0, "WCS 异常态必须在场，否则演示讲不出异常处理。");
        Assert.True(report.Validation.VarianceCountExecutionsChecked > 0, "盘点差异必须在场。");
    }

    [Theory]
    [MemberData(nameof(AsOfDates))]
    public async Task Warehouse_ops_seed_is_idempotent_for_any_as_of_date(int year, int month, int day)
    {
        var asOfDate = new DateOnly(year, month, day);
        await using var db = CreateDbContext();
        await SeedDocumentChainAsync(db, asOfDate);
        var seed = new WorldHistoryWarehouseOpsSeedService(db);

        var first = await seed.SeedAsync("org-001", "env-dev", asOfDate, TestScale);
        var countExecutions = await db.CountExecutions.CountAsync();
        var wcsTasks = await db.WcsTasks.CountAsync();
        var circuits = await db.WcsDispatchCircuits.CountAsync();
        var returns = await db.SupplierReturnRequests.CountAsync();

        var second = await seed.SeedAsync("org-001", "env-dev", asOfDate, TestScale);

        Assert.Equal(0, second.CountExecutionsWritten);
        Assert.Equal(0, second.WcsTasksWritten);
        Assert.Equal(0, second.WcsDispatchCircuitsWritten);
        Assert.Equal(0, second.SupplierReturnRequestsWritten);
        Assert.Equal(countExecutions, await db.CountExecutions.CountAsync());
        Assert.Equal(wcsTasks, await db.WcsTasks.CountAsync());
        Assert.Equal(circuits, await db.WcsDispatchCircuits.CountAsync());
        Assert.Equal(returns, await db.SupplierReturnRequests.CountAsync());
        Assert.True(first.CountExecutionsWritten > 0);
    }

    /// <summary>设定集 §7 要求的全量规模：循环盘点每周一次 × 6 个组合，约 150–220 条。</summary>
    [Fact]
    public void Full_scale_volumes_match_the_world_bible_shape()
    {
        var asOfDate = new DateOnly(2026, 7, 27);
        var plans = WorldHistoryCountSpec.BuildCountPlans(asOfDate, 1.0d);

        Assert.InRange(plans.Count, 150, 220);
        Assert.Equal(
            WorldHistoryCountSpec.CountDays(asOfDate).Count * WorldHistoryCountSpec.CountsPerCountDay,
            plans.Count);
        Assert.DoesNotContain(WorldHistoryCountSpec.CountDays(asOfDate), date => date.DayOfWeek == DayOfWeek.Sunday);

        // 四档结局必然在场（配额分层，不是概率抽样）。
        foreach (var outcome in Enum.GetValues<WorldHistoryCountOutcome>())
        {
            Assert.Contains(plans, plan => plan.Outcome == outcome);
        }

        // 差异 ⇔ 走审批：账实相符 / 作废 / 在盘一律零差异，走审批的一律非零差异。
        Assert.All(plans, plan => Assert.Equal(plan.VarianceQuantity != 0m, plan.HasInventoryAdjustment));
        Assert.All(plans, plan => Assert.True(plan.CountedQuantity > 0m));
        // 作废与在盘的从未实盘，因此没有回单时刻。
        Assert.All(plans.Where(x => !x.IsCompleted), plan => Assert.Null(plan.CompletedAtUtc));
        Assert.All(plans.Where(x => x.IsCompleted), plan => Assert.NotNull(plan.CompletedAtUtc));
    }

    /// <summary>
    /// 跨服务黄金向量：<c>WorldHistoryCountSpec</c> 在仓储与库存两侧按同一字面量重复声明，
    /// 两侧各有一份**逐字相同**的本用例。任一侧改动而另一侧没跟上，两边的盘点单号 /
    /// 差异量就会漂移，跨域对账当场失效。
    /// </summary>
    [Fact]
    public void Count_plan_golden_vector_matches_the_mirrored_spec()
    {
        var plans = WorldHistoryCountSpec.BuildCountPlans(new DateOnly(2026, 7, 27), 1.0d);

        Assert.Equal(WorldHistoryCountGoldenVector.PlanCount, plans.Count);
        Assert.Equal(WorldHistoryCountGoldenVector.Digest, WorldHistoryCountGoldenVector.DigestOf(plans));
    }

    /// <summary>盘点维度必须全部落在有期初批的常驻库位上，否则库存侧找不到台账、整批盘点会被跳过。</summary>
    [Fact]
    public void Count_dimensions_resolve_to_opening_lots_on_storage_locations()
    {
        Assert.NotEmpty(WorldHistoryCountSpec.Dimensions);
        Assert.All(WorldHistoryCountSpec.Dimensions, dimension =>
        {
            Assert.Equal(WorldHistorySpec.SiteCode, dimension.SiteCode);
            Assert.Equal($"LOT-OPENING-{dimension.SkuCode}", dimension.LotNo);
            Assert.Equal(WorldHistoryPhase2Spec.StorageLocationFor(dimension.SkuCode), dimension.LocationCode);
            Assert.NotEqual(WorldHistoryPhase2Spec.FinishedGoodsLocationCode, dimension.LocationCode);
        });

        // 与库存域 WorldHistoryInventorySpec.OpeningLotNo 同字面量（跨域对账的字面量锚点）。
        Assert.Equal("LOT-OPENING-SF-ROD-01", WorldHistoryCountSpec.OpeningLotNo("SF-ROD-01"));
    }

    [Fact]
    public async Task Validator_fails_closed_when_a_count_execution_disappears()
    {
        var asOfDate = new DateOnly(2026, 7, 26);
        await using var db = CreateDbContext();
        await SeedDocumentChainAsync(db, asOfDate);
        await new WorldHistoryWarehouseOpsSeedService(db).SeedAsync("org-001", "env-dev", asOfDate, TestScale);

        var execution = await db.CountExecutions.FirstAsync();
        db.CountExecutions.Remove(execution);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var exception = await Assert.ThrowsAsync<WorldHistoryWarehouseOpsConsistencyException>(() =>
            new WorldHistoryWarehouseOpsValidator(db).ValidateAsync("org-001", "env-dev", asOfDate, TestScale));

        Assert.Contains(exception.Failures, failure => failure.Contains("未落库", StringComparison.Ordinal));
    }

    private static async Task SeedDocumentChainAsync(ApplicationDbContext dbContext, DateOnly asOfDate) =>
        await new WorldHistorySeedService(dbContext).SeedAsync("org-001", "env-dev", asOfDate, TestScale);

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"wms-world-history-ops-{Guid.CreateVersion7():N}")
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }
}
