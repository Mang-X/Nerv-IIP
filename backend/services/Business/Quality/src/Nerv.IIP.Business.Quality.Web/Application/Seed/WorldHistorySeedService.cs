using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionPlanAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionTaskAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using System.Globalization;

namespace Nerv.IIP.Business.Quality.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》L1 背景历史引擎的 **质量域侧**（二期）。
///
/// 产出（设定集 §7）：三张世界观检验计划 + 约 7000 条检验任务（工序终检 / 来料检验 / 成品终检）
/// 及其检验记录，其中约 2.3% 判不合格并开 <c>NCR-2026-####</c>，按返工 60 / 让步 25 / 报废 15 处置，
/// 每张 NCR 都留下「开单 → MRB 评审通过 → 处置 → 关单引用」的完整持有痕迹。
///
/// 与一期的一致性靠 <see cref="WorldHistoryQualitySpec.BuildInspectionFacts"/> 一个确定性纯函数达成：
/// 检验任务的源单据号全部来自一期已落库的工单 / 收货单 / 发货单，两侧不通信、不跨库查询、不建跨 schema 外键。
///
/// 领域事件说明：本仓栈里 <c>DbContext.SaveChangesAsync()</c> 不派发领域事件（派发只发生在
/// netcorepal 的 UnitOfWork/命令管线上），因此这里可以放心调用会 <c>AddDomainEvent</c> 的聚合方法，
/// 历史数据不会反向触发 CAP 集成事件风暴——与一期 ERP/MES seed 同一前提。
/// </summary>
public sealed class WorldHistorySeedService(ApplicationDbContext dbContext)
{
    /// <summary>每批检验任务数。批内共享一次预查与一次 <c>SaveChanges</c>，批末清变更跟踪器。</summary>
    public const int BatchSize = 200;

    private const string ClosureActor = "system:business-quality-world-history";
    private const string HoldSiteCode = WorldHistorySpec.SiteCode;
    private const string HoldQualityStatus = "quarantine";
    private const string HoldOwnerType = "own";

    public async Task<WorldHistoryQualitySeedReport> SeedAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        double scale,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);

        var plansWritten = await SeedInspectionPlansAsync(organizationId, environmentId, cancellationToken);
        var plans = await LoadInspectionPlansAsync(organizationId, environmentId, cancellationToken);

        var facts = WorldHistoryQualitySpec.BuildInspectionFacts(asOfDate, scale);
        var counters = new SeedCounters();

        for (var batchStart = 0; batchStart < facts.Count; batchStart += BatchSize)
        {
            var batch = facts.Skip(batchStart).Take(BatchSize).ToArray();
            var existing = await LoadExistingTasksAsync(
                organizationId,
                environmentId,
                batch.Select(fact => fact.TriggerIdempotencyKey).ToArray(),
                cancellationToken);

            var added = 0;
            foreach (var fact in batch)
            {
                if (!existing.ContainsKey(fact.TriggerIdempotencyKey))
                {
                    WriteInspectionChain(organizationId, environmentId, fact, plans[fact.PlanCode], counters);
                    added++;
                }
            }

            if (added > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            dbContext.ChangeTracker.Clear();
        }

        // fail-closed：检验数量链 / 不合格率 / 处置分布 / 报废量边界对不上就让 seed 失败。
        var validation = await new WorldHistoryConsistencyValidator(dbContext)
            .ValidateAsync(organizationId, environmentId, asOfDate, scale, cancellationToken);

        return new WorldHistoryQualitySeedReport(
            InspectionPlansWritten: plansWritten,
            InspectionTasksWritten: counters.Tasks,
            InspectionRecordsWritten: counters.Records,
            ReinspectionRecordsWritten: counters.Reinspections,
            NonconformanceReportsWritten: counters.NonconformanceReports,
            Validation: validation);
    }

    #region 检验计划

    /// <summary>按 <c>PlanCode</c> 幂等补齐三张世界观检验计划；已存在的一律不动（保留租户事实）。</summary>
    private async Task<int> SeedInspectionPlansAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken)
    {
        var planCodes = WorldHistoryQualitySpec.InspectionPlans.Select(x => x.PlanCode).ToArray();
        var existing = (await dbContext.InspectionPlans
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                    planCodes.Contains(x.PlanCode))
                .Select(x => x.PlanCode)
                .ToArrayAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        // 计划本身没有业务时间戳语义，统一回填到上线日，避免历史页面里出现「今天创建的历史计划」。
        var goLiveUtc = WorldHistoryCalendar.GoLiveDate.ToDateTime(new TimeOnly(0, 0), DateTimeKind.Utc);
        var written = 0;
        foreach (var definition in WorldHistoryQualitySpec.InspectionPlans.Where(x => !existing.Contains(x.PlanCode)))
        {
            var plan = InspectionPlan.Create(
                organizationId,
                environmentId,
                definition.PlanCode,
                definition.Category,
                // 计划不绑定 SKU / 供应商 / 工作中心：三张计划覆盖 24 个成品与全部外购物料。
                skuCode: null,
                partnerId: null,
                workCenterId: null,
                deviceAssetId: null,
                documentType: null);

            foreach (var characteristic in definition.Characteristics)
            {
                plan.AddCharacteristic(
                    characteristic.Code,
                    characteristic.Name,
                    characteristic.Method,
                    characteristic.Severity,
                    characteristic.Required,
                    characteristic.SamplingRule,
                    characteristic.CharacteristicType,
                    characteristic.NominalValue,
                    characteristic.LowerSpecLimit,
                    characteristic.UpperSpecLimit,
                    characteristic.UnitCode,
                    samplingPlan: null);
            }

            plan.Activate();
            dbContext.InspectionPlans.Add(plan);
            Backdate(plan, x => x.CreatedAtUtc, goLiveUtc);
            Backdate(plan, x => x.UpdatedAtUtc, goLiveUtc);
            Backdate(plan, x => x.ActivatedAtUtc, (DateTime?)goLiveUtc);
            written++;
        }

        if (written > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        dbContext.ChangeTracker.Clear();
        return written;
    }

    private async Task<Dictionary<string, InspectionPlan>> LoadInspectionPlansAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken)
    {
        var planCodes = WorldHistoryQualitySpec.InspectionPlans.Select(x => x.PlanCode).ToArray();
        var plans = await dbContext.InspectionPlans
            .AsNoTracking()
            .Include(x => x.Characteristics)
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                planCodes.Contains(x.PlanCode))
            .ToArrayAsync(cancellationToken);

        var missing = planCodes.Except(plans.Select(x => x.PlanCode), StringComparer.Ordinal).ToArray();
        if (missing.Length > 0)
        {
            throw new WorldHistoryConsistencyException(
                $"世界观检验计划缺失：{string.Join(", ", missing)}——历史检验任务无处可挂。");
        }

        return plans.ToDictionary(x => x.PlanCode, StringComparer.Ordinal);
    }

    #endregion

    private async Task<Dictionary<string, InspectionTask>> LoadExistingTasksAsync(
        string organizationId,
        string environmentId,
        string[] triggerKeys,
        CancellationToken cancellationToken) =>
        (await dbContext.InspectionTasks
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                triggerKeys.Contains(x.TriggerIdempotencyKey))
            .ToArrayAsync(cancellationToken))
        .ToDictionary(x => x.TriggerIdempotencyKey, StringComparer.Ordinal);

    /// <summary>写一条历史检验链：任务 → （领取 → 检验记录）→（NCR 开单 → MRB 评审 → 处置 → 复检 → 关单）。</summary>
    private void WriteInspectionChain(
        string organizationId,
        string environmentId,
        WorldHistoryInspectionFact fact,
        InspectionPlan plan,
        SeedCounters counters)
    {
        var task = InspectionTask.CreatePending(
            organizationId,
            environmentId,
            plan.Id,
            fact.SourceType,
            fact.SourceService,
            fact.SourceDocumentId,
            fact.SourceDocumentLineId,
            fact.SkuCode,
            fact.Quantity,
            fact.UomCode,
            fact.BatchNo,
            serialNo: null,
            fact.CreatedAtUtc,
            fact.DueAtUtc,
            fact.TriggerIdempotencyKey);
        dbContext.InspectionTasks.Add(task);
        counters.Tasks++;
        if (fact.Status == WorldHistoryInspectionStatus.Pending)
        {
            if (WorldHistoryQualitySpec.ShouldPreAssignPendingTask(fact))
            {
                task.Assign(
                    fact.InspectorUserId,
                    null,
                    task.Version,
                    fact.CreatedAtUtc);
            }

            return;
        }

        task.Assign(
            fact.InspectorUserId,
            null,
            task.Version,
            fact.CreatedAtUtc);

        task.Claim(
            fact.InspectorUserId,
            [],
            task.Version,
            fact.StartedAtUtc!.Value);
        if (fact.Status == WorldHistoryInspectionStatus.InProgress)
        {
            return;
        }

        var completedAtUtc = fact.CompletedAtUtc!.Value;
        var record = InspectionRecord.Create(
            organizationId,
            environmentId,
            plan.Id,
            fact.SourceType,
            fact.SourceService,
            fact.SourceDocumentId,
            fact.SkuCode,
            fact.Quantity,
            fact.BatchNo,
            serialNo: null,
            BuildResultLines(fact),
            fact.HasNonconformance ? BuildDispositionReason(fact) : null,
            fact.HasNonconformance ? [fact.AttachmentFileId] : [],
            // 不合格件的库存维度落在不合格品隔离区——这就是 NCR 的「持有痕迹」，
            // 二期库存域会以同一库位镜像出隔离库存。
            fact.HasNonconformance ? BuildQualityHold(fact) : null);
        dbContext.InspectionRecords.Add(record);
        Backdate(record, x => x.CreatedAtUtc, completedAtUtc.UtcDateTime);
        Backdate(record, x => x.UpdatedAtUtc, completedAtUtc.UtcDateTime);
        counters.Records++;

        task.Complete(record.Id, completedAtUtc);

        if (!fact.HasNonconformance)
        {
            return;
        }

        WriteNonconformanceChain(fact, plan, record, counters);
    }

    private void WriteNonconformanceChain(
        WorldHistoryInspectionFact fact,
        InspectionPlan plan,
        InspectionRecord record,
        SeedCounters counters)
    {
        var openedAtUtc = fact.NcrOpenedAtUtc!.Value;
        var closedAtUtc = fact.NcrClosedAtUtc!.Value;

        var ncr = NonconformanceReport.OpenFromInspection(
            fact.NcrCode!,
            record,
            $"{fact.DefectReasonCode} {fact.DefectReasonText}",
            [fact.AttachmentFileId]);
        dbContext.NonconformanceReports.Add(ncr);
        record.LinkNonconformanceReport(ncr.Id.ToString());
        counters.NonconformanceReports++;

        // 返工 / 让步 / 报废都需要 MRB 评审通过后才能提交处置（领域层强约束）。
        ncr.SubmitDisposition(
            fact.DispositionType!,
            dispositionApprovalChainId: null,
            [fact.AttachmentFileId],
            [MrbReviewInput.Approve(fact.MrbReviewerUserId!, "MRB 评审通过", fact.NcrDispositionAtUtc!.Value)]);

        if (fact.ReinspectedAtUtc is { } reinspectedAtUtc)
        {
            // 返工后复检合格，才有资格关单——复检记录是 AttemptNumber=2 的第二次检验。
            var reinspection = InspectionRecord.Reinspect(
                record,
                plan,
                BuildReinspectionLines(fact),
                dispositionReason: null,
                dispositionAttachmentFileIds: []);
            dbContext.InspectionRecords.Add(reinspection);
            Backdate(reinspection, x => x.CreatedAtUtc, reinspectedAtUtc.UtcDateTime);
            Backdate(reinspection, x => x.UpdatedAtUtc, reinspectedAtUtc.UtcDateTime);
            counters.Reinspections++;
        }

        switch (fact.Disposition)
        {
            case WorldHistoryInspectionDisposition.Rework:
                // 返工关单引用一期真实存在的补产工单 WO-2026-R####（挑选规则见 WorldHistoryQualitySpec）。
                ncr.Close(fact.ReworkWorkOrderNo, null, null, "返工完成并复检合格", ClosureActor);
                break;

            case WorldHistoryInspectionDisposition.Scrap:
                // 报废关单引用库存报废流水 id——二期库存域会以同一 id 落一笔报废移动。
                // 关闭原因必须是界面可读的中文（NCR 详情页把 CloseReason 当必填展示）。
                ncr.CompleteScrapDisposition(fact.ScrapMovementId!, fact.DefectQuantity, "报废处理完成，缺陷品已隔离报废出库", ClosureActor);
                break;

            case WorldHistoryInspectionDisposition.ConditionalRelease:
                ncr.CompleteConditionalReleaseDisposition(fact.DefectQuantity, "让步接收放行，客户端确认可用", ClosureActor);
                break;

            default:
                throw new WorldHistoryConsistencyException($"{fact.NcrCode} 出现未预期的历史处置类型。");
        }

        Backdate(ncr, x => x.CreatedAtUtc, openedAtUtc.UtcDateTime);
        Backdate(ncr, x => x.UpdatedAtUtc, closedAtUtc.UtcDateTime);
        Backdate(record, x => x.UpdatedAtUtc, openedAtUtc.UtcDateTime);
    }

    #region 检验结果行

    private static StockReleaseDimension BuildQualityHold(WorldHistoryInspectionFact fact) =>
        StockReleaseDimension.Create(
            fact.UomCode,
            HoldSiteCode,
            WorldHistoryPhase2Spec.QualityHoldLocationCode,
            HoldQualityStatus,
            HoldOwnerType,
            ownerId: null);

    private static string BuildDispositionReason(WorldHistoryInspectionFact fact) =>
        fact.Disposition switch
        {
            WorldHistoryInspectionDisposition.Rework => $"{fact.DefectReasonText}，判定返工",
            WorldHistoryInspectionDisposition.ConditionalRelease => $"{fact.DefectReasonText}，判定让步接收",
            _ => $"{fact.DefectReasonText}，判定报废",
        };

    /// <summary>
    /// 首检结果行：计量特性给一个规格带内的实测值，计数特性给合格；
    /// 若本次判不合格，则把命中的那一条特性替换为 failed / conditional-release 行。
    /// </summary>
    private static IReadOnlyCollection<InspectionResultLineInput> BuildResultLines(WorldHistoryInspectionFact fact)
    {
        var definition = WorldHistoryQualitySpec.PlanFor(fact.SourceType);
        var lines = new List<InspectionResultLineInput>(definition.Characteristics.Count);
        foreach (var characteristic in definition.Characteristics)
        {
            var isDefect = fact.HasNonconformance &&
                string.Equals(characteristic.Code, fact.DefectCharacteristicCode, StringComparison.Ordinal);
            lines.Add(isDefect ? BuildDefectLine(fact, characteristic) : BuildPassingLine(fact, characteristic));
        }

        return lines;
    }

    /// <summary>复检结果行：全部落在规格带内，因此复检判定必为 passed。</summary>
    private static IReadOnlyCollection<InspectionResultLineInput> BuildReinspectionLines(WorldHistoryInspectionFact fact)
    {
        var definition = WorldHistoryQualitySpec.PlanFor(fact.SourceType);
        return [.. definition.Characteristics.Select(characteristic => characteristic.IsVariable
            ? InspectionResultLineInput.Measure(characteristic.Code, characteristic.NominalValue!.Value, characteristic.UnitCode, [])
            : InspectionResultLineInput.Pass(characteristic.Code, "合格", null, []))];
    }

    private static InspectionResultLineInput BuildPassingLine(
        WorldHistoryInspectionFact fact,
        WorldHistoryInspectionCharacteristic characteristic)
    {
        if (!characteristic.IsVariable)
        {
            return InspectionResultLineInput.Pass(characteristic.Code, "合格", null, []);
        }

        // 实测值在公差带内抖动（±80% 的单边公差），让 SPC 图与检验明细看起来像真测出来的。
        var random = new WorldHistoryRandom($"measure:{fact.TriggerIdempotencyKey}:{characteristic.Code}");
        var nominal = characteristic.NominalValue!.Value;
        var halfTolerance = (characteristic.UpperSpecLimit ?? nominal) - nominal;
        var offset = halfTolerance * 0.8m * (random.NextInt(-50, 51) / 50m);
        return InspectionResultLineInput.Measure(
            characteristic.Code,
            decimal.Round(nominal + offset, 2),
            characteristic.UnitCode,
            []);
    }

    private static InspectionResultLineInput BuildDefectLine(
        WorldHistoryInspectionFact fact,
        WorldHistoryInspectionCharacteristic characteristic)
    {
        var observed = characteristic.IsVariable
            ? decimal
                .Round((characteristic.UpperSpecLimit ?? characteristic.NominalValue!.Value) * 1.02m, 2)
                .ToString(CultureInfo.InvariantCulture)
            : "不合格";

        return fact.Disposition == WorldHistoryInspectionDisposition.ConditionalRelease
            ? InspectionResultLineInput.ConditionalRelease(
                characteristic.Code,
                observed,
                fact.DefectReasonCode!,
                fact.DefectQuantity,
                [fact.AttachmentFileId])
            : InspectionResultLineInput.Fail(
                characteristic.Code,
                observed,
                fact.DefectReasonCode!,
                fact.DefectQuantity,
                [fact.AttachmentFileId]);
    }

    #endregion

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
        public int Tasks { get; set; }
        public int Records { get; set; }
        public int Reinspections { get; set; }
        public int NonconformanceReports { get; set; }
    }
}

/// <summary>一次 L1 质量域历史生成的产出摘要。</summary>
public sealed record WorldHistoryQualitySeedReport(
    int InspectionPlansWritten,
    int InspectionTasksWritten,
    int InspectionRecordsWritten,
    int ReinspectionRecordsWritten,
    int NonconformanceReportsWritten,
    WorldHistoryQualityValidationReport Validation);
