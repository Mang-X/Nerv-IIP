using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalChainAggregate;
using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalDelegationAggregate;
using System.Text;

namespace Nerv.IIP.Business.Approval.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》§7 一致性校验器的 **审批域侧**。
///
/// 覆盖：事实流全量落库、状态与决策记录配对（终态必有决策时间与审批人）、待办数量与归属
/// （挂在 <c>user-admin</c> 名下）、全链时间戳单调且落在 <c>[上线日, asOfDate]</c> 工作日内、
/// 号段隔离（不与 <c>*-DEMO-*</c> / <c>*-SCALE-*</c> 相交）。
/// **fail-closed**：任何一条不成立即抛 <see cref="WorldHistoryApprovalConsistencyException"/>。
///
/// 跨服务的「审批链 ↔ 采购单 / NCR」配对不在这里做（审批域看不到 ERP / Quality 的库）：
/// 配对由 <see cref="WorldHistoryApprovalSpec"/> 的确定性与各侧黄金向量测试保证。
/// </summary>
public sealed class WorldHistoryApprovalConsistencyValidator(ApplicationDbContext dbContext)
{
    public const int SampleSize = 10;

    private static readonly string[] ReservedInfixes = ["-DEMO-", "-SCALE-"];

    public async Task<WorldHistoryApprovalValidationReport> ValidateAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        double scale,
        CancellationToken cancellationToken = default)
    {
        var facts = WorldHistoryApprovalSpec.BuildApprovalFacts(asOfDate, scale);
        var factByKey = facts.ToDictionary(fact => ChainKey(fact.TemplateCode, fact.DocumentId), StringComparer.Ordinal);
        var failures = new List<string>();

        var templateCodes = new[] { WorldHistoryApprovalSpec.PurchaseTemplateCode, WorldHistoryApprovalSpec.NcrTemplateCode };
        var chains = await dbContext.ApprovalChains
            .AsNoTracking()
            .Include(x => x.Steps)
            .Include(x => x.Decisions)
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                templateCodes.Contains(x.TemplateCode))
            .ToListAsync(cancellationToken);

        var lowerBound = new DateTimeOffset(WorldHistoryCalendar.GoLiveDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var upperBound = new DateTimeOffset(asOfDate.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);

        CheckPopulation(factByKey, chains, failures);

        foreach (var chain in chains)
        {
            if (!factByKey.TryGetValue(ChainKey(chain.TemplateCode, chain.DocumentReference.DocumentId), out var fact))
            {
                failures.Add($"库内审批链 {chain.TemplateCode}/{chain.DocumentReference.DocumentId} 不在本次计划内（号段被外部占用？）。");
                continue;
            }

            CheckChain(chain, fact, lowerBound, upperBound, failures);
        }

        CheckPendingAssignment(facts, chains, failures);
        CheckIsolation(chains, failures);

        var delegations = await dbContext.ApprovalDelegations
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .ToListAsync(cancellationToken);
        CheckDelegations(asOfDate, delegations, lowerBound, failures);

        if (failures.Count > 0)
        {
            throw new WorldHistoryApprovalConsistencyException(failures);
        }

        var pending = chains.Count(x => string.Equals(x.Status, ApprovalChainStatuses.Pending, StringComparison.Ordinal));
        var rejected = chains.Count(x => string.Equals(x.Status, ApprovalChainStatuses.Rejected, StringComparison.Ordinal));
        return new WorldHistoryApprovalValidationReport(
            ChainsChecked: chains.Count,
            PendingChainsChecked: pending,
            ApprovedChainsChecked: chains.Count - pending - rejected,
            RejectedChainsChecked: rejected,
            DelegationsChecked: delegations.Count,
            ActiveDelegationsChecked: delegations.Count(x =>
                string.Equals(x.Status, ApprovalDelegationStatuses.Active, StringComparison.Ordinal)),
            Sample: BuildSample(chains));
    }

    #region 校验项

    private static void CheckPopulation(
        Dictionary<string, WorldHistoryApprovalFact> factByKey,
        IReadOnlyList<ApprovalChain> chains,
        List<string> failures)
    {
        var present = chains
            .Select(x => ChainKey(x.TemplateCode, x.DocumentReference.DocumentId))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var missing in factByKey.Keys.Where(key => !present.Contains(key)).Take(5))
        {
            failures.Add($"计划中的审批链 {missing} 未落库。");
        }
    }

    private static void CheckChain(
        ApprovalChain chain,
        WorldHistoryApprovalFact fact,
        DateTimeOffset lowerBound,
        DateTimeOffset upperBound,
        List<string> failures)
    {
        var documentId = fact.DocumentId;
        var expectedStatus = fact.Outcome switch
        {
            WorldHistoryApprovalOutcome.Pending => ApprovalChainStatuses.Pending,
            WorldHistoryApprovalOutcome.Approved => ApprovalChainStatuses.Approved,
            _ => ApprovalChainStatuses.Rejected,
        };
        if (!string.Equals(chain.Status, expectedStatus, StringComparison.Ordinal))
        {
            failures.Add($"{documentId} 审批链状态应为 {expectedStatus}，实际 {chain.Status}。");
        }

        if (!string.Equals(chain.StartedBy, fact.StartedByActorRef, StringComparison.Ordinal))
        {
            failures.Add($"{documentId} 发起人应为 {fact.StartedByActorRef}，实际 {chain.StartedBy}。");
        }

        CheckMoment(chain.StartedAtUtc, $"{documentId} 发起时间", lowerBound, upperBound, failures);

        if (fact.Outcome == WorldHistoryApprovalOutcome.Pending)
        {
            if (chain.CompletedAtUtc is not null || chain.Decisions.Count > 0)
            {
                failures.Add($"{documentId} 待办审批链不应带有终态时间或决策记录。");
            }

            var pendingStep = chain.Steps.SingleOrDefault(x =>
                string.Equals(x.Status, ApprovalStepStatuses.Pending, StringComparison.Ordinal));
            if (pendingStep is null ||
                !string.Equals(pendingStep.ApproverType, WorldHistoryApprovalSpec.ActorTypeUser, StringComparison.Ordinal) ||
                !string.Equals(pendingStep.ApproverRef, fact.ApproverUserId, StringComparison.Ordinal))
            {
                failures.Add($"{documentId} 待办步骤未挂在 {WorldHistoryApprovalSpec.ActorTypeUser}:{fact.ApproverUserId} 名下。");
            }

            return;
        }

        // 终态链：必有决策时间与审批人（演示走查缺口——「已通过却没有审批人」= 假数据穿帮）。
        if (chain.CompletedAtUtc is not { } completedAtUtc)
        {
            failures.Add($"{documentId} 审批链已终态却没有完成时间。");
            return;
        }

        CheckMoment(completedAtUtc, $"{documentId} 完成时间", lowerBound, upperBound, failures);
        if (completedAtUtc < chain.StartedAtUtc)
        {
            failures.Add($"{documentId} 完成时间早于发起时间，时间链非单调。");
        }

        var expectedDecision = fact.Outcome == WorldHistoryApprovalOutcome.Approved
            ? ApprovalDecisions.Approve
            : ApprovalDecisions.Reject;
        var decision = chain.Decisions.SingleOrDefault(x =>
            string.Equals(x.Decision, expectedDecision, StringComparison.Ordinal));
        if (decision is null)
        {
            failures.Add($"{documentId} 终态审批链缺少 {expectedDecision} 决策记录。");
            return;
        }

        if (!string.Equals(decision.ActorType, WorldHistoryApprovalSpec.ActorTypeUser, StringComparison.Ordinal) ||
            !string.Equals(decision.ActorRef, fact.ApproverUserId, StringComparison.Ordinal))
        {
            failures.Add($"{documentId} 决策人应为 {WorldHistoryApprovalSpec.ActorTypeUser}:{fact.ApproverUserId}，实际 {decision.ActorType}:{decision.ActorRef}。");
        }

        if (decision.DecidedAtUtc != completedAtUtc)
        {
            failures.Add($"{documentId} 决策时间与链完成时间不一致。");
        }

        if (fact.Outcome == WorldHistoryApprovalOutcome.Rejected && string.IsNullOrWhiteSpace(decision.Comment))
        {
            failures.Add($"{documentId} 驳回决策缺少中文驳回原因（界面必填展示）。");
        }
    }

    private static void CheckMoment(
        DateTimeOffset moment,
        string label,
        DateTimeOffset lowerBound,
        DateTimeOffset upperBound,
        List<string> failures)
    {
        if (moment < lowerBound || moment > upperBound)
        {
            failures.Add($"{label} {moment:O} 不在 [{lowerBound:yyyy-MM-dd}, {upperBound:yyyy-MM-dd}] 历史窗内。");
        }

        if (!WorldHistoryCalendar.IsWorkingDay(DateOnly.FromDateTime(moment.UtcDateTime)))
        {
            failures.Add($"{label} {moment:O} 落在周日（停产保养日）。");
        }
    }

    private static void CheckPendingAssignment(
        IReadOnlyList<WorldHistoryApprovalFact> facts,
        IReadOnlyList<ApprovalChain> chains,
        List<string> failures)
    {
        var expectedAdminPending = facts.Count(fact =>
            fact.Outcome == WorldHistoryApprovalOutcome.Pending &&
            string.Equals(fact.ApproverUserId, WorldHistoryApprovalSpec.AdminUserId, StringComparison.Ordinal));
        var actualAdminPending = chains.Count(chain =>
            string.Equals(chain.Status, ApprovalChainStatuses.Pending, StringComparison.Ordinal) &&
            chain.Steps.Any(step =>
                string.Equals(step.Status, ApprovalStepStatuses.Pending, StringComparison.Ordinal) &&
                string.Equals(step.ApproverType, WorldHistoryApprovalSpec.ActorTypeUser, StringComparison.Ordinal) &&
                string.Equals(step.ApproverRef, WorldHistoryApprovalSpec.AdminUserId, StringComparison.Ordinal)));
        if (expectedAdminPending != actualAdminPending)
        {
            failures.Add($"挂在 {WorldHistoryApprovalSpec.AdminUserId} 名下的待办审批应为 {expectedAdminPending} 条，实际 {actualAdminPending} 条。");
        }
    }

    /// <summary>
    /// 审批委托：计划中的每条都必须落库且逐字段对上；状态与撤销痕迹配对；
    /// 委托人 / 受托人只能是世界观既有审批人；至少留一条 <c>active</c>（页面委托区块不能全是历史撤销件）。
    /// </summary>
    private static void CheckDelegations(
        DateOnly asOfDate,
        IReadOnlyList<ApprovalDelegation> delegations,
        DateTimeOffset lowerBound,
        List<string> failures)
    {
        var facts = WorldHistoryApprovalSpec.BuildDelegationFacts(asOfDate);
        var factByKey = facts.ToDictionary(fact => fact.NaturalKey, StringComparer.Ordinal);
        var byKey = delegations
            .GroupBy(
                x => WorldHistoryDelegationFact.NaturalKeyOf(x.DelegatorActorRef, x.DelegateActorRef, x.EffectiveFromUtc),
                StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        var knownActors = new[]
        {
            WorldHistoryApprovalSpec.AdminUserId,
            WorldHistoryApprovalSpec.QualitySupervisorUserId,
            WorldHistoryApprovalSpec.PlanningSupervisorUserId,
        }
            .Concat(WorldHistoryApprovalSpec.QualityEngineers.Select(x => x.UserId))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var (key, fact) in factByKey)
        {
            if (!byKey.TryGetValue(key, out var rows))
            {
                failures.Add($"计划中的审批委托 {key} 未落库。");
                continue;
            }

            if (rows.Length > 1)
            {
                failures.Add($"审批委托 {key} 出现 {rows.Length} 条重复（幂等预查失效？）。");
            }

            var row = rows[0];
            var expectedStatus = fact.IsRevoked ? ApprovalDelegationStatuses.Revoked : ApprovalDelegationStatuses.Active;
            if (!string.Equals(row.Status, expectedStatus, StringComparison.Ordinal))
            {
                failures.Add($"审批委托 {key} 状态应为 {expectedStatus}，实际 {row.Status}。");
            }

            if (row.EffectiveToUtc != fact.EffectiveToUtc)
            {
                failures.Add($"审批委托 {key} 失效时间与计划不符。");
            }

            if (!string.Equals(row.DocumentType, fact.DocumentType, StringComparison.Ordinal))
            {
                failures.Add($"审批委托 {key} 单据范围应为 '{fact.DocumentType ?? "(全部)"}'，实际 '{row.DocumentType ?? "(全部)"}'。");
            }

            if (row.EffectiveToUtc <= row.EffectiveFromUtc)
            {
                failures.Add($"审批委托 {key} 起止时间倒挂。");
            }

            if (row.CreatedAtUtc < lowerBound || row.CreatedAtUtc > row.EffectiveFromUtc)
            {
                failures.Add($"审批委托 {key} 创建时间 {row.CreatedAtUtc:O} 不在 [上线日, 生效起点] 内。");
            }

            if (string.IsNullOrWhiteSpace(row.Reason))
            {
                failures.Add($"审批委托 {key} 缺少中文委托事由（界面展示必填）。");
            }

            if (fact.IsRevoked)
            {
                if (row.RevokedAtUtc is not { } revokedAtUtc)
                {
                    failures.Add($"审批委托 {key} 已撤销却没有撤销时间。");
                }
                else if (revokedAtUtc < row.EffectiveFromUtc || revokedAtUtc > row.EffectiveToUtc)
                {
                    failures.Add($"审批委托 {key} 的撤销时间 {revokedAtUtc:O} 落在委托期之外。");
                }

                if (string.IsNullOrWhiteSpace(row.RevokedBy))
                {
                    failures.Add($"审批委托 {key} 已撤销却没有撤销人。");
                }
            }
            else if (row.RevokedAtUtc is not null || row.RevokedBy is not null)
            {
                failures.Add($"审批委托 {key} 仍生效却带有撤销痕迹。");
            }
        }

        // 只校验本引擎计划内的委托：演示当场创建的委托（L2）是租户事实，不该被 fail-closed 误杀。
        var seeded = factByKey.Keys
            .Where(byKey.ContainsKey)
            .Select(key => byKey[key][0])
            .ToArray();

        foreach (var row in seeded.Where(x =>
            !knownActors.Contains(x.DelegatorActorRef) || !knownActors.Contains(x.DelegateActorRef)))
        {
            failures.Add($"审批委托 {row.DelegatorActorRef}→{row.DelegateActorRef} 的委托人 / 受托人不是世界观既有审批人。");
        }

        if (facts.Count > 0 &&
            !seeded.Any(x => string.Equals(x.Status, ApprovalDelegationStatuses.Active, StringComparison.Ordinal)))
        {
            failures.Add("审批委托全部处于 revoked，委托区块缺少至少一条生效中的委托。");
        }
    }

    private static void CheckIsolation(IReadOnlyList<ApprovalChain> chains, List<string> failures)
    {
        foreach (var chain in chains)
        {
            var documentId = chain.DocumentReference.DocumentId;
            if (ReservedInfixes.Any(infix => documentId.Contains(infix, StringComparison.Ordinal)))
            {
                failures.Add($"{documentId} 与固定演示号段相交（*-DEMO-* / *-SCALE-* 必须隔离）。");
            }

            if (!documentId.StartsWith("PO-2026-", StringComparison.Ordinal) &&
                !documentId.StartsWith("NCR-2026-", StringComparison.Ordinal))
            {
                failures.Add($"{documentId} 不在世界观号段（PO-2026-* / NCR-2026-*）内。");
            }
        }
    }

    #endregion

    private static IReadOnlyList<string> BuildSample(IReadOnlyList<ApprovalChain> chains) =>
        [.. chains
            .OrderByDescending(x => x.StartedAtUtc)
            .Take(SampleSize)
            .Select(x => FormattableString.Invariant(
                $"{x.DocumentReference.DocumentId} [{x.Status}] {x.StartedBy} @ {x.StartedAtUtc:yyyy-MM-dd HH:mm}Z"))];

    private static string ChainKey(string templateCode, string documentId) => $"{templateCode}:{documentId}";
}

/// <summary>一次审批域历史校验的产出摘要。</summary>
public sealed record WorldHistoryApprovalValidationReport(
    int ChainsChecked,
    int PendingChainsChecked,
    int ApprovedChainsChecked,
    int RejectedChainsChecked,
    int DelegationsChecked,
    int ActiveDelegationsChecked,
    IReadOnlyList<string> Sample);

/// <summary>审批域 L1 历史一致性校验失败（fail-closed），失败明细全部中文累积后一次抛出。</summary>
public sealed class WorldHistoryApprovalConsistencyException : Exception
{
    public WorldHistoryApprovalConsistencyException(IReadOnlyList<string> failures)
        : base(BuildMessage(failures))
    {
        Failures = failures;
    }

    public WorldHistoryApprovalConsistencyException()
        : base("World-history consistency validation failed.")
    {
        Failures = [];
    }

    public WorldHistoryApprovalConsistencyException(string message)
        : base(message)
    {
        Failures = [message];
    }

    public WorldHistoryApprovalConsistencyException(string message, Exception innerException)
        : base(message, innerException)
    {
        Failures = [message];
    }

    public IReadOnlyList<string> Failures { get; }

    private static string BuildMessage(IReadOnlyList<string> failures)
    {
        ArgumentNullException.ThrowIfNull(failures);
        var builder = new StringBuilder("L1 背景历史一致性校验失败（审批域），共 ");
        builder.Append(failures.Count).AppendLine(" 条：");
        foreach (var failure in failures.Take(25))
        {
            builder.Append("  - ").AppendLine(failure);
        }

        if (failures.Count > 25)
        {
            builder.Append("  … 另有 ").Append(failures.Count - 25).AppendLine(" 条未列出。");
        }

        return builder.ToString();
    }
}
