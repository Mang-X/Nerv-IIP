using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ScheduleAggregate;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Seed;

namespace Nerv.IIP.Business.Mes.Web.Tests;

/// <summary>
/// 《工厂世界观设定集》L1「规则排程」块的形状与幂等性证据，外加历史排程读面
/// （<see cref="ListScheduleResultsQuery"/>）的行为——两者是同一件事的两半：
/// 表里没有历史事实、服务端也没有列表端点，「规则排程」页刷新后就永远是空的。
/// </summary>
public sealed class WorldHistoryScheduleResultSeedServiceTests
{
    private const double TestScale = 0.02d;

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
    public async Task Schedule_result_seed_fills_the_table_for_any_as_of_date(int year, int month, int day)
    {
        var asOfDate = new DateOnly(year, month, day);
        await using var dbContext = WorldHistorySeedTestContext.Create();
        await WorldHistorySeedTestContext.SeedWorkOrderChainAsync(dbContext, asOfDate, TestScale);

        var report = await new WorldHistoryScheduleResultSeedService(dbContext).SeedAsync("org-001", "env-dev", TestScale);

        Assert.True(report.ScheduleResultsWritten > 0);
        Assert.Equal(report.ScheduleResultsWritten, await dbContext.ScheduleResults.CountAsync());
        Assert.Equal(report.ScheduleResultsWritten, report.Validation.ScheduleResultsChecked);

        // 每个周计划号至少有一次基线排程。
        var planIds = (await dbContext.OperationTasks
                .Where(x => x.SchedulePlanId != null)
                .Select(x => x.SchedulePlanId!)
                .Distinct()
                .ToArrayAsync())
            .ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(planIds);
        Assert.True(report.ScheduleResultsWritten >= planIds.Count);
        Assert.Contains(await dbContext.ScheduleResults.ToArrayAsync(), x => x.Trigger == ScheduleTrigger.Manual);
    }

    [Theory]
    [MemberData(nameof(AsOfDates))]
    public async Task Schedule_results_anchor_on_real_operation_tasks(int year, int month, int day)
    {
        var asOfDate = new DateOnly(year, month, day);
        await using var dbContext = WorldHistorySeedTestContext.Create();
        await WorldHistorySeedTestContext.SeedWorkOrderChainAsync(dbContext, asOfDate, TestScale);
        await new WorldHistoryScheduleResultSeedService(dbContext).SeedAsync("org-001", "env-dev", TestScale);

        var workOrderIds = (await dbContext.WorkOrders.Select(x => x.WorkOrderIdValue).ToArrayAsync())
            .ToHashSet(StringComparer.Ordinal);
        var taskIds = (await dbContext.OperationTasks.Select(x => x.OperationTaskIdValue).ToArrayAsync())
            .ToHashSet(StringComparer.Ordinal);
        var workCenters = WorldHistoryFloorEventsSpec.WorkCenterIds.ToHashSet(StringComparer.Ordinal);
        var reasons = new[]
        {
            ScheduleTrigger.Manual, ScheduleTrigger.RushOrder,
            ScheduleTrigger.AssetUnavailable, ScheduleTrigger.AssetRestored,
        }.Select(WorldHistoryScheduleResultSpec.ReasonText).ToHashSet(StringComparer.Ordinal);

        var results = await dbContext.ScheduleResults.ToArrayAsync();
        Assert.NotEmpty(results);
        Assert.All(results, result =>
        {
            Assert.NotEmpty(result.Assignments);
            Assert.True(result.Assignments.Count <= WorldHistoryScheduleResultSpec.MaxAssignmentsPerRun);
            Assert.NotEmpty(result.AffectedWorkOrderIds);
            Assert.All(result.Assignments, assignment =>
            {
                Assert.Contains(assignment.WorkOrderId, workOrderIds);
                Assert.Contains(assignment.OperationTaskId, taskIds);
                Assert.Contains(assignment.WorkCenterId, workCenters);
                Assert.True(assignment.EndUtc >= assignment.StartUtc);
                // 原因文案必须是中文口径，不是裸枚举名。
                Assert.Contains(assignment.Reason, reasons);
            });
            Assert.All(result.AffectedWorkOrderIds, workOrderId => Assert.Contains(workOrderId, workOrderIds));
        });

        // 版本号唯一、自 1 起连续——运行时的「已有条数 + 1」才不会撞号。
        var versions = results.Select(x => x.ScheduleVersion).Order().ToArray();
        Assert.Equal(versions.Length, versions.Distinct().Count());
        Assert.Equal(1, versions[0]);
        Assert.Equal(versions.Length, versions[^1]);
    }

    [Theory]
    [MemberData(nameof(AsOfDates))]
    public async Task Schedule_result_seed_is_idempotent_for_any_as_of_date(int year, int month, int day)
    {
        var asOfDate = new DateOnly(year, month, day);
        await using var dbContext = WorldHistorySeedTestContext.Create();
        await WorldHistorySeedTestContext.SeedWorkOrderChainAsync(dbContext, asOfDate, TestScale);
        var seed = new WorldHistoryScheduleResultSeedService(dbContext);

        var first = await seed.SeedAsync("org-001", "env-dev", TestScale);
        var count = await dbContext.ScheduleResults.CountAsync();

        var second = await seed.SeedAsync("org-001", "env-dev", TestScale);

        Assert.Equal(0, second.ScheduleResultsWritten);
        Assert.Equal(count, await dbContext.ScheduleResults.CountAsync());
        Assert.True(first.ScheduleResultsWritten > 0);
    }

    /// <summary>工序链没落库时宁可不写，也不造假工序号。</summary>
    [Fact]
    public async Task Schedule_results_are_skipped_when_the_work_order_chain_is_missing()
    {
        await using var dbContext = WorldHistorySeedTestContext.Create();

        var report = await new WorldHistoryScheduleResultSeedService(dbContext).SeedAsync("org-001", "env-dev", TestScale);

        Assert.Equal(0, report.ScheduleResultsWritten);
    }

    /// <summary>历史排程读面：分页、触发原因过滤、倒序、分配明细随行返回。</summary>
    [Fact]
    public async Task Schedule_result_read_face_lists_history_with_paging_and_trigger_filter()
    {
        var asOfDate = new DateOnly(2026, 4, 15);
        await using var dbContext = WorldHistorySeedTestContext.Create();
        await WorldHistorySeedTestContext.SeedWorkOrderChainAsync(dbContext, asOfDate, TestScale);
        await new WorldHistoryScheduleResultSeedService(dbContext).SeedAsync("org-001", "env-dev", TestScale);

        var handler = new ListScheduleResultsQueryHandler(dbContext);
        var all = await handler.Handle(new ListScheduleResultsQuery("org-001", "env-dev", Take: 100), default);

        Assert.True(all.Total > 1);
        Assert.NotEmpty(all.Items);
        Assert.All(all.Items, item =>
        {
            Assert.True(item.ScheduleVersion > 0);
            Assert.Equal(item.Assignments.Count, item.AssignmentCount);
            Assert.Equal(item.AffectedWorkOrderIds.Count, item.AffectedWorkOrderCount);
            Assert.Contains(
                item.Trigger,
                Enum.GetNames<ScheduleTrigger>());
        });

        // 倒序：最新一次排程在最前。
        var scheduledAt = all.Items.Select(x => x.ScheduledAtUtc).ToArray();
        Assert.Equal(scheduledAt.OrderByDescending(x => x).ToArray(), scheduledAt);

        // 分页
        var firstPage = await handler.Handle(new ListScheduleResultsQuery("org-001", "env-dev", Take: 1), default);
        Assert.Single(firstPage.Items);
        Assert.Equal(all.Total, firstPage.Total);
        var secondPage = await handler.Handle(new ListScheduleResultsQuery("org-001", "env-dev", Skip: 1, Take: 1), default);
        Assert.NotEqual(firstPage.Items.First().ScheduleVersion, secondPage.Items.First().ScheduleVersion);

        // 触发原因过滤（大小写不敏感）
        var manual = await handler.Handle(
            new ListScheduleResultsQuery("org-001", "env-dev", Trigger: "manual", Take: 100), default);
        Assert.NotEmpty(manual.Items);
        Assert.All(manual.Items, item => Assert.Equal(nameof(ScheduleTrigger.Manual), item.Trigger));
        Assert.True(manual.Total <= all.Total);

        // 无法识别的触发原因不做过滤（不至于把页面打空）
        var unknown = await handler.Handle(
            new ListScheduleResultsQuery("org-001", "env-dev", Trigger: "not-a-trigger", Take: 100), default);
        Assert.Equal(all.Total, unknown.Total);
    }

    /// <summary>@scale=1.0 规模：29 周 × （1 次基线 + 0–3 次重排），落在 60–200 次之间。</summary>
    [Fact]
    public void Full_scale_volumes_match_the_world_bible_shape()
    {
        var planIds = Enumerable.Range(2, 30).Select(week => $"SP-2026-W{week:D2}").ToArray();
        var runs = WorldHistoryScheduleResultSpec.BuildRuns(planIds, 1.0d);

        Assert.InRange(runs.Count, 60, 200);
        Assert.Equal(planIds.Length, runs.Count(x => x.Trigger == ScheduleTrigger.Manual));
        Assert.Contains(runs, x => x.Trigger == ScheduleTrigger.RushOrder);
        Assert.Contains(runs, x => x.Trigger == ScheduleTrigger.AssetUnavailable);
        Assert.Contains(runs, x => x.Trigger == ScheduleTrigger.AssetRestored);

        // 版本号沿时间递增且连续。
        Assert.Equal(Enumerable.Range(1, runs.Count), runs.Select(x => x.ScheduleVersion));
        Assert.Equal(runs.OrderBy(x => x.ScheduledAtUtc).Select(x => x.SchedulePlanId), runs.Select(x => x.SchedulePlanId));
        Assert.Empty(WorldHistoryScheduleResultSpec.BuildRuns([], 1.0d));
    }

    /// <summary>周计划号 → 周一零点的解析必须与 <c>WorldHistoryMesSpec.SchedulePlanId</c> 互逆。</summary>
    [Fact]
    public void Week_start_is_the_inverse_of_the_schedule_plan_id()
    {
        foreach (var day in new[]
        {
            new DateOnly(2026, 1, 5), new DateOnly(2026, 4, 15), new DateOnly(2026, 7, 27), new DateOnly(2026, 12, 31),
        })
        {
            var planId = WorldHistoryMesSpec.SchedulePlanId(day);
            var weekStart = WorldHistoryScheduleResultSpec.WeekStartUtc(planId);

            Assert.Equal(DayOfWeek.Monday, weekStart.DayOfWeek);
            Assert.Equal(planId, WorldHistoryMesSpec.SchedulePlanId(DateOnly.FromDateTime(weekStart.UtcDateTime)));
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => WorldHistoryScheduleResultSpec.WeekStartUtc("SP-BAD"));
    }
}
