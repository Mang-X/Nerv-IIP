using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringChangeAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringDocumentAggregate;
using Nerv.IIP.Business.ProductEngineering.Infrastructure;
using System.Linq.Expressions;

namespace Nerv.IIP.Business.ProductEngineering.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》L1 背景历史引擎的 **工程域侧**：把
/// <see cref="WorldHistoryEngineeringSpec"/> 的确定性事实流落成
/// <c>ECO-2026-####</c> 工程变更（含受影响版本）与 <c>DOC-2026-####</c> 工程文档。
///
/// 与 L0（<see cref="WorldBibleSeedService"/>，门控 <c>LeaderDemo:World:Enabled</c>）分开：
/// L0 铺产品/BOM/工艺主数据，本引擎（门控 <c>LeaderDemo:History:Enabled</c>）铺「上线以来的变更与文档痕迹」。
/// 受影响版本引用的 <c>EBOM-*/MBOM-*/ROUTING-*</c> 编码全部来自 L0 的同一份字面量，
/// 因此只要 L0 已铺开，引用即真实存在——不跨聚合查询，也不建外键。
///
/// 幂等：变更按 <c>ChangeNumber</c>、文档按 <c>(DocumentNumber, Revision)</c> 预查；
/// 已存在的一律不动（保留租户事实），重复执行行数不变。
///
/// 时间戳：聚合根构造时用 <c>DateTime.UtcNow</c>，因此每条都要 <see cref="Backdate"/> 回填到历史窗口内，
/// 否则页面上会出现「今天创建的 1 月变更单」。
///
/// 领域事件说明：本仓栈里 <c>DbContext.SaveChangesAsync()</c> 不派发领域事件（派发只发生在
/// netcorepal 的 UnitOfWork/命令管线上），因此这里可以放心调用会 <c>AddDomainEvent</c> 的聚合方法，
/// 历史数据不会反向触发 CAP 集成事件风暴——与 ERP/MES/Quality 侧同一前提。
/// </summary>
public sealed class WorldHistorySeedService(ApplicationDbContext dbContext)
{
    /// <summary>每批写入条数。批内共享一次预查与一次 <c>SaveChanges</c>，批末清变更跟踪器。</summary>
    public const int BatchSize = 200;

    public async Task<WorldHistoryEngineeringSeedReport> SeedAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        double scale,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);

        var changesWritten = await SeedEngineeringChangesAsync(organizationId, environmentId, asOfDate, scale, cancellationToken);
        var documentsWritten = await SeedEngineeringDocumentsAsync(organizationId, environmentId, asOfDate, scale, cancellationToken);

        // fail-closed：号段、状态分布、受影响版本、时间窗口对不上就让 seed 失败。
        var validation = await new WorldHistoryConsistencyValidator(dbContext)
            .ValidateAsync(organizationId, environmentId, asOfDate, scale, cancellationToken);

        return new WorldHistoryEngineeringSeedReport(
            EngineeringChangesWritten: changesWritten.Changes,
            AffectedVersionsWritten: changesWritten.AffectedVersions,
            EngineeringDocumentsWritten: documentsWritten,
            Validation: validation);
    }

    #region 工程变更

    private async Task<(int Changes, int AffectedVersions)> SeedEngineeringChangesAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        double scale,
        CancellationToken cancellationToken)
    {
        var facts = WorldHistoryEngineeringSpec.BuildChangeFacts(asOfDate, scale);
        var changes = 0;
        var affectedVersions = 0;

        for (var batchStart = 0; batchStart < facts.Count; batchStart += BatchSize)
        {
            var batch = facts.Skip(batchStart).Take(BatchSize).ToArray();
            var changeNumbers = batch.Select(fact => fact.ChangeNumber).ToArray();
            var existing = (await dbContext.EngineeringChanges
                    .AsNoTracking()
                    .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                        changeNumbers.Contains(x.ChangeNumber))
                    .Select(x => x.ChangeNumber)
                    .ToArrayAsync(cancellationToken))
                .ToHashSet(StringComparer.Ordinal);

            var added = 0;
            foreach (var fact in batch.Where(fact => !existing.Contains(fact.ChangeNumber)))
            {
                WriteEngineeringChange(organizationId, environmentId, fact);
                changes++;
                affectedVersions += fact.AffectedVersions.Count;
                added++;
            }

            if (added > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            dbContext.ChangeTracker.Clear();
        }

        return (changes, affectedVersions);
    }

    private void WriteEngineeringChange(
        string organizationId,
        string environmentId,
        WorldHistoryEngineeringChangeFact fact)
    {
        var change = EngineeringChange.Open(organizationId, environmentId, fact.ChangeNumber, fact.Reason);
        foreach (var affectedVersion in fact.AffectedVersions)
        {
            change.Affect(affectedVersion.VersionKind, affectedVersion.VersionId, affectedVersion.SupersededByVersionId);
        }

        if (fact.ApprovalReferenceId is { } approvalReferenceId)
        {
            change.Approve(approvalReferenceId);
        }

        switch (fact.State)
        {
            case WorldHistoryEngineeringChangeState.Draft:
                // 草稿：受影响版本已挂好，审批还没走完——工程变更页的「待处理」那一档。
                break;

            case WorldHistoryEngineeringChangeState.Scheduled:
                change.Schedule(fact.EffectiveDate!.Value);
                break;

            case WorldHistoryEngineeringChangeState.Published:
                change.Release(fact.EffectiveDate!.Value);
                break;

            case WorldHistoryEngineeringChangeState.Cancelled:
                // 取消必须先排期——这也是真实工厂里「排了期又撤回」的那条路径。
                change.Schedule(fact.EffectiveDate!.Value);
                change.CancelScheduled();
                break;

            default:
                throw new WorldHistoryConsistencyException($"{fact.ChangeNumber} 出现未预期的历史状态。");
        }

        dbContext.EngineeringChanges.Add(change);
        Backdate(change, x => x.CreatedAtUtc, fact.OpenedAtUtc.UtcDateTime);
        Backdate(change, x => x.UpdatedAtUtc, fact.DecidedAtUtc.UtcDateTime);
    }

    #endregion

    #region 工程文档

    private async Task<int> SeedEngineeringDocumentsAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        double scale,
        CancellationToken cancellationToken)
    {
        var facts = WorldHistoryEngineeringSpec.BuildDocumentFacts(asOfDate, scale);
        var written = 0;

        for (var batchStart = 0; batchStart < facts.Count; batchStart += BatchSize)
        {
            var batch = facts.Skip(batchStart).Take(BatchSize).ToArray();
            var documentNumbers = batch.Select(fact => fact.DocumentNumber).ToArray();
            var existing = (await dbContext.EngineeringDocuments
                    .AsNoTracking()
                    .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                        documentNumbers.Contains(x.DocumentNumber))
                    .Select(x => new { x.DocumentNumber, x.Revision })
                    .ToArrayAsync(cancellationToken))
                .Select(x => DocumentKey(x.DocumentNumber, x.Revision))
                .ToHashSet(StringComparer.Ordinal);

            var added = 0;
            foreach (var fact in batch.Where(fact => !existing.Contains(DocumentKey(fact.DocumentNumber, fact.Revision))))
            {
                WriteEngineeringDocument(organizationId, environmentId, fact);
                written++;
                added++;
            }

            if (added > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            dbContext.ChangeTracker.Clear();
        }

        return written;
    }

    private void WriteEngineeringDocument(
        string organizationId,
        string environmentId,
        WorldHistoryEngineeringDocumentFact fact)
    {
        var document = fact.IsSop
            ? EngineeringDocument.PublishSop(
                organizationId,
                environmentId,
                fact.DocumentNumber,
                fact.Revision,
                fact.OperationCode!,
                fact.WorkCenterCode,
                fact.RoutingCode,
                fact.RoutingRevision,
                fact.EffectiveDate!.Value,
                fact.FileId,
                fact.FileName,
                fact.ContentType)
            : EngineeringDocument.Register(
                organizationId,
                environmentId,
                fact.DocumentNumber,
                fact.Revision,
                fact.ItemCode,
                fact.FileId,
                fact.FileName,
                fact.ContentType,
                fact.DocumentType);

        if (fact.IsArchived)
        {
            document.Archive(fact.ArchiveReason!);
        }

        dbContext.EngineeringDocuments.Add(document);
        Backdate(document, x => x.RegisteredAtUtc, fact.RegisteredAtUtc.UtcDateTime);
    }

    /// <summary>文档自然键：编号 + 分隔符 + 修订（与 <c>(organization, environment, document_number, revision)</c> 唯一索引同口径）。</summary>
    internal static string DocumentKey(string documentNumber, string revision) => $"{documentNumber}\u001F{revision}";

    #endregion

    private void Backdate<TEntity, TProperty>(
        TEntity entity,
        Expression<Func<TEntity, TProperty>> property,
        TProperty value)
        where TEntity : class
    {
        dbContext.Entry(entity).Property(property).CurrentValue = value;
    }
}

/// <summary>一次 L1 工程域历史生成的产出摘要。</summary>
public sealed record WorldHistoryEngineeringSeedReport(
    int EngineeringChangesWritten,
    int AffectedVersionsWritten,
    int EngineeringDocumentsWritten,
    WorldHistoryEngineeringValidationReport Validation);
