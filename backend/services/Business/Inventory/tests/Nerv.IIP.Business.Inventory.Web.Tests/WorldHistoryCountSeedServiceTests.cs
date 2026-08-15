using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockCountAdjustmentAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockCountTaskAggregate;
using Nerv.IIP.Business.Inventory.Infrastructure;
using Nerv.IIP.Business.Inventory.Web.Application.Queries;
using Nerv.IIP.Business.Inventory.Web.Application.Seed;

namespace Nerv.IIP.Business.Inventory.Web.Tests;

/// <summary>
/// 《工厂世界观设定集》L1「库存盘点」块的形状与幂等性证据，外加盘点 / 流水读面的查询证据。
///
/// <c>stock_count_tasks</c> 与 <c>stock_count_adjustments</c> 此前恒为 0 行，而库存盘点页
/// 连**读端点都没有**，表格只能挂会话内本地队列、刷新即空。断言覆盖：条数、状态分布、
/// 号段格式、差异量与共享计划一致、历史盘点绝不落到已确认态、台账不残留盘点冻结、幂等，
/// 以及 **5 个 asOfDate 边界**——单日期测试假绿的教训见 #1151。
/// </summary>
public sealed class WorldHistoryCountSeedServiceTests
{
    /// <summary>库写入类用例的规模：足够跑出四档结局，又不让 InMemory provider 变慢。</summary>
    private const double TestScale = 0.3d;

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
    public async Task Count_seed_fills_count_tasks_and_adjustments_for_any_as_of_date(int year, int month, int day)
    {
        var asOfDate = new DateOnly(year, month, day);
        await using var db = CreateDbContext();
        await SeedMovementChainAsync(db, asOfDate);

        var report = await new WorldHistoryCountSeedService(db).SeedAsync("org-001", "env-dev", asOfDate, TestScale);

        var plans = WorldHistoryCountSpec.BuildCountPlans(asOfDate, TestScale)
            .Where(plan => plan.HasInventoryCountTask)
            .ToArray();

        // 盘点任务 = 计划里有差异或未闭环的那部分，减去台账维度确实不存在而被跳过的。
        Assert.Equal(plans.Length - report.PlansSkippedWithoutLedger, report.StockCountTasksWritten);
        Assert.Equal(report.StockCountTasksWritten, await db.StockCountTasks.CountAsync());
        Assert.Equal(report.StockCountAdjustmentsWritten, await db.StockCountAdjustments.CountAsync());
        Assert.Equal(report.StockCountTasksWritten, report.Validation.StockCountTasksChecked);
        Assert.Equal(report.StockCountAdjustmentsWritten, report.Validation.StockCountAdjustmentsChecked);
    }

    /// <summary>历史盘点绝不过账、绝不冻结台账——这两条都是会当场毁掉演示的红线。</summary>
    [Theory]
    [MemberData(nameof(AsOfDates))]
    public async Task Count_seed_never_confirms_and_never_leaves_a_frozen_ledger(int year, int month, int day)
    {
        var asOfDate = new DateOnly(year, month, day);
        await using var db = CreateDbContext();
        await SeedMovementChainAsync(db, asOfDate);
        await new WorldHistoryCountSeedService(db).SeedAsync("org-001", "env-dev", asOfDate, TestScale);

        var tasks = await db.StockCountTasks.ToArrayAsync();
        Assert.All(tasks, task =>
        {
            Assert.NotEqual(StockCountTaskStatuses.Confirmed, task.Status);
            Assert.Matches(@"^CNT-2026-\d{4}$", task.CountTaskCode);
            Assert.DoesNotContain("-DEMO-", task.CountTaskCode, StringComparison.Ordinal);
            Assert.DoesNotContain("-SCALE-", task.CountTaskCode, StringComparison.Ordinal);
        });

        Assert.Empty(await db.StockLedgers.Where(x => x.IsFrozenForCount).ToArrayAsync());

        // 盘点不写流水：库存域的现存量恒等式必须仍然成立（校验器 fail-closed）。
        await new WorldHistoryConsistencyValidator(db).ValidateAsync("org-001", "env-dev", asOfDate, TestScale);
    }

    /// <summary>库存侧的差异量必须与仓储侧共享计划逐笔一致——这是跨域对账的唯一锚点。</summary>
    [Theory]
    [MemberData(nameof(AsOfDates))]
    public async Task Count_variance_matches_the_shared_plan(int year, int month, int day)
    {
        var asOfDate = new DateOnly(year, month, day);
        await using var db = CreateDbContext();
        await SeedMovementChainAsync(db, asOfDate);
        await new WorldHistoryCountSeedService(db).SeedAsync("org-001", "env-dev", asOfDate, TestScale);

        var planByCountNo = WorldHistoryCountSpec.BuildCountPlans(asOfDate, TestScale)
            .ToDictionary(plan => plan.CountNo, StringComparer.Ordinal);
        var tasks = await db.StockCountTasks.ToArrayAsync();
        foreach (var task in tasks.Where(x => x.VarianceQuantity is not null))
        {
            var plan = planByCountNo[task.CountTaskCode];
            Assert.Equal(Math.Abs(plan.VarianceQuantity), Math.Abs(task.VarianceQuantity!.Value));
            Assert.NotEqual(0m, task.VarianceQuantity!.Value);
            Assert.True(task.CountedQuantity >= 0m);
        }

        var adjustments = await db.StockCountAdjustments.ToArrayAsync();
        Assert.All(adjustments, adjustment =>
        {
            Assert.Null(adjustment.MovementId);
            Assert.Null(adjustment.ConfirmedAtUtc);
            Assert.Equal(
                WorldHistoryCountSpec.ApprovalChainReference(adjustment.CountTaskCode),
                adjustment.ApprovalChainId);
            Assert.True(
                adjustment.Status is StockCountAdjustmentStatuses.PendingApproval
                    or StockCountAdjustmentStatuses.Voided,
                $"Unexpected adjustment status '{adjustment.Status}'.");
        });
    }

    [Theory]
    [MemberData(nameof(AsOfDates))]
    public async Task Count_seed_is_idempotent_for_any_as_of_date(int year, int month, int day)
    {
        var asOfDate = new DateOnly(year, month, day);
        await using var db = CreateDbContext();
        await SeedMovementChainAsync(db, asOfDate);
        var seed = new WorldHistoryCountSeedService(db);

        var first = await seed.SeedAsync("org-001", "env-dev", asOfDate, TestScale);
        var taskCount = await db.StockCountTasks.CountAsync();
        var adjustmentCount = await db.StockCountAdjustments.CountAsync();
        var statuses = (await db.StockCountTasks.Select(x => new { x.CountTaskCode, x.Status }).ToArrayAsync())
            .ToDictionary(x => x.CountTaskCode, x => x.Status, StringComparer.Ordinal);

        var second = await seed.SeedAsync("org-001", "env-dev", asOfDate, TestScale);

        Assert.Equal(0, second.StockCountTasksWritten);
        Assert.Equal(0, second.StockCountAdjustmentsWritten);
        Assert.Equal(taskCount, await db.StockCountTasks.CountAsync());
        Assert.Equal(adjustmentCount, await db.StockCountAdjustments.CountAsync());
        var afterStatuses = (await db.StockCountTasks.Select(x => new { x.CountTaskCode, x.Status }).ToArrayAsync())
            .ToDictionary(x => x.CountTaskCode, x => x.Status, StringComparer.Ordinal);
        Assert.Equal(statuses, afterStatuses);
        Assert.True(first.StockCountTasksWritten > 0);
    }

    /// <summary>历史铺开之后四档状态必须全部在场，否则盘点页的状态页签会莫名其妙全空。</summary>
    [Fact]
    public async Task Count_statuses_cover_all_four_history_states_once_history_has_unrolled()
    {
        var asOfDate = new DateOnly(2026, 7, 27);
        await using var db = CreateDbContext();
        await SeedMovementChainAsync(db, asOfDate);

        var report = await new WorldHistoryCountSeedService(db).SeedAsync("org-001", "env-dev", asOfDate, TestScale);

        Assert.True(report.Validation.PendingApprovalTasksChecked > 0);
        Assert.True(report.Validation.RecountRequiredTasksChecked > 0);
        Assert.True(report.Validation.CancelledTasksChecked > 0);
        Assert.True(report.Validation.OpenTasksChecked > 0);
        Assert.True(report.Validation.VarianceAmountTotal > 0m);
    }

    /// <summary>校验器 fail-closed：历史盘点一旦被过账成已确认态就必须让 seed 失败。</summary>
    [Fact]
    public async Task Validator_fails_closed_when_a_history_count_task_is_confirmed()
    {
        var asOfDate = new DateOnly(2026, 7, 27);
        await using var db = CreateDbContext();
        await SeedMovementChainAsync(db, asOfDate);
        await new WorldHistoryCountSeedService(db).SeedAsync("org-001", "env-dev", asOfDate, TestScale);

        var task = await db.StockCountTasks.FirstAsync(x => x.Status == StockCountTaskStatuses.PendingApproval);
        var ledger = await db.StockLedgers.FirstAsync(x =>
            x.SkuCode == task.SkuCode && x.LocationCode == task.LocationCode && x.LotNo == task.LotNo);
        task.ConfirmApprovedAdjustment(ledger, "manual-confirm-0001");
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var exception = await Assert.ThrowsAsync<WorldHistoryCountConsistencyException>(() =>
            new WorldHistoryCountValidator(db).ValidateAsync("org-001", "env-dev", asOfDate, TestScale));

        Assert.Contains(exception.Failures, failure => failure.Contains("已确认态", StringComparison.Ordinal));
    }

    /// <summary>
    /// #1374 · **盘点任务的快照版本必须是 seed 落幕时的台账版本**。
    ///
    /// 预留块会 <c>LedgerVersion++</c>，两块的维度 100% 重叠；先盘点后预留会让盘点任务
    /// 一出生即死单（确认差异当场判需复盘）。这里按 Program.cs 的真实顺序
    /// 流水 → 预留 → 盘点 跑完，断言每张可确认任务的快照都对得上。
    /// </summary>
    [Theory]
    [MemberData(nameof(AsOfDates))]
    public async Task Count_task_snapshot_version_matches_the_ledger_after_reservations_land(
        int year,
        int month,
        int day)
    {
        var asOfDate = new DateOnly(year, month, day);
        await using var db = CreateDbContext();
        await SeedMovementChainAsync(db, asOfDate);
        await new WorldHistoryReservationSeedService(db).SeedAsync("org-001", "env-dev", asOfDate, TestScale);

        await new WorldHistoryCountSeedService(db).SeedAsync("org-001", "env-dev", asOfDate, TestScale);

        var tasks = await db.StockCountTasks.AsNoTracking()
            .Where(x => x.Status != StockCountTaskStatuses.Cancelled)
            .ToArrayAsync();
        var ledgers = await db.StockLedgers.AsNoTracking().ToArrayAsync();
        Assert.NotEmpty(tasks);
        foreach (var task in tasks)
        {
            var ledger = Assert.Single(
                ledgers,
                x => x.SkuCode == task.SkuCode
                    && x.LocationCode == task.LocationCode
                    && x.LotNo == task.LotNo
                    && x.QualityStatus == task.QualityStatus);
            Assert.Equal(ledger.LedgerVersion, task.ExpectedLedgerVersion);
        }
    }

    /// <summary>
    /// #1374 的门禁反证：把顺序倒回「先盘点、后预留」，校验器必须 fail-closed，
    /// 而不是像修复前那样全绿放行一整批死单。
    /// </summary>
    [Fact]
    public async Task Validator_fails_closed_when_reservations_land_after_the_count_snapshot()
    {
        var asOfDate = new DateOnly(2026, 7, 27);
        await using var db = CreateDbContext();
        await SeedMovementChainAsync(db, asOfDate);
        await new WorldHistoryCountSeedService(db).SeedAsync("org-001", "env-dev", asOfDate, TestScale);
        await new WorldHistoryReservationSeedService(db).SeedAsync("org-001", "env-dev", asOfDate, TestScale);

        var exception = await Assert.ThrowsAsync<WorldHistoryCountConsistencyException>(() =>
            new WorldHistoryCountValidator(db).ValidateAsync("org-001", "env-dev", asOfDate, TestScale));

        Assert.Contains(exception.Failures, failure => failure.Contains("一出生即死单", StringComparison.Ordinal));
    }

    /// <summary>
    /// #1374 的次生脆弱性：未回单的盘点任务必须避开 WMS 当前队列占用的维度，
    /// 否则演示当天「先拣货、再确认盘点」照样把快照版本捅穿。
    /// </summary>
    [Theory]
    [MemberData(nameof(AsOfDates))]
    public void Open_count_plans_never_touch_the_current_queue_dimensions(int year, int month, int day)
    {
        var currentQueueSkus = WorldHistoryCountSpec.CurrentQueueDimensions
            .Select(dimension => dimension.SkuCode)
            .ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(currentQueueSkus);
        Assert.Empty(WorldHistoryCountSpec.OpenCountDimensions
            .Select(dimension => dimension.SkuCode)
            .Intersect(currentQueueSkus, StringComparer.Ordinal));

        var openPlans = WorldHistoryCountSpec.BuildCountPlans(new DateOnly(year, month, day), 1.0d)
            .Where(plan => plan.Outcome == WorldHistoryCountOutcome.Open)
            .ToArray();

        Assert.NotEmpty(openPlans);
        Assert.All(openPlans, plan => Assert.DoesNotContain(plan.SkuCode, currentQueueSkus));
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

    #region 读面（此前完全缺失，业务前端只能挂会话内本地队列）

    [Fact]
    public async Task Count_task_read_face_pages_and_reports_status_distribution()
    {
        var asOfDate = new DateOnly(2026, 7, 27);
        await using var db = CreateDbContext();
        await SeedMovementChainAsync(db, asOfDate);
        await new WorldHistoryCountSeedService(db).SeedAsync("org-001", "env-dev", asOfDate, TestScale);

        var handler = new ListStockCountTasksQueryHandler(db);
        var all = await handler.Handle(
            new ListStockCountTasksQuery("org-001", "env-dev", PageSize: 5), CancellationToken.None);

        Assert.Equal(await db.StockCountTasks.CountAsync(), all.TotalCount);
        Assert.Equal(5, all.Items.Count);
        Assert.Equal(1, all.Page);
        Assert.True(all.PendingApprovalCount > 0);
        Assert.True(all.RecountRequiredCount > 0);
        Assert.True(all.CancelledCount > 0);
        Assert.True(all.OpenCount > 0);
        Assert.Equal(0, all.ConfirmedCount);

        // 状态过滤只收敛列表，不该让状态分布计数自我坍缩。
        var pending = await handler.Handle(
            new ListStockCountTasksQuery("org-001", "env-dev", Status: StockCountTaskStatuses.PendingApproval),
            CancellationToken.None);
        Assert.Equal(all.PendingApprovalCount, pending.TotalCount);
        Assert.Equal(all.OpenCount, pending.OpenCount);
        Assert.All(pending.Items, item => Assert.Equal(StockCountTaskStatuses.PendingApproval, item.Status));

        // 翻页不重不漏。
        var page2 = await handler.Handle(
            new ListStockCountTasksQuery("org-001", "env-dev", Page: 2, PageSize: 5), CancellationToken.None);
        Assert.Empty(all.Items.Select(x => x.CountTaskId).Intersect(page2.Items.Select(x => x.CountTaskId)));

        // 租户边界：别的组织读不到本组织的盘点。
        var otherTenant = await handler.Handle(
            new ListStockCountTasksQuery("org-002", "env-dev"), CancellationToken.None);
        Assert.Equal(0, otherTenant.TotalCount);
        Assert.Empty(otherTenant.Items);
    }

    [Fact]
    public async Task Count_adjustment_read_face_exposes_approval_provenance()
    {
        var asOfDate = new DateOnly(2026, 7, 27);
        await using var db = CreateDbContext();
        await SeedMovementChainAsync(db, asOfDate);
        await new WorldHistoryCountSeedService(db).SeedAsync("org-001", "env-dev", asOfDate, TestScale);

        var handler = new ListStockCountAdjustmentsQueryHandler(db);
        var all = await handler.Handle(
            new ListStockCountAdjustmentsQuery("org-001", "env-dev"), CancellationToken.None);

        Assert.Equal(await db.StockCountAdjustments.CountAsync(), all.TotalCount);
        Assert.True(all.PendingApprovalCount > 0);
        Assert.True(all.VoidedCount > 0);
        Assert.Equal(0, all.PostedCount);
        Assert.True(all.VarianceAmountTotal > 0m);
        Assert.All(all.Items, item =>
        {
            Assert.StartsWith("APPR-CNT-2026-", item.ApprovalChainId!, StringComparison.Ordinal);
            Assert.Null(item.MovementId);
        });

        var byCountTask = await handler.Handle(
            new ListStockCountAdjustmentsQuery("org-001", "env-dev", CountTaskCode: all.Items.First().CountTaskCode),
            CancellationToken.None);
        Assert.Equal(1, byCountTask.TotalCount);
    }

    [Fact]
    public async Task Movement_read_face_filters_by_dimension_and_date_range()
    {
        var asOfDate = new DateOnly(2026, 7, 27);
        await using var db = CreateDbContext();
        await SeedMovementChainAsync(db, asOfDate);

        var handler = new ListStockMovementsQueryHandler(db);
        var all = await handler.Handle(
            new ListStockMovementsQuery("org-001", "env-dev", PageSize: 20), CancellationToken.None);

        Assert.Equal(await db.StockMovements.CountAsync(), all.TotalCount);
        Assert.Equal(20, all.Items.Count);
        Assert.True(all.InboundQuantityTotal > 0m);
        Assert.True(all.OutboundQuantityTotal > 0m);

        // 时间倒序：页面默认「最近发生的在最上面」。
        var postedTimes = all.Items.Select(x => x.PostedAtUtc).ToArray();
        Assert.Equal(postedTimes.OrderByDescending(x => x).ToArray(), postedTimes);

        var skuCode = all.Items.First().SkuCode;
        var bySku = await handler.Handle(
            new ListStockMovementsQuery("org-001", "env-dev", SkuCode: skuCode, PageSize: 200),
            CancellationToken.None);
        Assert.All(bySku.Items, item => Assert.Equal(skuCode, item.SkuCode));
        Assert.True(bySku.TotalCount <= all.TotalCount);

        // 日期区间收敛：上线日之前不该有任何世界观流水。
        var beforeGoLive = await handler.Handle(
            new ListStockMovementsQuery(
                "org-001", "env-dev", ToDate: WorldHistoryCalendar.GoLiveDate.AddDays(-1)),
            CancellationToken.None);
        Assert.Equal(0, beforeGoLive.TotalCount);
    }

    #endregion

    private static async Task SeedMovementChainAsync(ApplicationDbContext dbContext, DateOnly asOfDate) =>
        await new WorldHistorySeedService(dbContext).SeedAsync("org-001", "env-dev", asOfDate, TestScale);

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"inventory-world-history-count-{Guid.CreateVersion7():N}")
            .Options;
        return new ApplicationDbContext(options, new WorldHistoryCountTestMediator());
    }

    private sealed class WorldHistoryCountTestMediator : IMediator
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
