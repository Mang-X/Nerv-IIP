using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalChainAggregate;
using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalDelegationAggregate;
using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalTemplateAggregate;

namespace Nerv.IIP.Business.Approval.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》L1 背景历史引擎的 **审批域侧**。
///
/// 产出（设定集 §7 派生）：两张世界观审批模板 + 逐张覆盖 <c>PO-2026-####</c> 的采购订单审批链、
/// 引用 <c>NCR-2026-####</c> 号段的 NCR 处置审批链。绝大多数已通过、约 5% 被驳回，
/// 最新几张采购单留作待办挂在厂长（<c>user-admin</c>）名下——工作台待办卡据此有数。
///
/// 与源域的一致性靠 <see cref="WorldHistoryApprovalSpec.BuildApprovalFacts"/> 一个确定性纯函数达成：
/// 审批链引用的源单据号全部来自 ERP / Quality 已落库的号段，两侧不通信、不跨库查询、不建跨 schema 外键。
///
/// 领域事件说明：本仓栈里 <c>DbContext.SaveChangesAsync()</c> 不派发领域事件（派发只发生在
/// netcorepal 的 UnitOfWork/命令管线上），因此这里可以放心调用会 <c>AddDomainEvent</c> 的聚合方法，
/// 历史数据不会反向触发 CAP 集成事件风暴——与一期 ERP/MES、二期 Quality seed 同一前提。
/// </summary>
public sealed class WorldHistoryApprovalSeedService(ApplicationDbContext dbContext)
{
    /// <summary>每批审批链数。批内共享一次预查与一次 <c>SaveChanges</c>，批末清变更跟踪器。</summary>
    public const int BatchSize = 200;

    /// <summary>模板步骤时限（小时）：24 小时未审即逾期，与待办卡的到期时间展示对齐。</summary>
    public const int StepDueInHours = 24;

    public async Task<WorldHistoryApprovalSeedReport> SeedAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        double scale,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);

        var templatesWritten = await SeedTemplatesAsync(organizationId, environmentId, cancellationToken);
        var templates = await LoadTemplatesAsync(organizationId, environmentId, cancellationToken);

        var facts = WorldHistoryApprovalSpec.BuildApprovalFacts(asOfDate, scale);
        var counters = new SeedCounters();

        for (var batchStart = 0; batchStart < facts.Count; batchStart += BatchSize)
        {
            var batch = facts.Skip(batchStart).Take(BatchSize).ToArray();
            var existing = await LoadExistingDocumentKeysAsync(
                organizationId,
                environmentId,
                batch.Select(fact => fact.DocumentId).ToArray(),
                cancellationToken);

            var added = 0;
            foreach (var fact in batch.Where(fact => !existing.Contains(DocumentKey(fact.TemplateCode, fact.DocumentId))))
            {
                WriteApprovalChain(fact, templates[fact.TemplateCode], counters);
                added++;
            }

            if (added > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            dbContext.ChangeTracker.Clear();
        }

        var delegationsWritten = await SeedDelegationsAsync(organizationId, environmentId, asOfDate, cancellationToken);

        // fail-closed：待办数量 / 终态完整性 / 时间窗与号段隔离对不上就让 seed 失败。
        var validation = await new WorldHistoryApprovalConsistencyValidator(dbContext)
            .ValidateAsync(organizationId, environmentId, asOfDate, scale, cancellationToken);

        return new WorldHistoryApprovalSeedReport(
            TemplatesWritten: templatesWritten,
            ChainsWritten: counters.Chains,
            DelegationsWritten: delegationsWritten,
            PurchaseChainsWritten: counters.PurchaseChains,
            NcrChainsWritten: counters.NcrChains,
            PendingChainsWritten: counters.PendingChains,
            RejectedChainsWritten: counters.RejectedChains,
            Validation: validation);
    }

    #region 审批模板

    /// <summary>按 <c>TemplateCode</c> 幂等补齐两张世界观审批模板；已存在的一律不动（保留租户事实）。</summary>
    private async Task<int> SeedTemplatesAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken)
    {
        var templateCodes = new[] { WorldHistoryApprovalSpec.PurchaseTemplateCode, WorldHistoryApprovalSpec.NcrTemplateCode };
        var existing = (await dbContext.ApprovalTemplates
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                    templateCodes.Contains(x.TemplateCode))
                .Select(x => x.TemplateCode)
                .ToArrayAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        // 模板本身没有业务时间戳语义，统一回填到上线日，避免历史页面里出现「今天创建的历史模板」。
        var goLiveUtc = new DateTimeOffset(WorldHistoryCalendar.GoLiveDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var written = 0;

        if (!existing.Contains(WorldHistoryApprovalSpec.PurchaseTemplateCode))
        {
            var template = ApprovalTemplate.Create(
                organizationId,
                environmentId,
                WorldHistoryApprovalSpec.PurchaseTemplateCode,
                WorldHistoryApprovalSpec.PurchaseDocumentType,
                version: 1,
                isActive: true,
                [
                    new ApprovalTemplateStepDefinition(
                        StepNo: 1,
                        StepName: "总经理审批",
                        ParallelGroupKey: null,
                        ApproverType: WorldHistoryApprovalSpec.ActorTypeUser,
                        ApproverRef: WorldHistoryApprovalSpec.AdminUserId,
                        DueInHours: StepDueInHours),
                ]);
            dbContext.ApprovalTemplates.Add(template);
            Backdate(template, x => x.CreatedAtUtc, goLiveUtc);
            Backdate(template, x => x.UpdatedAtUtc, goLiveUtc);
            written++;
        }

        if (!existing.Contains(WorldHistoryApprovalSpec.NcrTemplateCode))
        {
            var template = ApprovalTemplate.Create(
                organizationId,
                environmentId,
                WorldHistoryApprovalSpec.NcrTemplateCode,
                WorldHistoryApprovalSpec.NcrDocumentType,
                version: 1,
                isActive: true,
                [
                    new ApprovalTemplateStepDefinition(
                        StepNo: 1,
                        StepName: "质量主管评审",
                        ParallelGroupKey: null,
                        ApproverType: WorldHistoryApprovalSpec.ActorTypeUser,
                        ApproverRef: WorldHistoryApprovalSpec.QualitySupervisorUserId,
                        DueInHours: StepDueInHours),
                ]);
            dbContext.ApprovalTemplates.Add(template);
            Backdate(template, x => x.CreatedAtUtc, goLiveUtc);
            Backdate(template, x => x.UpdatedAtUtc, goLiveUtc);
            written++;
        }

        if (written > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        dbContext.ChangeTracker.Clear();
        return written;
    }

    private async Task<Dictionary<string, ApprovalTemplate>> LoadTemplatesAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken)
    {
        var templateCodes = new[] { WorldHistoryApprovalSpec.PurchaseTemplateCode, WorldHistoryApprovalSpec.NcrTemplateCode };
        var templates = await dbContext.ApprovalTemplates
            .AsNoTracking()
            .Include(x => x.Steps)
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                templateCodes.Contains(x.TemplateCode))
            .ToArrayAsync(cancellationToken);

        var missing = templateCodes.Except(templates.Select(x => x.TemplateCode), StringComparer.Ordinal).ToArray();
        if (missing.Length > 0)
        {
            throw new WorldHistoryApprovalConsistencyException(
                $"世界观审批模板缺失：{string.Join(", ", missing)}——历史审批链无处可挂。");
        }

        return templates.ToDictionary(x => x.TemplateCode, StringComparer.Ordinal);
    }

    #endregion

    #region 审批委托

    /// <summary>
    /// 按自然键（委托人 → 受托人 + 生效起点）幂等补齐历史审批委托。
    ///
    /// 委托是低频事件（全量历史十几条），一次预查一次写入即可，无需分批。
    /// <see cref="ApprovalDelegation"/> 不发领域事件，也没有跨服务消费者，因此不必 <c>ClearDomainEvents</c>。
    /// </summary>
    private async Task<int> SeedDelegationsAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        CancellationToken cancellationToken)
    {
        var facts = WorldHistoryApprovalSpec.BuildDelegationFacts(asOfDate);
        var existing = (await dbContext.ApprovalDelegations
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
                .Select(x => new { x.DelegatorActorRef, x.DelegateActorRef, x.EffectiveFromUtc })
                .ToArrayAsync(cancellationToken))
            .Select(x => WorldHistoryDelegationFact.NaturalKeyOf(x.DelegatorActorRef, x.DelegateActorRef, x.EffectiveFromUtc))
            .ToHashSet(StringComparer.Ordinal);

        var written = 0;
        foreach (var fact in facts.Where(fact => !existing.Contains(fact.NaturalKey)))
        {
            var delegation = ApprovalDelegation.Create(
                organizationId,
                environmentId,
                WorldHistoryApprovalSpec.ActorTypeUser,
                fact.DelegatorActorRef,
                WorldHistoryApprovalSpec.ActorTypeUser,
                fact.DelegateActorRef,
                fact.DocumentType,
                fact.EffectiveFromUtc,
                fact.EffectiveToUtc,
                fact.Reason,
                fact.CreatedBy);
            if (fact.IsRevoked)
            {
                delegation.Revoke(fact.CreatedBy);
            }

            dbContext.ApprovalDelegations.Add(delegation);

            // 领域方法一律取 UtcNow；创建 / 撤销时刻靠 Entry().Property() 回填到历史窗内。
            Backdate(delegation, x => x.CreatedAtUtc, fact.CreatedAtUtc);
            if (fact.RevokedAtUtc is { } revokedAtUtc)
            {
                Backdate(delegation, x => x.RevokedAtUtc, (DateTimeOffset?)revokedAtUtc);
            }

            written++;
        }

        if (written > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        dbContext.ChangeTracker.Clear();
        return written;
    }

    #endregion

    private static string DocumentKey(string templateCode, string documentId) => $"{templateCode}:{documentId}";

    private async Task<HashSet<string>> LoadExistingDocumentKeysAsync(
        string organizationId,
        string environmentId,
        string[] documentIds,
        CancellationToken cancellationToken) =>
        (await dbContext.ApprovalChains
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                documentIds.Contains(x.DocumentReference.DocumentId))
            .Select(x => new { x.TemplateCode, x.DocumentReference.DocumentId })
            .ToArrayAsync(cancellationToken))
        .Select(x => DocumentKey(x.TemplateCode, x.DocumentId))
        .ToHashSet(StringComparer.Ordinal);

    /// <summary>写一条历史审批链：发起 →（审批通过 / 驳回，或留作待办）→ 回填时间戳。</summary>
    private void WriteApprovalChain(WorldHistoryApprovalFact fact, ApprovalTemplate template, SeedCounters counters)
    {
        var documentReference = new ApprovalDocumentReference(
            fact.SourceService,
            fact.DocumentType,
            fact.DocumentId,
            documentLineId: null,
            amount: fact.Amount);
        var chain = ApprovalChain.Start(template, documentReference, fact.StartedByActorRef);

        if (fact.IsCompleted)
        {
            chain.ResolveStep(
                stepNo: 1,
                actorType: WorldHistoryApprovalSpec.ActorTypeUser,
                actorRef: fact.ApproverUserId,
                decision: fact.Outcome == WorldHistoryApprovalOutcome.Approved
                    ? ApprovalDecisions.Approve
                    : ApprovalDecisions.Reject,
                comment: fact.DecisionComment);
        }

        dbContext.ApprovalChains.Add(chain);

        // 领域方法一律取 UtcNow；历史时间靠 Entry().Property() 逐字段回填（与 Quality 侧同一手法）。
        Backdate(chain, x => x.StartedAtUtc, fact.StartedAtUtc);
        Backdate(chain, x => x.CompletedAtUtc, fact.DecidedAtUtc);
        foreach (var step in chain.Steps)
        {
            Backdate(step, x => x.DueAtUtc, (DateTimeOffset?)fact.StartedAtUtc.AddHours(StepDueInHours));
            if (fact.IsCompleted)
            {
                Backdate(step, x => x.ResolvedAtUtc, fact.DecidedAtUtc);
            }
        }

        foreach (var decision in chain.Decisions)
        {
            Backdate(decision, x => x.DecidedAtUtc, fact.DecidedAtUtc!.Value);
        }

        counters.Chains++;
        if (string.Equals(fact.TemplateCode, WorldHistoryApprovalSpec.PurchaseTemplateCode, StringComparison.Ordinal))
        {
            counters.PurchaseChains++;
        }
        else
        {
            counters.NcrChains++;
        }

        if (fact.Outcome == WorldHistoryApprovalOutcome.Pending)
        {
            counters.PendingChains++;
        }
        else if (fact.Outcome == WorldHistoryApprovalOutcome.Rejected)
        {
            counters.RejectedChains++;
        }
    }

    private void Backdate<TEntity, TProperty>(
        TEntity entity,
        System.Linq.Expressions.Expression<Func<TEntity, TProperty>> property,
        TProperty value)
        where TEntity : class
    {
        dbContext.Entry(entity).Property(property).CurrentValue = value;
    }

    private sealed class SeedCounters
    {
        public int Chains { get; set; }
        public int PurchaseChains { get; set; }
        public int NcrChains { get; set; }
        public int PendingChains { get; set; }
        public int RejectedChains { get; set; }
    }
}

/// <summary>一次 L1 审批域历史生成的产出摘要。</summary>
public sealed record WorldHistoryApprovalSeedReport(
    int TemplatesWritten,
    int ChainsWritten,
    int DelegationsWritten,
    int PurchaseChainsWritten,
    int NcrChainsWritten,
    int PendingChainsWritten,
    int RejectedChainsWritten,
    WorldHistoryApprovalValidationReport Validation);
