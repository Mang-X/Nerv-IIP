using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》§7 一致性校验器的 **排产域侧**。
///
/// 覆盖：问题快照与规格逐条配对且能被 <c>CreateSchedulePlanRevisionCommandHandler</c> 用同一套
/// <see cref="SchedulingJson.Options"/> 反序列化回 <see cref="SchedulingProblemContract"/>、
/// 订单紧急度快照覆盖问题里出现过的每个工单、全部时间戳落在 [上线日, asOfDate] 窗口内。
/// **不覆盖**号段隔离：本引擎的问题号由 <c>WorldHistorySchedulingSpec.ProblemId</c> 拼成
/// <c>SPB-2026-{index:D4}</c>，结构上撞不进保留段（<c>*-DEMO-*</c> / <c>*-SCALE-*</c>），
/// 运行时校验它等于给不可达状态兜底。号段格式本身由
/// <c>WorldHistorySchedulingSeedServiceTests</c> 在测试库上断言。
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

        var problems = await dbContext.ScheduleProblems.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.ProblemId.StartsWith(WorldHistorySchedulingSpec.ProblemNumberPrefix))
            .ToListAsync(cancellationToken);

        var urgencyOrderIds = await dbContext.OrderUrgencySnapshots.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .Select(x => x.OrderId)
            .ToArrayAsync(cancellationToken);

        CheckProblems(facts, problems, organizationId, environmentId, lowerBound, upperBound, failures);
        CheckUrgencies(facts, urgencyOrderIds, failures);

        if (failures.Count > 0)
        {
            throw new WorldHistoryConsistencyException(failures);
        }

        var sample = facts.Problems
            .TakeLast(SampleSize)
            .Select(fact => string.Create(
                CultureInfo.InvariantCulture,
                $"{fact.ProblemId} {fact.WeekStart:yyyy-MM-dd} 起两周窗口：{fact.Orders.Count} 单 / " +
                $"{fact.Problem.Orders.Sum(x => x.Operations.Count)} 工序 / {fact.Problem.Resources.Count} 资源 / " +
                $"{fact.Problem.UnavailabilityWindows.Count} 不可用窗口"))
            .ToArray();

        return new WorldHistorySchedulingValidationReport(
            ProblemsChecked: problems.Count,
            OperationsChecked: facts.OperationCount,
            UrgencySnapshotsChecked: urgencyOrderIds.Length,
            Sample: sample);
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
        if (problems.Count != facts.Problems.Count)
        {
            failures.Add($"库内世界观问题快照 {problems.Count} 条，与规格 {facts.Problems.Count} 个不配对。");
        }

        var byProblemId = problems.ToDictionary(x => x.ProblemId, StringComparer.Ordinal);
        foreach (var fact in facts.Problems)
        {
            if (!byProblemId.TryGetValue(fact.ProblemId, out var snapshot))
            {
                failures.Add($"问题快照 {fact.ProblemId} 缺失，基于它创建排产方案会失败。");
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
                failures.Add($"问题快照 {fact.ProblemId} 缺少租户作用域，基于它创建方案会写出无主数据。");
            }

            if (problem.Orders.Count != fact.Orders.Count || problem.Resources.Count == 0)
            {
                failures.Add($"问题快照 {fact.ProblemId} 的订单/资源集合与规格不一致。");
                continue;
            }

            var operationCount = problem.Orders.Sum(x => x.Operations.Count);
            if (operationCount != fact.Problem.Orders.Sum(x => x.Operations.Count))
            {
                failures.Add($"问题快照 {fact.ProblemId} 的工序数与规格不一致（库内 {operationCount} 道）。");
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

}

/// <summary>排产域一致性校验通过后的对账摘要。</summary>
public sealed record WorldHistorySchedulingValidationReport(
    int ProblemsChecked,
    int OperationsChecked,
    int UrgencySnapshotsChecked,
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
