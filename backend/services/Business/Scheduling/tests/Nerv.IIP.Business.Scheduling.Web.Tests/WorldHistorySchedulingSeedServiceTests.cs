using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Web.Application.Seed;
using Nerv.IIP.Contracts.Scheduling;
using Xunit.Abstractions;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

/// <summary>
/// L1 背景历史（排产域侧）的常规门禁测试：形状、确定性、幂等、生命周期分布、
/// 号段隔离、问题快照可反序列化（「锁定重预览」的前提）、fail-closed。
/// </summary>
public sealed class WorldHistorySchedulingSeedServiceTests(ITestOutputHelper output)
{
    private static readonly DateOnly AsOfDate = new(2026, 7, 27);

    /// <summary>库写入类用例的规模：足够覆盖全链，又不让 InMemory provider 变慢。</summary>
    private const double SmallScale = 0.05d;

    [Fact]
    public void Full_scale_fact_stream_matches_the_world_bible_shape()
    {
        var facts = WorldHistorySchedulingSpec.BuildSchedulingFacts(AsOfDate, 1.0d);

        output.WriteLine($"scheduling-world-history-plans={facts.Plans.Count}");
        output.WriteLine($"scheduling-world-history-assignments={facts.AssignmentCount}");
        output.WriteLine($"scheduling-world-history-resource-loads={facts.ResourceLoadCount}");
        output.WriteLine($"scheduling-world-history-conflicts={facts.ConflictCount}");
        output.WriteLine($"scheduling-world-history-unscheduled={facts.UnscheduledOperationCount}");
        output.WriteLine($"scheduling-world-history-urgencies={facts.Urgencies.Count}");
        foreach (var status in Enum.GetValues<SchedulePlanLifecycleStatus>())
        {
            output.WriteLine($"scheduling-world-history-status-{status}={facts.CountOf(status)}");
        }

        // 上线日到 asOfDate 约 29–30 周，每周一版、约四成的周额外一版重排。
        Assert.InRange(facts.Plans.Count, 30, 60);
        Assert.Equal(1, facts.CountOf(SchedulePlanLifecycleStatus.Released));
        Assert.Equal(WorldHistorySchedulingSpec.PendingGeneratedPlanCount, facts.CountOf(SchedulePlanLifecycleStatus.Generated));
        Assert.InRange(facts.CountOf(SchedulePlanLifecycleStatus.Revoked), 0, WorldHistorySchedulingSpec.MaxRevokedPlanCount);
        Assert.Equal(
            facts.Plans.Count - 1 - WorldHistorySchedulingSpec.PendingGeneratedPlanCount - facts.CountOf(SchedulePlanLifecycleStatus.Revoked),
            facts.CountOf(SchedulePlanLifecycleStatus.Superseded));

        // 单方案 200–500 条资源分配：春节低谷周与队尾未满周天然更小，因此按均值卡形状、按上限卡单方案。
        Assert.InRange(facts.AssignmentCount / facts.Plans.Count, 200, 500);
        foreach (var plan in facts.Plans)
        {
            Assert.InRange(plan.Assignments.Count, 1, 500);
            Assert.NotEmpty(plan.ResourceLoads);
            Assert.InRange(plan.Conflicts.Count, 0, 8);
            Assert.InRange(plan.UnscheduledOperations.Count, 0, 15);
        }

        // 覆盖方案里出现过的每个工单，否则紧急度徽标全部走 MissingContract 兜底。
        Assert.Equal(
            facts.Plans.SelectMany(x => x.Orders).Select(x => x.WorkOrderNo).Distinct(StringComparer.Ordinal).Count(),
            facts.Urgencies.Count);
    }

    [Fact]
    public void Assignments_stay_inside_shift_windows_on_working_days()
    {
        var facts = WorldHistorySchedulingSpec.BuildSchedulingFacts(AsOfDate, 0.1d);

        foreach (var assignment in facts.Plans.SelectMany(x => x.Assignments))
        {
            var localStart = assignment.StartUtc.ToOffset(WorldHistoryCalendar.SiteUtcOffset);
            var localEnd = assignment.EndUtc.ToOffset(WorldHistoryCalendar.SiteUtcOffset);
            Assert.True(WorldHistoryCalendar.IsWorkingDay(DateOnly.FromDateTime(localStart.DateTime)),
                $"{assignment.AssignmentId} 落在周日停产日。");
            Assert.True(localStart.Hour >= WorldHistoryCalendar.EarlyShiftStartLocalHour,
                $"{assignment.AssignmentId} 早于早班开班时间。");
            Assert.True(localEnd.Date == localStart.Date || localEnd.TimeOfDay == TimeSpan.Zero,
                $"{assignment.AssignmentId} 跨越了班次窗口。");
            Assert.True(assignment.EndUtc > assignment.StartUtc);
        }
    }

    [Fact]
    public void Resource_utilization_looks_like_a_real_shop_floor()
    {
        var facts = WorldHistorySchedulingSpec.BuildSchedulingFacts(AsOfDate, 0.1d);

        var loads = facts.Plans.SelectMany(x => x.ResourceLoads).ToArray();
        Assert.NotEmpty(loads);
        Assert.All(loads, load => Assert.InRange(load.Utilization, 0.5m, 1.1m));
        // 瓶颈线（电泳 / 性能终检）必须出现高于 0.95 的负荷。
        Assert.Contains(loads, load =>
            WorldHistoryMesSpec.BottleneckWorkCenters.Contains(WorldHistorySchedulingSpec.WorkCenterOf(load.ResourceId)) &&
            load.Utilization > 0.95m);
    }

    [Fact]
    public void Fact_stream_is_deterministic_for_the_same_inputs()
    {
        var first = WorldHistorySchedulingSpec.BuildSchedulingFacts(AsOfDate, 0.1d);
        var second = WorldHistorySchedulingSpec.BuildSchedulingFacts(AsOfDate, 0.1d);

        Assert.Equal(first.Plans.Count, second.Plans.Count);
        for (var index = 0; index < first.Plans.Count; index++)
        {
            var left = first.Plans[index];
            var right = second.Plans[index];
            Assert.Equal(left.PlanId, right.PlanId);
            Assert.Equal(left.ProblemFingerprint, right.ProblemFingerprint);
            Assert.Equal(left.GeneratedAtUtc, right.GeneratedAtUtc);
            Assert.Equal(left.ReleaseRevision, right.ReleaseRevision);
            Assert.Equal(left.Status, right.Status);
            Assert.Equal(left.Assignments, right.Assignments);
            Assert.Equal(left.ResourceLoads, right.ResourceLoads);
            Assert.Equal(left.Conflicts, right.Conflicts);
            Assert.Equal(left.UnscheduledOperations, right.UnscheduledOperations);
        }

        // 紧急度事实内嵌一个风险清单（record 相等对内嵌列表走引用比较），逐字段展平后比对。
        Assert.Equal(Flatten(first.Urgencies), Flatten(second.Urgencies));
    }

    private static string[] Flatten(IReadOnlyList<WorldHistoryUrgencyFact> facts) =>
        [.. facts.Select(fact =>
            $"{fact.OrderId}|{fact.BusinessReference}|{fact.CalculatedAtUtc:O}|{fact.CalculationBucketUtc:O}|" +
            $"{fact.DueUtc:O}|{fact.RemainingCycle}|{fact.IsSourceStale}|{fact.FactsObservedAtUtc:O}|" +
            $"{fact.InputFingerprint}|{string.Join(',', fact.ExecutionRisks.Select(risk => risk.ReasonCode))}")];

    [Fact]
    public void Assignments_pair_with_the_shared_mes_work_order_and_operation_task_formula()
    {
        var facts = WorldHistorySchedulingSpec.BuildSchedulingFacts(AsOfDate, 0.1d);

        foreach (var assignment in facts.Plans.SelectMany(x => x.Assignments))
        {
            Assert.StartsWith("WO-2026-", assignment.OrderId, StringComparison.Ordinal);
            Assert.Equal(
                WorldHistoryMesSpec.OperationTaskId(assignment.OrderId, assignment.OperationSequence),
                assignment.OperationId);
            Assert.Equal(
                assignment.WorkCenterId,
                WorldHistorySchedulingSpec.WorkCenterOf(assignment.ResourceId));
        }
    }

    /// <summary>
    /// 演示走查缺口：排产工作台页完全空白（<c>nerv_iip_scheduling</c> 业务表 0 行）。
    /// 全链写入 + 幂等重跑零写入，且对任意 asOfDate（含周日、春节段、月末冲量窗口）成立；
    /// 量以 spec 事实流为准，不空断。
    /// </summary>
    [Theory]
    [InlineData(2026, 7, 27)]
    [InlineData(2026, 7, 26)]
    [InlineData(2026, 8, 2)]
    [InlineData(2026, 2, 16)]
    [InlineData(2026, 7, 31)]
    public async Task Seed_writes_the_full_chain_and_reruns_without_writing_anything(int year, int month, int day)
    {
        await using var db = CreateDbContext();
        var seed = new WorldHistorySeedService(db);
        var asOfDate = new DateOnly(year, month, day);

        var first = await seed.SeedAsync("org-001", "env-dev", asOfDate, SmallScale);
        var second = await seed.SeedAsync("org-001", "env-dev", asOfDate, SmallScale);

        var facts = WorldHistorySchedulingSpec.BuildSchedulingFacts(asOfDate, SmallScale);
        output.WriteLine($"small-scale-{asOfDate:yyyy-MM-dd}-plans={first.SchedulePlansWritten}");
        output.WriteLine($"small-scale-{asOfDate:yyyy-MM-dd}-assignments={first.AssignmentsWritten}");

        Assert.Equal(facts.Plans.Count, first.SchedulePlansWritten);
        Assert.Equal(facts.Plans.Count, first.ScheduleProblemsWritten);
        Assert.Equal(facts.AssignmentCount, first.AssignmentsWritten);
        Assert.Equal(facts.ResourceLoadCount, first.ResourceLoadsWritten);
        Assert.Equal(facts.ConflictCount, first.ConflictsWritten);
        Assert.Equal(facts.UnscheduledOperationCount, first.UnscheduledOperationsWritten);
        Assert.Equal(facts.Urgencies.Count, first.OrderUrgencySnapshotsWritten);

        Assert.Equal(0, second.SchedulePlansWritten);
        Assert.Equal(0, second.ScheduleProblemsWritten);
        Assert.Equal(0, second.AssignmentsWritten);
        Assert.Equal(0, second.ResourceLoadsWritten);
        Assert.Equal(0, second.ConflictsWritten);
        Assert.Equal(0, second.UnscheduledOperationsWritten);
        Assert.Equal(0, second.OrderUrgencySnapshotsWritten);

        // 库终态 == spec 事实流。
        Assert.Equal(facts.Plans.Count, await db.SchedulePlans.CountAsync());
        Assert.Equal(facts.Plans.Count, await db.ScheduleProblems.CountAsync());
        Assert.Equal(facts.Urgencies.Count, await db.OrderUrgencySnapshots.CountAsync());
        var persistedAssignments = await db.SchedulePlans.AsNoTracking().SumAsync(x => x.Assignments.Count);
        Assert.Equal(facts.AssignmentCount, persistedAssignments);

        // 生命周期分布：恰一个已发布（ux_schedule_plans_scope_active_release），队尾待发布。
        Assert.Equal(1, await db.SchedulePlans.CountAsync(x => x.Status == SchedulePlanLifecycleStatus.Released));
        Assert.Equal(
            facts.CountOf(SchedulePlanLifecycleStatus.Generated),
            await db.SchedulePlans.CountAsync(x => x.Status == SchedulePlanLifecycleStatus.Generated));
        Assert.Equal(
            facts.CountOf(SchedulePlanLifecycleStatus.Superseded),
            await db.SchedulePlans.CountAsync(x => x.Status == SchedulePlanLifecycleStatus.Superseded));
        Assert.Equal(
            facts.CountOf(SchedulePlanLifecycleStatus.Revoked),
            await db.SchedulePlans.CountAsync(x => x.Status == SchedulePlanLifecycleStatus.Revoked));

        // 发布号单调唯一（ux_schedule_plans_scope_release_revision）。
        var revisions = await db.SchedulePlans.AsNoTracking()
            .Where(x => x.ReleaseRevision != null)
            .Select(x => x.ReleaseRevision!.Value)
            .ToArrayAsync();
        Assert.Equal(revisions.Length, revisions.Distinct().Count());
        Assert.Equal(Enumerable.Range(1, revisions.Length).Select(x => (long)x), revisions.OrderBy(x => x));

        // 号段格式与保留段隔离。
        var planIds = await db.SchedulePlans.Select(x => x.PlanId).ToArrayAsync();
        Assert.All(planIds, planId => Assert.Matches(@"^SP-2026-\d{4}$", planId));
        var problemIds = await db.ScheduleProblems.Select(x => x.ProblemId).ToArrayAsync();
        Assert.All(problemIds, problemId => Assert.Matches(@"^SPB-2026-\d{4}$", problemId));
        Assert.All(
            planIds.Concat(problemIds),
            reference => Assert.DoesNotContain(
                WorldHistorySchedulingSpec.ReservedInfixes,
                infix => reference.Contains(infix, StringComparison.Ordinal)));
    }

    /// <summary>
    /// 「锁定重预览」从 <c>ProblemJson</c> 反序列化重建 problem，没有它 <c>SingleAsync</c> 直接抛异常。
    /// </summary>
    [Fact]
    public async Task Problem_snapshots_deserialize_back_into_the_scheduling_contract()
    {
        await using var db = CreateDbContext();
        await new WorldHistorySeedService(db).SeedAsync("org-001", "env-dev", AsOfDate, SmallScale);

        var snapshots = await db.ScheduleProblems.AsNoTracking().ToArrayAsync();
        Assert.NotEmpty(snapshots);
        foreach (var snapshot in snapshots)
        {
            var problem = JsonSerializer.Deserialize<SchedulingProblemContract>(snapshot.ProblemJson, SchedulingJson.Options);
            Assert.NotNull(problem);
            Assert.Equal("org-001", problem.OrganizationId);
            Assert.Equal("env-dev", problem.EnvironmentId);
            Assert.Equal(snapshot.ProblemId, problem.ProblemId);
            Assert.NotEmpty(problem.Orders);
            Assert.NotEmpty(problem.Resources);
            Assert.NotEmpty(problem.Calendars);
            Assert.All(problem.Orders, order => Assert.NotEmpty(order.Operations));

            // 锁定工序必须落在本快照的订单/工序/合格资源集合内，否则重预览会 KnownException。
            var operations = problem.Orders
                .SelectMany(order => order.Operations.Select(operation => (order.OrderId, operation)))
                .ToDictionary(x => (x.OrderId, x.operation.OperationId));
            foreach (var locked in problem.LockedAssignments)
            {
                Assert.True(operations.TryGetValue((locked.OrderId, locked.OperationId), out var source));
                Assert.Contains(locked.ResourceId, source.operation.EligibleResourceIds);
                Assert.InRange(locked.StartUtc, problem.HorizonStartUtc, problem.HorizonEndUtc);
                Assert.InRange(locked.EndUtc, problem.HorizonStartUtc, problem.HorizonEndUtc);
            }
        }
    }

    [Fact]
    public async Task Validator_fails_closed_when_history_rows_are_tampered_with()
    {
        await using var db = CreateDbContext();
        await new WorldHistorySeedService(db).SeedAsync("org-001", "env-dev", AsOfDate, SmallScale);

        var victim = await db.ScheduleProblems.FirstAsync();
        db.ScheduleProblems.Remove(victim);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var exception = await Assert.ThrowsAsync<WorldHistoryConsistencyException>(() =>
            new WorldHistoryConsistencyValidator(db).ValidateAsync("org-001", "env-dev", AsOfDate, SmallScale));
        Assert.NotEmpty(exception.Failures);
        Assert.StartsWith("World-history scheduling seed validation failed", exception.Message, StringComparison.Ordinal);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"scheduling-world-history-{Guid.CreateVersion7():N}")
            .Options;
        return new ApplicationDbContext(options, new WorldHistoryTestMediator());
    }

    private sealed class WorldHistoryTestMediator : IMediator
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
