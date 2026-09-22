using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.OrderUrgencyAggregate;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Domain.Services;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Business.Scheduling.Web.Application.Urgency;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》L1 背景历史引擎的 **排产域侧**：问题快照（排程引擎的输入）与订单紧急度快照。
///
/// **不写排产方案**：<c>schedule_plans</c> 及其四张明细在演示环境保持为空，直到用户在工作台点「生成」，
/// 由 <see cref="FiniteCapacityScheduler"/> 现场算出来（#3594）。种子回填的方案盖着
/// <c>aps-lite-v1</c> 的算法版本章却从未被该算法算过，排程行为一变就与演示数据脱节。
///
/// 时间回填：问题快照与紧急度快照的时间戳（<c>CapturedAtUtc</c> / <c>CalculatedAtUtc</c>）
/// 都由入参显式给定，没有一处写 <c>UtcNow</c>，因此不需要 EF Entry 级别的回填。
/// 本引擎**绕开仓储与 UnitOfWork**，直接调用 <c>DbContext.SaveChangesAsync()</c>——本仓栈里该方法
/// 不派发领域事件（派发只发生在 netcorepal 的 UnitOfWork/命令管线上），与其它域的历史引擎同一前提。
/// </summary>
public sealed class WorldHistorySeedService(ApplicationDbContext dbContext)
{
    /// <summary>行式批量的批大小。批末一次 <c>SaveChanges</c> 并清变更跟踪器。</summary>
    public const int BatchSize = 500;

    private int pendingWrites;

    public async Task<WorldHistorySchedulingSeedReport> SeedAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        double scale,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);

        var facts = WorldHistorySchedulingSpec.BuildSchedulingFacts(asOfDate, scale);

        var problemsWritten = await SeedProblemsAsync(organizationId, environmentId, facts.Problems, cancellationToken);
        var urgenciesWritten = await SeedUrgencySnapshotsAsync(organizationId, environmentId, facts.Urgencies, cancellationToken);

        // fail-closed：问题快照反序列化不回来、或紧急度快照缺工单就让 seed 失败。
        var validation = await new WorldHistoryConsistencyValidator(dbContext)
            .ValidateAsync(organizationId, environmentId, asOfDate, scale, cancellationToken);

        return new WorldHistorySchedulingSeedReport(
            ScheduleProblemsWritten: problemsWritten,
            OrderUrgencySnapshotsWritten: urgenciesWritten,
            Validation: validation);
    }

    #region 问题快照（自然键 (Org, Env, ProblemId)）

    private async Task<int> SeedProblemsAsync(
        string organizationId,
        string environmentId,
        IReadOnlyList<WorldHistoryScheduleProblemFact> problems,
        CancellationToken cancellationToken)
    {
        var existing = await LoadExistingProblemIdsAsync(organizationId, environmentId, cancellationToken);
        var written = 0;
        foreach (var fact in problems.Where(fact => !existing.Contains(fact.ProblemId)))
        {
            var problem = WorldHistorySchedulingSpec.Scope(fact.Problem, organizationId, environmentId);
            dbContext.ScheduleProblems.Add(new ScheduleProblemSnapshot(
                fact.ProblemId,
                WorldHistorySchedulingSpec.ContractVersion,
                organizationId,
                environmentId,
                fact.ProblemFingerprint,
                JsonSerializer.Serialize(problem, SchedulingJson.Options),
                fact.HorizonStartUtc,
                fact.HorizonEndUtc,
                fact.CapturedAtUtc));
            written++;
            await FlushAsync(cancellationToken);
        }

        await FlushAsync(cancellationToken, force: true);
        return written;
    }

    private async Task<HashSet<string>> LoadExistingProblemIdsAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken)
    {
        var ids = await dbContext.ScheduleProblems.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.ProblemId.StartsWith(WorldHistorySchedulingSpec.ProblemNumberPrefix))
            .Select(x => x.ProblemId)
            .ToArrayAsync(cancellationToken);
        return ids.ToHashSet(StringComparer.Ordinal);
    }

    #endregion

    #region 订单紧急度快照（自然键 (Org, Env, OrderId, ModelVersion, InputFingerprint, Revision, Bucket)）

    private async Task<int> SeedUrgencySnapshotsAsync(
        string organizationId,
        string environmentId,
        IReadOnlyList<WorldHistoryUrgencyFact> facts,
        CancellationToken cancellationToken)
    {
        var existingKeys = await dbContext.OrderUrgencySnapshots.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .Select(x => new { x.OrderId, x.InputFingerprint })
            .ToArrayAsync(cancellationToken);
        var existing = existingKeys
            .Select(x => $"{x.OrderId}{x.InputFingerprint}")
            .ToHashSet(StringComparer.Ordinal);

        var written = 0;
        foreach (var fact in facts.Where(fact =>
                     !existing.Contains($"{fact.OrderId}{fact.InputFingerprint}")))
        {
            var result = OrderUrgencyCalculator.Calculate(WorldHistorySchedulingSpec.ToCalculationInput(fact));
            dbContext.OrderUrgencySnapshots.Add(new OrderUrgencySnapshot(
                organizationId,
                environmentId,
                result.OrderId,
                result.BusinessReference,
                result.Level,
                result.ModelVersion,
                result.InputFingerprint,
                result.BusinessPriority.Revision,
                fact.CalculationBucketUtc,
                result.CalculatedAtUtc,
                OrderUrgencyContractMapper.Serialize(result)));
            written++;
            await FlushAsync(cancellationToken);
        }

        await FlushAsync(cancellationToken, force: true);
        return written;
    }

    #endregion

    private async Task FlushAsync(CancellationToken cancellationToken, bool force = false)
    {
        pendingWrites++;
        if (!force && pendingWrites < BatchSize)
        {
            return;
        }

        if (dbContext.ChangeTracker.HasChanges())
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        dbContext.ChangeTracker.Clear();
        pendingWrites = 0;
    }
}

/// <summary>一次 L1 排产域历史生成的产出摘要。</summary>
public sealed record WorldHistorySchedulingSeedReport(
    int ScheduleProblemsWritten,
    int OrderUrgencySnapshotsWritten,
    WorldHistorySchedulingValidationReport Validation);
