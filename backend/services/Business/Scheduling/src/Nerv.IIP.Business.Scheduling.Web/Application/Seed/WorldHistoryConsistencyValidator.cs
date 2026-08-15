using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》§7 一致性校验器的 **排产域侧**。
///
/// 覆盖：方案数量与四张明细逐方案对账、生命周期分布（同一 org/env 只允许一个 Released）、
/// <c>ReleaseRevision</c> 沿时间轴单调唯一、Superseded 链指向下一版、
/// 问题快照能被 <c>CreateSchedulePlanRevisionCommandHandler</c> 用同一套
/// <see cref="SchedulingJson.Options"/> 反序列化回 <see cref="SchedulingProblemContract"/>
/// 且订单集合覆盖全部资源分配、订单紧急度快照覆盖方案里出现过的每个工单、
/// 全部时间戳落在 [上线日, asOfDate] 窗口内、与固定演示事实（<c>*-DEMO-*</c>）
/// 和千单规模块（<c>*-SCALE-*</c>）隔离。
///
/// **fail-closed**：任何一条不成立即抛 <see cref="WorldHistoryConsistencyException"/>（中文累积）。
/// </summary>
public sealed class WorldHistoryConsistencyValidator(ApplicationDbContext dbContext)
{
    public const int SampleSize = 10;

    public async Task<WorldHistorySchedulingValidationReport> ValidateAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        double scale,
        CancellationToken cancellationToken = default)
    {
        var facts = WorldHistorySchedulingSpec.BuildSchedulingFacts(asOfDate, scale);
        var failures = new List<string>();
        var lowerBound = new DateTimeOffset(WorldHistoryCalendar.GoLiveDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var upperBound = WorldHistorySchedulingSpec.HistoryUpperBound(asOfDate);

        var plans = await dbContext.SchedulePlans.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.PlanId.StartsWith(WorldHistorySchedulingSpec.PlanNumberPrefix))
            .Select(x => new PersistedPlan(
                x.PlanId,
                x.ProblemId,
                x.ProblemFingerprint,
                x.Status,
                x.ReleaseRevision,
                x.SupersededByPlanId,
                x.GeneratedAtUtc,
                x.ReleasedAtUtc,
                x.RevokedAtUtc,
                x.Assignments.Count,
                x.ResourceLoads.Count,
                x.Conflicts.Count,
                x.UnscheduledOperations.Count))
            .ToListAsync(cancellationToken);

        var problems = await dbContext.ScheduleProblems.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.ProblemId.StartsWith(WorldHistorySchedulingSpec.ProblemNumberPrefix))
            .ToListAsync(cancellationToken);

        var urgencyOrderIds = await dbContext.OrderUrgencySnapshots.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .Select(x => x.OrderId)
            .ToArrayAsync(cancellationToken);

        CheckPlans(facts, plans, lowerBound, upperBound, failures);
        CheckLifecycle(facts, plans, failures);
        CheckProblems(facts, problems, organizationId, environmentId, lowerBound, upperBound, failures);
        CheckUrgencies(facts, urgencyOrderIds, failures);
        CheckIsolation(plans, problems, failures);

        if (failures.Count > 0)
        {
            throw new WorldHistoryConsistencyException(failures);
        }

        var sample = facts.Plans
            .TakeLast(SampleSize)
            .Select(fact => string.Create(
                CultureInfo.InvariantCulture,
                $"{fact.PlanId}（{StatusName(fact.Status)}{(fact.ReleaseRevision is { } revision ? $" rev.{revision}" : string.Empty)}）" +
                $" {fact.WeekStart:yyyy-MM-dd} 起两周窗口：{fact.Orders.Count} 单 / {fact.Assignments.Count} 工序 / " +
                $"{fact.ResourceLoads.Count} 资源 / {fact.Conflicts.Count} 冲突 / {fact.UnscheduledOperations.Count} 不可排"))
            .ToArray();

        return new WorldHistorySchedulingValidationReport(
            PlansChecked: plans.Count,
            ProblemsChecked: problems.Count,
            AssignmentsChecked: plans.Sum(x => x.AssignmentCount),
            UrgencySnapshotsChecked: urgencyOrderIds.Length,
            GeneratedChecked: plans.Count(x => x.Status == SchedulePlanLifecycleStatus.Generated),
            ReleasedChecked: plans.Count(x => x.Status == SchedulePlanLifecycleStatus.Released),
            SupersededChecked: plans.Count(x => x.Status == SchedulePlanLifecycleStatus.Superseded),
            RevokedChecked: plans.Count(x => x.Status == SchedulePlanLifecycleStatus.Revoked),
            Sample: sample);
    }

    private sealed record PersistedPlan(
        string PlanId,
        string ProblemId,
        string ProblemFingerprint,
        SchedulePlanLifecycleStatus Status,
        long? ReleaseRevision,
        string? SupersededByPlanId,
        DateTimeOffset GeneratedAtUtc,
        DateTimeOffset? ReleasedAtUtc,
        DateTimeOffset? RevokedAtUtc,
        int AssignmentCount,
        int ResourceLoadCount,
        int ConflictCount,
        int UnscheduledOperationCount);

    private static void CheckPlans(
        WorldHistorySchedulingFacts facts,
        IReadOnlyList<PersistedPlan> plans,
        DateTimeOffset lowerBound,
        DateTimeOffset upperBound,
        List<string> failures)
    {
        if (plans.Count != facts.Plans.Count)
        {
            failures.Add($"库内世界观排产方案 {plans.Count} 个，与事实流 {facts.Plans.Count} 个不一致。");
        }

        var byPlanId = plans.ToDictionary(x => x.PlanId, StringComparer.Ordinal);
        foreach (var fact in facts.Plans)
        {
            if (!byPlanId.TryGetValue(fact.PlanId, out var plan))
            {
                failures.Add($"排产方案 {fact.PlanId} 缺失。");
                continue;
            }

            if (!string.Equals(plan.ProblemId, fact.ProblemId, StringComparison.Ordinal) ||
                !string.Equals(plan.ProblemFingerprint, fact.ProblemFingerprint, StringComparison.Ordinal))
            {
                failures.Add($"排产方案 {fact.PlanId} 的问题号/指纹与事实流不一致。");
            }

            if (plan.AssignmentCount != fact.Assignments.Count ||
                plan.ResourceLoadCount != fact.ResourceLoads.Count ||
                plan.ConflictCount != fact.Conflicts.Count ||
                plan.UnscheduledOperationCount != fact.UnscheduledOperations.Count)
            {
                failures.Add(
                    $"排产方案 {fact.PlanId} 的明细条数（分配 {plan.AssignmentCount}/负荷 {plan.ResourceLoadCount}/" +
                    $"冲突 {plan.ConflictCount}/不可排 {plan.UnscheduledOperationCount}）与事实流" +
                    $"（{fact.Assignments.Count}/{fact.ResourceLoads.Count}/{fact.Conflicts.Count}/{fact.UnscheduledOperations.Count}）不一致。");
            }

            if (plan.Status != fact.Status)
            {
                failures.Add($"排产方案 {fact.PlanId} 状态 {plan.Status} 与期望 {fact.Status} 不一致。");
            }

            if (plan.ReleaseRevision != fact.ReleaseRevision)
            {
                failures.Add($"排产方案 {fact.PlanId} 的发布号 {plan.ReleaseRevision} 与期望 {fact.ReleaseRevision} 不一致。");
            }

            if (!string.Equals(plan.SupersededByPlanId, fact.SupersededByPlanId, StringComparison.Ordinal))
            {
                failures.Add($"排产方案 {fact.PlanId} 的后继方案 {plan.SupersededByPlanId} 与期望 {fact.SupersededByPlanId} 不一致。");
            }

            if (plan.GeneratedAtUtc < lowerBound || plan.GeneratedAtUtc > upperBound ||
                plan.ReleasedAtUtc > upperBound || plan.RevokedAtUtc > upperBound)
            {
                failures.Add($"排产方案 {fact.PlanId} 的时间戳越出历史窗口。");
            }

            if (plan.ReleasedAtUtc is { } releasedAtUtc && releasedAtUtc < plan.GeneratedAtUtc)
            {
                failures.Add($"排产方案 {fact.PlanId} 的发布时间早于生成时间，时间链非单调。");
            }

            if (plan.RevokedAtUtc is { } revokedAtUtc && plan.ReleasedAtUtc is { } released && revokedAtUtc < released)
            {
                failures.Add($"排产方案 {fact.PlanId} 的终结时间早于发布时间，时间链非单调。");
            }
        }
    }

    private static void CheckLifecycle(
        WorldHistorySchedulingFacts facts,
        IReadOnlyList<PersistedPlan> plans,
        List<string> failures)
    {
        var released = plans.Where(x => x.Status == SchedulePlanLifecycleStatus.Released).ToArray();
        if (facts.Plans.Count > 0 && released.Length != 1)
        {
            failures.Add($"同一 org/env 下应恰有 1 个已发布方案（ux_schedule_plans_scope_active_release），实际 {released.Length} 个。");
        }

        var revisions = plans.Where(x => x.ReleaseRevision.HasValue)
            .OrderBy(x => x.GeneratedAtUtc)
            .ThenBy(x => x.PlanId, StringComparer.Ordinal)
            .Select(x => x.ReleaseRevision!.Value)
            .ToArray();
        if (revisions.Distinct().Count() != revisions.Length)
        {
            failures.Add("发布号出现重复，违反 ux_schedule_plans_scope_release_revision。");
        }

        for (var index = 1; index < revisions.Length; index++)
        {
            if (revisions[index] <= revisions[index - 1])
            {
                failures.Add($"发布号沿时间轴非单调递增：{revisions[index - 1]} → {revisions[index]}。");
                break;
            }
        }

        if (plans.Any(x => x.Status == SchedulePlanLifecycleStatus.Generated && x.ReleaseRevision.HasValue))
        {
            failures.Add("待发布方案不应带发布号。");
        }

        if (plans.Any(x => x.Status == SchedulePlanLifecycleStatus.Superseded && string.IsNullOrWhiteSpace(x.SupersededByPlanId)))
        {
            failures.Add("被取代的方案缺少后继方案号，版本链断裂。");
        }
    }

    private static void CheckProblems(
        WorldHistorySchedulingFacts facts,
        IReadOnlyList<ScheduleProblemSnapshot> problems,
        string organizationId,
        string environmentId,
        DateTimeOffset lowerBound,
        DateTimeOffset upperBound,
        List<string> failures)
    {
        if (problems.Count != facts.Plans.Count)
        {
            failures.Add($"库内世界观问题快照 {problems.Count} 条，与方案 {facts.Plans.Count} 个不配对。");
        }

        var byProblemId = problems.ToDictionary(x => x.ProblemId, StringComparer.Ordinal);
        foreach (var fact in facts.Plans)
        {
            if (!byProblemId.TryGetValue(fact.ProblemId, out var snapshot))
            {
                // 缺问题快照 = 「锁定重预览」直接 SingleAsync 抛异常，必须 fail-closed。
                failures.Add($"问题快照 {fact.ProblemId} 缺失，方案 {fact.PlanId} 无法重预览。");
                continue;
            }

            if (snapshot.CapturedAtUtc < lowerBound || snapshot.CapturedAtUtc > upperBound)
            {
                failures.Add($"问题快照 {fact.ProblemId} 的采集时间越出历史窗口。");
            }

            SchedulingProblemContract? problem;
            try
            {
                problem = JsonSerializer.Deserialize<SchedulingProblemContract>(snapshot.ProblemJson, SchedulingJson.Options);
            }
            catch (JsonException exception)
            {
                failures.Add($"问题快照 {fact.ProblemId} 无法反序列化为排产问题契约：{exception.Message}");
                continue;
            }

            if (problem is null)
            {
                failures.Add($"问题快照 {fact.ProblemId} 反序列化为 null。");
                continue;
            }

            if (!string.Equals(problem.OrganizationId, organizationId, StringComparison.Ordinal) ||
                !string.Equals(problem.EnvironmentId, environmentId, StringComparison.Ordinal))
            {
                failures.Add($"问题快照 {fact.ProblemId} 缺少租户作用域，重预览会写出无主方案。");
            }

            if (problem.Orders.Count != fact.Orders.Count || problem.Resources.Count == 0)
            {
                failures.Add($"问题快照 {fact.ProblemId} 的订单/资源集合与事实流不一致。");
                continue;
            }

            var orderIds = problem.Orders.Select(x => x.OrderId).ToHashSet(StringComparer.Ordinal);
            var uncovered = fact.Assignments.Select(x => x.OrderId)
                .Distinct(StringComparer.Ordinal)
                .Where(x => !orderIds.Contains(x))
                .ToArray();
            if (uncovered.Length > 0)
            {
                failures.Add($"问题快照 {fact.ProblemId} 未覆盖方案里的工单：{string.Join("、", uncovered.Take(5))}。");
            }

            var operationIds = problem.Orders.SelectMany(x => x.Operations).Select(x => x.OperationId)
                .ToHashSet(StringComparer.Ordinal);
            if (fact.Assignments.Any(x => !operationIds.Contains(x.OperationId)))
            {
                failures.Add($"问题快照 {fact.ProblemId} 未覆盖方案里的工序，锁定重预览会判定「锁定工序不在本次修订内」。");
            }
        }
    }

    private static void CheckUrgencies(
        WorldHistorySchedulingFacts facts,
        IReadOnlyList<string> urgencyOrderIds,
        List<string> failures)
    {
        var persisted = urgencyOrderIds.ToHashSet(StringComparer.Ordinal);
        var missing = facts.Urgencies.Select(x => x.OrderId).Where(x => !persisted.Contains(x)).ToArray();
        if (missing.Length > 0)
        {
            failures.Add($"{missing.Length} 个工单缺少紧急度快照（如 {string.Join("、", missing.Take(5))}），列表页会退回 MissingContract 兜底。");
        }
    }

    private static void CheckIsolation(
        IReadOnlyList<PersistedPlan> plans,
        IReadOnlyList<ScheduleProblemSnapshot> problems,
        List<string> failures)
    {
        var references = plans.Select(x => x.PlanId)
            .Concat(plans.Select(x => x.ProblemId))
            .Concat(problems.Select(x => x.ProblemId));
        foreach (var reference in references)
        {
            foreach (var infix in WorldHistorySchedulingSpec.ReservedInfixes)
            {
                if (reference.Contains(infix, StringComparison.Ordinal))
                {
                    failures.Add($"世界观排产编号 {reference} 撞入保留号段 {infix}。");
                }
            }
        }
    }

    private static string StatusName(SchedulePlanLifecycleStatus status) => status switch
    {
        SchedulePlanLifecycleStatus.Generated => "待发布",
        SchedulePlanLifecycleStatus.Released => "已发布",
        SchedulePlanLifecycleStatus.Superseded => "已被取代",
        _ => "已撤销",
    };
}

/// <summary>排产域一致性校验通过后的对账摘要。</summary>
public sealed record WorldHistorySchedulingValidationReport(
    int PlansChecked,
    int ProblemsChecked,
    int AssignmentsChecked,
    int UrgencySnapshotsChecked,
    int GeneratedChecked,
    int ReleasedChecked,
    int SupersededChecked,
    int RevokedChecked,
    IReadOnlyList<string> Sample);

/// <summary>世界观一致性校验失败（fail-closed，中文累积原因）。</summary>
public sealed class WorldHistoryConsistencyException : InvalidOperationException
{
    private const string Prefix = "World-history scheduling seed validation failed";

    public WorldHistoryConsistencyException(IReadOnlyList<string> failures)
        : base(BuildMessage(failures))
    {
        Failures = failures;
    }

    public WorldHistoryConsistencyException()
        : base($"{Prefix}.")
    {
        Failures = [];
    }

    public WorldHistoryConsistencyException(string message)
        : base($"{Prefix}: {message}")
    {
        Failures = [message];
    }

    public WorldHistoryConsistencyException(string message, Exception innerException)
        : base($"{Prefix}: {message}", innerException)
    {
        Failures = [message];
    }

    public IReadOnlyList<string> Failures { get; }

    private static string BuildMessage(IReadOnlyList<string> failures) =>
        $"{Prefix}（{failures.Count} 条）：{Environment.NewLine}{string.Join(Environment.NewLine, failures.Take(20))}";
}
