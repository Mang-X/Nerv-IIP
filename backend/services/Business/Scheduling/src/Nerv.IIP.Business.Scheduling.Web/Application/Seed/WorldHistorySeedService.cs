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
/// 《工厂世界观设定集》L1 背景历史引擎的 **排产域侧**：
/// 问题快照 → 排产方案（含资源分配 / 资源负荷 / 冲突 / 不可排工序）→ 订单紧急度快照。
///
/// 领域事件说明：<see cref="SchedulePlan.FromGeneratedPlan"/> 每个方案发 1 个
/// <c>SchedulePlanGeneratedDomainEvent</c>、每条冲突再各发 1 个 <c>ScheduleConflictDetectedDomainEvent</c>，
/// N 方案 × M 冲突经 CAP 会直接把消息总线打爆。本引擎**绕开仓储与 UnitOfWork**，
/// 直接调用 <c>DbContext.SaveChangesAsync()</c>——本仓栈里该方法不派发领域事件
/// （派发只发生在 netcorepal 的 UnitOfWork/命令管线上），与 ERP/MES/Quality/DemandPlanning 引擎同一前提。
///
/// 时间回填：排产聚合的所有时间戳（<c>GeneratedAtUtc</c> / <c>ReleasedAtUtc</c> /
/// <c>RevokedAtUtc</c> / <c>StartUtc</c> / <c>EndUtc</c> / <c>CapturedAtUtc</c>）都由入参显式给定，
/// 没有一处写 <c>UtcNow</c>，因此不需要 EF Entry 级别的回填。
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

        var problemsWritten = await SeedProblemsAsync(organizationId, environmentId, facts.Plans, cancellationToken);
        var written = await SeedPlansAsync(organizationId, environmentId, facts.Plans, cancellationToken);
        var urgenciesWritten = await SeedUrgencySnapshotsAsync(organizationId, environmentId, facts.Urgencies, cancellationToken);

        // fail-closed：方案数量、生命周期分布、发布号单调、问题快照可反序列化对不上就让 seed 失败。
        var validation = await new WorldHistoryConsistencyValidator(dbContext)
            .ValidateAsync(organizationId, environmentId, asOfDate, scale, cancellationToken);

        return new WorldHistorySchedulingSeedReport(
            ScheduleProblemsWritten: problemsWritten,
            SchedulePlansWritten: written.Plans,
            AssignmentsWritten: written.Assignments,
            ResourceLoadsWritten: written.ResourceLoads,
            ConflictsWritten: written.Conflicts,
            UnscheduledOperationsWritten: written.UnscheduledOperations,
            OrderUrgencySnapshotsWritten: urgenciesWritten,
            Validation: validation);
    }

    #region 问题快照（自然键 (Org, Env, ProblemId)）

    private async Task<int> SeedProblemsAsync(
        string organizationId,
        string environmentId,
        IReadOnlyList<WorldHistorySchedulePlanFact> plans,
        CancellationToken cancellationToken)
    {
        var existing = await LoadExistingProblemIdsAsync(organizationId, environmentId, cancellationToken);
        var written = 0;
        foreach (var plan in plans.Where(plan => !existing.Contains(plan.ProblemId)))
        {
            var problem = WorldHistorySchedulingSpec.Scope(plan.Problem, organizationId, environmentId);
            dbContext.ScheduleProblems.Add(new ScheduleProblemSnapshot(
                plan.ProblemId,
                WorldHistorySchedulingSpec.ContractVersion,
                organizationId,
                environmentId,
                plan.ProblemFingerprint,
                JsonSerializer.Serialize(problem, SchedulingJson.Options),
                plan.HorizonStartUtc,
                plan.HorizonEndUtc,
                plan.CapturedAtUtc));
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

    #region 排产方案（自然键 PlanId；一方案一次 SaveChanges）

    private async Task<(int Plans, int Assignments, int ResourceLoads, int Conflicts, int UnscheduledOperations)> SeedPlansAsync(
        string organizationId,
        string environmentId,
        IReadOnlyList<WorldHistorySchedulePlanFact> facts,
        CancellationToken cancellationToken)
    {
        var existingIds = await dbContext.SchedulePlans.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.PlanId.StartsWith(WorldHistorySchedulingSpec.PlanNumberPrefix))
            .Select(x => x.PlanId)
            .ToArrayAsync(cancellationToken);
        var existing = existingIds.ToHashSet(StringComparer.Ordinal);

        var plans = 0;
        var assignments = 0;
        var resourceLoads = 0;
        var conflicts = 0;
        var unscheduled = 0;
        foreach (var fact in facts.Where(fact => !existing.Contains(fact.PlanId)))
        {
            var plan = SchedulePlan.FromGeneratedPlan(
                organizationId,
                environmentId,
                fact.ToGeneratedSnapshot(FiniteCapacityScheduler.AlgorithmVersion));

            // 顺序要紧：先发布再终结，领域动作拒绝「未发布即撤销」。
            if (fact.ReleaseRevision is { } revision && fact.ReleasedAtUtc is { } releasedAtUtc)
            {
                plan.Release(releasedAtUtc, revision);
                if (fact.Status == SchedulePlanLifecycleStatus.Superseded && fact.RevokedAtUtc is { } supersededAtUtc)
                {
                    plan.Supersede(fact.SupersededByPlanId!, supersededAtUtc);
                }
                else if (fact.Status == SchedulePlanLifecycleStatus.Revoked && fact.RevokedAtUtc is { } revokedAtUtc)
                {
                    plan.Revoke(revokedAtUtc);
                }
            }

            dbContext.SchedulePlans.Add(plan);
            plans++;
            assignments += fact.Assignments.Count;
            resourceLoads += fact.ResourceLoads.Count;
            conflicts += fact.Conflicts.Count;
            unscheduled += fact.UnscheduledOperations.Count;

            // 单个方案可带 200–500 条明细，逐方案落库并清跟踪器，避免变更跟踪器无限膨胀。
            await FlushAsync(cancellationToken, force: true);
        }

        return (plans, assignments, resourceLoads, conflicts, unscheduled);
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
    int SchedulePlansWritten,
    int AssignmentsWritten,
    int ResourceLoadsWritten,
    int ConflictsWritten,
    int UnscheduledOperationsWritten,
    int OrderUrgencySnapshotsWritten,
    WorldHistorySchedulingValidationReport Validation);
