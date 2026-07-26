using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.BarcodeRuleAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelPrintBatchAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelTemplateAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.ScanRecordAggregate;
using Nerv.IIP.Business.BarcodeLabel.Infrastructure;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》L1 背景历史引擎的 **条码标签域侧**（二期）。
///
/// 产出（设定集 §7）：4 套标签模板 + 4 条配套条码规则、约 900 个打印批次
/// （批次标签 / 成品箱贴 / 物料标签 / 工位标签四族）、以及与领料 / 入库 / 出库动作逐笔对应的扫码记录，
/// 时间戳全部由源单据的确定性时刻推出。
///
/// 与其余域的一致性靠 <see cref="WorldHistoryLabelSpec"/> 一组纯函数达成：
/// 源单据号与时刻全部由共享形状推出，本服务不通信、不跨库查询、不建跨 schema 外键。
///
/// 写入顺序（三段，各自幂等）：
/// <list type="number">
/// <item>模板（按 <c>TemplateCode</c>）与条码规则（按 <c>RuleCode</c>）——已存在的一律不动，保留租户事实；</item>
/// <item>打印批次（按 <c>IdempotencyKey</c>）——含标签明细与 EPCIS 建档事件；</item>
/// <item>扫码记录（按 <c>IdempotencyKey</c>）。</item>
/// </list>
///
/// 领域事件说明：本仓栈里 <c>DbContext.SaveChangesAsync()</c> 不派发领域事件（派发只发生在
/// netcorepal 的 UnitOfWork/命令管线上），因此这里可以放心调用会 <c>AddDomainEvent</c> 的聚合方法，
/// 历史数据不会反向触发 CAP 集成事件风暴——与一期 ERP/MES seed 同一前提。
/// </summary>
public sealed class WorldHistorySeedService(ApplicationDbContext dbContext)
{
    /// <summary>每批打印批次数。批内共享一次预查与一次 <c>SaveChanges</c>，批末清变更跟踪器。</summary>
    public const int BatchSize = 200;

    /// <summary>扫码记录是单行聚合，批量可以开大一些。</summary>
    public const int ScanBatchSize = 500;

    public async Task<WorldHistoryLabelSeedReport> SeedAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        double scale,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);

        var templatesWritten = await SeedTemplatesAsync(organizationId, environmentId, cancellationToken);
        var rulesWritten = await SeedBarcodeRulesAsync(organizationId, environmentId, cancellationToken);
        var templates = await LoadTemplateIdsAsync(organizationId, environmentId, cancellationToken);
        var rules = await LoadRulesAsync(organizationId, environmentId, cancellationToken);

        var counters = new SeedCounters();
        await WritePrintBatchesAsync(organizationId, environmentId, asOfDate, scale, templates, rules, counters, cancellationToken);
        await WriteScanRecordsAsync(organizationId, environmentId, asOfDate, scale, rules, counters, cancellationToken);

        // fail-closed：模板 / 规则缺失、批次数量不对、扫码与源单据对不上就让 seed 失败。
        var validation = await new WorldHistoryConsistencyValidator(dbContext)
            .ValidateAsync(organizationId, environmentId, asOfDate, scale, cancellationToken);

        return new WorldHistoryLabelSeedReport(
            LabelTemplatesWritten: templatesWritten,
            BarcodeRulesWritten: rulesWritten,
            PrintBatchesWritten: counters.PrintBatches,
            PrintItemsWritten: counters.PrintItems,
            EpcisEventsWritten: counters.EpcisEvents,
            ScanRecordsWritten: counters.ScanRecords,
            Validation: validation);
    }

    #region 模板与条码规则

    /// <summary>按 <c>TemplateCode</c> 幂等补齐四套模板。模板没有业务时间戳语义，统一回填到上线日。</summary>
    private async Task<int> SeedTemplatesAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken)
    {
        var codes = WorldHistoryLabelSpec.Templates.Select(x => x.TemplateCode).ToArray();
        var existing = (await dbContext.LabelTemplates
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                    codes.Contains(x.TemplateCode))
                .Select(x => x.TemplateCode)
                .ToArrayAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        var goLiveUtc = GoLiveMoment();
        var written = 0;
        foreach (var definition in WorldHistoryLabelSpec.Templates.Where(x => !existing.Contains(x.TemplateCode)))
        {
            var template = LabelTemplate.Create(
                organizationId,
                environmentId,
                definition.TemplateCode,
                definition.TemplateName,
                definition.TemplateFileId,
                definition.VariableSchemaJson,
                "active");
            dbContext.LabelTemplates.Add(template);
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

    /// <summary>按 <c>RuleCode</c> 幂等补齐四条条码规则。</summary>
    private async Task<int> SeedBarcodeRulesAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken)
    {
        var codes = WorldHistoryLabelSpec.Templates.Select(x => x.RuleCode).ToArray();
        var existing = (await dbContext.BarcodeRules
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                    codes.Contains(x.RuleCode))
                .Select(x => x.RuleCode)
                .ToArrayAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        var goLiveUtc = GoLiveMoment();
        var written = 0;
        foreach (var definition in WorldHistoryLabelSpec.Templates.Where(x => !existing.Contains(x.RuleCode)))
        {
            var rule = BarcodeRule.Create(
                organizationId,
                environmentId,
                definition.RuleCode,
                definition.BarcodeType,
                definition.Prefix,
                definition.Length,
                definition.ChecksumRule,
                [.. definition.AllowedSourceDocumentTypes],
                "active",
                definition.Gs1CompanyPrefixLength);
            dbContext.BarcodeRules.Add(rule);
            Backdate(rule, x => x.CreatedAtUtc, goLiveUtc);
            Backdate(rule, x => x.UpdatedAtUtc, goLiveUtc);
            written++;
        }

        if (written > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        dbContext.ChangeTracker.Clear();
        return written;
    }

    private async Task<Dictionary<string, LabelTemplateId>> LoadTemplateIdsAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken)
    {
        var codes = WorldHistoryLabelSpec.Templates.Select(x => x.TemplateCode).ToArray();
        var templates = await dbContext.LabelTemplates
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                codes.Contains(x.TemplateCode))
            .Select(x => new { x.TemplateCode, x.Id })
            .ToArrayAsync(cancellationToken);

        var missing = codes.Except(templates.Select(x => x.TemplateCode), StringComparer.Ordinal).ToArray();
        if (missing.Length > 0)
        {
            throw new WorldHistoryLabelConsistencyException(
                $"世界观标签模板缺失：{string.Join(", ", missing)}——历史打印批次无处可挂。");
        }

        return templates.ToDictionary(x => x.TemplateCode, x => x.Id, StringComparer.Ordinal);
    }

    /// <summary>
    /// 载入四条规则实例。<c>LabelPrintBatch.Create</c> 需要活的 <see cref="BarcodeRule"/>（要读类型与前缀现算条码值），
    /// 因此这里以 <c>AsNoTracking</c> 取出后长期持有：批末的 <c>ChangeTracker.Clear()</c> 不会影响它们。
    /// </summary>
    private async Task<Dictionary<string, BarcodeRule>> LoadRulesAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken)
    {
        var codes = WorldHistoryLabelSpec.Templates.Select(x => x.RuleCode).ToArray();
        var rules = await dbContext.BarcodeRules
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                codes.Contains(x.RuleCode))
            .ToArrayAsync(cancellationToken);

        var missing = codes.Except(rules.Select(x => x.RuleCode), StringComparer.Ordinal).ToArray();
        if (missing.Length > 0)
        {
            throw new WorldHistoryLabelConsistencyException(
                $"世界观条码规则缺失：{string.Join(", ", missing)}——历史标签值无从生成。");
        }

        return rules.ToDictionary(x => x.RuleCode, StringComparer.Ordinal);
    }

    #endregion

    #region 打印批次

    private async Task WritePrintBatchesAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        double scale,
        Dictionary<string, LabelTemplateId> templates,
        Dictionary<string, BarcodeRule> rules,
        SeedCounters counters,
        CancellationToken cancellationToken)
    {
        var facts = WorldHistoryLabelSpec.BuildPrintBatchFacts(asOfDate, scale);
        for (var batchStart = 0; batchStart < facts.Count; batchStart += BatchSize)
        {
            var slice = facts.Skip(batchStart).Take(BatchSize).ToArray();
            var keys = slice.Select(x => x.IdempotencyKey).ToArray();
            var existing = (await dbContext.LabelPrintBatches
                    .AsNoTracking()
                    .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                        keys.Contains(x.IdempotencyKey))
                    .Select(x => x.IdempotencyKey)
                    .ToArrayAsync(cancellationToken))
                .ToHashSet(StringComparer.Ordinal);

            var added = 0;
            foreach (var fact in slice.Where(x => !existing.Contains(x.IdempotencyKey)))
            {
                WritePrintBatch(organizationId, environmentId, fact, templates[fact.TemplateCode], rules[fact.RuleCode], counters);
                added++;
            }

            if (added > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            dbContext.ChangeTracker.Clear();
        }
    }

    private void WritePrintBatch(
        string organizationId,
        string environmentId,
        WorldHistoryPrintBatchFact fact,
        LabelTemplateId templateId,
        BarcodeRule rule,
        SeedCounters counters)
    {
        var batch = LabelPrintBatch.Create(
            organizationId,
            environmentId,
            rule,
            templateId,
            fact.SourceDocumentType,
            fact.SourceDocumentId,
            fact.IdempotencyKey,
            fact.LabelValuesJson,
            fact.RequestedQuantity);

        if (fact.Printed)
        {
            batch.RecordSentToPrinter(fact.PrinterId, fact.PrintJobId);
            batch.RecordPrinted();
            if (fact.VoidedSequenceNo is { } voidedSequenceNo)
            {
                batch.VoidItem(voidedSequenceNo, fact.VoidReason!);
            }
            else if (fact.ReprintedSequenceNo is { } reprintedSequenceNo)
            {
                batch.ReprintItem(reprintedSequenceNo);
            }
        }
        else
        {
            batch.RecordPrintFailed(fact.FailureReason!);
        }

        dbContext.LabelPrintBatches.Add(batch);

        // 回填时间戳：聚合方法用的是 UtcNow，历史批次必须落在源单据当天的班次内。
        Backdate(batch, x => x.CreatedAtUtc, fact.CreatedAtUtc);
        Backdate(batch, x => x.CompletedAtUtc, (DateTimeOffset?)fact.CompletedAtUtc);
        foreach (var item in batch.Items)
        {
            Backdate(item, x => x.CreatedAtUtc, fact.CreatedAtUtc);
            if (item.VoidedAtUtc is not null)
            {
                Backdate(item, x => x.VoidedAtUtc, (DateTimeOffset?)fact.CompletedAtUtc);
            }

            counters.PrintItems++;
        }

        foreach (var epcisEvent in batch.EpcisEvents)
        {
            Backdate(epcisEvent, x => x.OccurredAtUtc, fact.CreatedAtUtc);
            counters.EpcisEvents++;
        }

        counters.PrintBatches++;
    }

    #endregion

    #region 扫码记录

    private async Task WriteScanRecordsAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        double scale,
        Dictionary<string, BarcodeRule> rules,
        SeedCounters counters,
        CancellationToken cancellationToken)
    {
        var facts = WorldHistoryLabelSpec.BuildScanFacts(asOfDate, scale);
        for (var batchStart = 0; batchStart < facts.Count; batchStart += ScanBatchSize)
        {
            var slice = facts.Skip(batchStart).Take(ScanBatchSize).ToArray();
            var keys = slice.Select(x => x.IdempotencyKey).ToArray();
            var existing = (await dbContext.ScanRecords
                    .AsNoTracking()
                    .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                        keys.Contains(x.IdempotencyKey))
                    .Select(x => x.IdempotencyKey)
                    .ToArrayAsync(cancellationToken))
                .ToHashSet(StringComparer.Ordinal);

            var added = 0;
            foreach (var fact in slice.Where(x => !existing.Contains(x.IdempotencyKey)))
            {
                WriteScanRecord(organizationId, environmentId, fact, rules[fact.RuleCode], counters);
                added++;
            }

            if (added > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            dbContext.ChangeTracker.Clear();
        }
    }

    private void WriteScanRecord(
        string organizationId,
        string environmentId,
        WorldHistoryScanFact fact,
        BarcodeRule rule,
        SeedCounters counters)
    {
        // 扫到的值由「拥有这条码的规则」现算，而不是规格层复制公式——
        // 于是「这台设备当时真能扫到这个值」是可断言的，校验器用同一规则实例复算比对。
        var scannedValue = rule.GenerateValue(fact.ValueSourceDocumentType, fact.ValueSourceDocumentId, fact.ValueSequence);

        var scan = ScanRecord.Record(
            organizationId,
            environmentId,
            fact.DeviceCode,
            scannedValue,
            fact.SourceWorkflow,
            fact.SourceDocumentId,
            fact.IdempotencyKey,
            fact.Result,
            fact.RejectionReason,
            fact.SkuCode,
            fact.UomCode,
            fact.SiteCode,
            fact.LocationCode,
            fact.QualityStatus,
            fact.OwnerType,
            ownerId: null,
            fact.Quantity);

        dbContext.ScanRecords.Add(scan);
        Backdate(scan, x => x.ScannedAtUtc, fact.ScannedAtUtc);
        foreach (var epcisEvent in scan.EpcisEvents)
        {
            Backdate(epcisEvent, x => x.OccurredAtUtc, fact.ScannedAtUtc);
            counters.EpcisEvents++;
        }

        counters.ScanRecords++;
    }

    #endregion

    private static DateTimeOffset GoLiveMoment() =>
        WorldHistoryCalendar.ShiftMoment(WorldHistoryCalendar.GoLiveDate, 0, 0);

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
        public int PrintBatches { get; set; }
        public int PrintItems { get; set; }
        public int EpcisEvents { get; set; }
        public int ScanRecords { get; set; }
    }
}

/// <summary>一次 L1 条码标签域历史生成的产出摘要。</summary>
public sealed record WorldHistoryLabelSeedReport(
    int LabelTemplatesWritten,
    int BarcodeRulesWritten,
    int PrintBatchesWritten,
    int PrintItemsWritten,
    int EpcisEventsWritten,
    int ScanRecordsWritten,
    WorldHistoryLabelValidationReport Validation);
