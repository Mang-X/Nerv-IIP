using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.BarcodeRuleAggregate;
using Nerv.IIP.Business.BarcodeLabel.Infrastructure;
using System.Globalization;
using System.Text;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》§7 一致性校验器的 **条码标签域侧**（二期）。**fail-closed**：
/// 任何一条不成立即抛 <see cref="WorldHistoryLabelConsistencyException"/>。
///
/// 覆盖：
/// <list type="number">
/// <item>4 套模板与 4 条条码规则齐全且 active，规则配置与规格逐字段一致；</item>
/// <item>打印批次数落在 ~900 目标口径内，每批的标签明细数等于请求数量，且只有 printed / failed 两种终态；</item>
/// <item>扫码 ↔ 源单据对账：源单据号必须是共享形状真正产出的号
///       （从 <c>BuildWorkOrderFacts</c> / <c>MaterialIssues</c> / <c>BuildPurchasePlans</c> 现场重算），
///       且扫码时刻不早于源单据自身的时刻——这就是设定集的「时间戳与源单据一致」；</item>
/// <item>被接受的 <c>inventory.*</c> 扫码必须带齐库存维度，且库位与库存域对同一动作用的库位一致；</item>
/// <item>扫到的值与其归属规则的前缀 / 长度一致，且等于该规则现算的值；</item>
/// <item>全部时间戳落在 <c>[2026-01-05, asOfDate]</c> 内且不在周日；</item>
/// <item>与 MAN-519 固定演示事实、千单规模块完全隔离；</item>
/// <item>20 条「源单据 → 打印批次 → 扫码」全链抽样，供启动日志肉眼核对。</item>
/// </list>
///
/// 跨服务的对账（工单 / 收货单是否真在 MES、ERP 库里）不在这里做——条码域看不到别人的库；
/// 配对由共享形状的确定性与各侧黄金向量测试保证。
/// </summary>
public sealed class WorldHistoryConsistencyValidator(ApplicationDbContext dbContext)
{
    public const int SampleSize = 20;

    /// <summary>打印批次总数的相对容差（目标由规格精确算出，这里只兜住取整误差）。</summary>
    public const double PrintBatchTolerance = 0.02;

    /// <summary>扫码时刻允许晚于源单据时刻的上限（同一班次内的走动时间）。</summary>
    public static readonly TimeSpan MaxScanDelay = TimeSpan.FromMinutes(30);

    private static readonly string[] ReservedInfixes = ["-DEMO-", "-SCALE-"];

    private const string PrintBatchKeyPrefix = "PB-";
    private const string ScanKeyPrefix = "SCAN-";

    public async Task<WorldHistoryLabelValidationReport> ValidateAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        double scale,
        CancellationToken cancellationToken = default)
    {
        var failures = new List<string>();
        var lowerBound = new DateTimeOffset(WorldHistoryCalendar.GoLiveDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var upperBound = new DateTimeOffset(asOfDate.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);

        var rules = await LoadRulesAsync(organizationId, environmentId, cancellationToken);
        CheckTemplatesAndRules(
            await LoadTemplatesAsync(organizationId, environmentId, cancellationToken),
            rules,
            failures);

        var batchFacts = WorldHistoryLabelSpec.BuildPrintBatchFacts(asOfDate, scale);
        var batches = await LoadPrintBatchesAsync(organizationId, environmentId, cancellationToken);
        CheckPrintBatches(batchFacts, batches, lowerBound, upperBound, failures);

        var scanFacts = WorldHistoryLabelSpec.BuildScanFacts(asOfDate, scale);
        var scans = await LoadScansAsync(organizationId, environmentId, cancellationToken);
        var sourceIndex = WorldHistorySourceDocumentIndex.Build(asOfDate, scale);
        CheckScans(scanFacts, scans, rules, sourceIndex, lowerBound, upperBound, failures);

        CheckIsolation(batches, scans, failures);

        if (failures.Count > 0)
        {
            throw new WorldHistoryLabelConsistencyException(failures);
        }

        return new WorldHistoryLabelValidationReport(
            LabelTemplatesChecked: WorldHistoryLabelSpec.Templates.Count,
            BarcodeRulesChecked: rules.Count,
            PrintBatchesChecked: batches.Count,
            PrintedBatchesChecked: batches.Count(x => string.Equals(x.Status, "printed", StringComparison.Ordinal)),
            FailedBatchesChecked: batches.Count(x => string.Equals(x.Status, "failed", StringComparison.Ordinal)),
            PrintItemsChecked: batches.Sum(x => x.ItemCount),
            ScanRecordsChecked: scans.Count,
            AcceptedScansChecked: scans.Count(x => string.Equals(x.Result, "accepted", StringComparison.Ordinal)),
            RejectedScansChecked: scans.Count(x => string.Equals(x.Result, "rejected", StringComparison.Ordinal)),
            DeviceFleetSize: scans.Select(x => x.DeviceCode).Distinct(StringComparer.Ordinal).Count(),
            Sample: BuildSample(batches, scans));
    }

    #region 1) 模板与条码规则

    private static void CheckTemplatesAndRules(
        Dictionary<string, TemplateProjection> templates,
        Dictionary<string, BarcodeRule> rules,
        List<string> failures)
    {
        foreach (var definition in WorldHistoryLabelSpec.Templates)
        {
            if (!templates.TryGetValue(definition.TemplateCode, out var template))
            {
                failures.Add($"标签模板 {definition.TemplateCode} 缺失。");
            }
            else
            {
                if (!string.Equals(template.Status, "active", StringComparison.Ordinal))
                {
                    failures.Add($"标签模板 {definition.TemplateCode} 状态为 '{template.Status}'，历史模板必须 active。");
                }

                if (!string.Equals(template.TemplateFileId, definition.TemplateFileId, StringComparison.Ordinal))
                {
                    failures.Add($"标签模板 {definition.TemplateCode} 的模板文件 id 与规格不符。");
                }
            }

            if (!rules.TryGetValue(definition.RuleCode, out var rule))
            {
                failures.Add($"条码规则 {definition.RuleCode} 缺失。");
                continue;
            }

            if (!string.Equals(rule.Status, "active", StringComparison.Ordinal))
            {
                failures.Add($"条码规则 {definition.RuleCode} 状态为 '{rule.Status}'，历史规则必须 active。");
            }

            if (!string.Equals(rule.BarcodeType, definition.BarcodeType, StringComparison.Ordinal) ||
                !string.Equals(rule.Prefix, definition.Prefix, StringComparison.Ordinal) ||
                rule.Length != definition.Length)
            {
                failures.Add($"条码规则 {definition.RuleCode} 的类型 / 前缀 / 长度与规格不符。");
            }

            var missingTypes = definition.AllowedSourceDocumentTypes
                .Except(rule.AllowedSourceDocumentTypes, StringComparer.Ordinal)
                .ToArray();
            if (missingTypes.Length > 0)
            {
                failures.Add($"条码规则 {definition.RuleCode} 未放行源单据类型：{string.Join(", ", missingTypes)}。");
            }
        }
    }

    #endregion

    #region 2) 打印批次

    private static void CheckPrintBatches(
        IReadOnlyList<WorldHistoryPrintBatchFact> facts,
        IReadOnlyList<PrintBatchProjection> batches,
        DateTimeOffset lowerBound,
        DateTimeOffset upperBound,
        List<string> failures)
    {
        var factByKey = facts.ToDictionary(x => x.IdempotencyKey, StringComparer.Ordinal);

        // 总数：目标由规格精确算出（见 WorldHistoryLabelSpec 裁决点一），这里只兜住取整误差。
        var allowed = Math.Max(1, (int)Math.Ceiling(facts.Count * PrintBatchTolerance));
        if (Math.Abs(batches.Count - facts.Count) > allowed)
        {
            failures.Add($"打印批次总数 {batches.Count} 偏离本次计划的 {facts.Count}（容差 ±{allowed}）。");
        }

        foreach (var missing in factByKey.Keys
                     .Except(batches.Select(x => x.IdempotencyKey), StringComparer.Ordinal)
                     .Take(5))
        {
            failures.Add($"计划中的打印批次 {missing} 未落库。");
        }

        foreach (var batch in batches)
        {
            if (!factByKey.TryGetValue(batch.IdempotencyKey, out var fact))
            {
                failures.Add($"库内打印批次 {batch.IdempotencyKey} 不在本次计划内（号段被外部占用？）。");
                continue;
            }

            if (batch.ItemCount != fact.RequestedQuantity || batch.RequestedQuantity != fact.RequestedQuantity)
            {
                failures.Add(
                    $"{batch.IdempotencyKey} 的标签明细数 {batch.ItemCount} 与请求数量 {batch.RequestedQuantity} " +
                    $"/ 计划数量 {fact.RequestedQuantity} 不一致。");
            }

            if (!string.Equals(batch.Status, fact.TerminalStatus, StringComparison.Ordinal))
            {
                failures.Add($"{batch.IdempotencyKey} 状态为 '{batch.Status}'，与计划的 '{fact.TerminalStatus}' 不符。");
            }

            if (batch.Status is "pending" or "sent-to-printer")
            {
                failures.Add($"{batch.IdempotencyKey} 停在非终态 '{batch.Status}'，历史批次必须已打印或已失败。");
            }

            CheckMoment($"{batch.IdempotencyKey} 创建", batch.CreatedAtUtc, lowerBound, upperBound, failures);
            if (batch.CompletedAtUtc is { } completedAtUtc)
            {
                CheckMoment($"{batch.IdempotencyKey} 完成", completedAtUtc, lowerBound, upperBound, failures);
                if (completedAtUtc < batch.CreatedAtUtc)
                {
                    failures.Add($"{batch.IdempotencyKey} 完成时间早于创建时间。");
                }
            }
        }
    }

    #endregion

    #region 3–6) 扫码对账

    private static void CheckScans(
        IReadOnlyList<WorldHistoryScanFact> facts,
        IReadOnlyList<ScanProjection> scans,
        Dictionary<string, BarcodeRule> rules,
        WorldHistorySourceDocumentIndex sourceIndex,
        DateTimeOffset lowerBound,
        DateTimeOffset upperBound,
        List<string> failures)
    {
        var factByKey = facts.ToDictionary(x => x.IdempotencyKey, StringComparer.Ordinal);

        foreach (var missing in factByKey.Keys
                     .Except(scans.Select(x => x.IdempotencyKey), StringComparer.Ordinal)
                     .Take(5))
        {
            failures.Add($"计划中的扫码记录 {missing} 未落库。");
        }

        foreach (var scan in scans)
        {
            if (!factByKey.TryGetValue(scan.IdempotencyKey, out var fact))
            {
                failures.Add($"库内扫码记录 {scan.IdempotencyKey} 不在本次计划内（号段被外部占用？）。");
                continue;
            }

            // 3) 源单据必须是共享形状真正产出的号（现场重算，不信任规格自证）。
            if (!sourceIndex.Contains(scan.SourceWorkflow, scan.SourceDocumentId))
            {
                failures.Add(
                    $"{scan.IdempotencyKey} 的 {scan.SourceWorkflow} 扫码挂在一张共享形状并不产出的单据 " +
                    $"{scan.SourceDocumentId} 上。");
                continue;
            }

            // 3) 时间戳与源单据一致：不早于源单据动作时刻，且不晚过一个班内走动的时间。
            var sourceMomentUtc = sourceIndex.MomentFor(scan.SourceWorkflow, scan.SourceDocumentId);
            if (scan.ScannedAtUtc < sourceMomentUtc)
            {
                failures.Add(
                    $"{scan.IdempotencyKey} 扫码时刻 {scan.ScannedAtUtc:O} 早于源单据 {scan.SourceDocumentId} " +
                    $"的动作时刻 {sourceMomentUtc:O}。");
            }
            else if (scan.ScannedAtUtc - sourceMomentUtc > MaxScanDelay)
            {
                failures.Add(
                    $"{scan.IdempotencyKey} 扫码时刻晚于源单据动作时刻 " +
                    $"{(scan.ScannedAtUtc - sourceMomentUtc).TotalMinutes:0} 分钟，超出同班次走动时间。");
            }

            // 6) 时间戳落在历史区间内且不在周日。
            CheckMoment($"{scan.IdempotencyKey} 扫码", scan.ScannedAtUtc, lowerBound, upperBound, failures);

            // 5) 条码值与归属规则一致。
            CheckScannedValue(scan, fact, rules, failures);

            // 4) 被接受的库存类扫码必须带齐库存维度，且库位与库存域同一动作一致。
            CheckInventoryContext(scan, fact, sourceIndex, failures);
        }
    }

    private static void CheckScannedValue(
        ScanProjection scan,
        WorldHistoryScanFact fact,
        Dictionary<string, BarcodeRule> rules,
        List<string> failures)
    {
        if (!rules.TryGetValue(fact.RuleCode, out var rule))
        {
            failures.Add($"{scan.IdempotencyKey} 引用的条码规则 {fact.RuleCode} 不存在。");
            return;
        }

        if (!scan.ScannedValue.StartsWith(rule.Prefix, StringComparison.Ordinal))
        {
            failures.Add($"{scan.IdempotencyKey} 的条码值 '{scan.ScannedValue}' 不带规则 {rule.RuleCode} 的前缀 '{rule.Prefix}'。");
        }

        if (scan.ScannedValue.Length > rule.Length)
        {
            failures.Add($"{scan.IdempotencyKey} 的条码值长度 {scan.ScannedValue.Length} 超过规则 {rule.RuleCode} 的上限 {rule.Length}。");
        }

        if (!string.Equals(rule.Status, "active", StringComparison.Ordinal))
        {
            // 规则失活的失败项已由模板 / 规则校验记下，这里不再重复报，也不能调 GenerateValue（会直接抛）。
            return;
        }

        var expected = rule.GenerateValue(fact.ValueSourceDocumentType, fact.ValueSourceDocumentId, fact.ValueSequence);
        if (!string.Equals(scan.ScannedValue, expected, StringComparison.Ordinal))
        {
            failures.Add($"{scan.IdempotencyKey} 的条码值 '{scan.ScannedValue}' 不是规则 {rule.RuleCode} 会生成的值（期望 '{expected}'）。");
        }
    }

    private static void CheckInventoryContext(
        ScanProjection scan,
        WorldHistoryScanFact fact,
        WorldHistorySourceDocumentIndex sourceIndex,
        List<string> failures)
    {
        if (!string.Equals(scan.Result, "accepted", StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(scan.RejectionReason))
            {
                failures.Add($"{scan.IdempotencyKey} 判定 rejected 却没有拒收原因。");
            }

            return;
        }

        if (!fact.RequiresInventoryContext)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(scan.SkuCode) || string.IsNullOrWhiteSpace(scan.UomCode) ||
            string.IsNullOrWhiteSpace(scan.SiteCode) || string.IsNullOrWhiteSpace(scan.LocationCode) ||
            string.IsNullOrWhiteSpace(scan.QualityStatus) || string.IsNullOrWhiteSpace(scan.OwnerType) ||
            scan.Quantity is null or <= 0m)
        {
            failures.Add($"{scan.IdempotencyKey} 是被接受的 {scan.SourceWorkflow} 扫码，却没有带齐库存维度。");
            return;
        }

        var expectedLocation = sourceIndex.ExpectedLocation(scan.SourceWorkflow, scan.SourceDocumentId);
        if (expectedLocation is not null && !string.Equals(scan.LocationCode, expectedLocation, StringComparison.Ordinal))
        {
            failures.Add(
                $"{scan.IdempotencyKey} 的库位 {scan.LocationCode} 与库存域对同一动作使用的 {expectedLocation} 不一致。");
        }

        if (!string.Equals(scan.OwnerType, WorldHistoryLabelSpec.OwnerType, StringComparison.Ordinal))
        {
            failures.Add($"{scan.IdempotencyKey} 的货主类型 {scan.OwnerType} 与历史库存的 company 口径不一致。");
        }
    }

    private static void CheckMoment(
        string label,
        DateTimeOffset moment,
        DateTimeOffset lowerBound,
        DateTimeOffset upperBound,
        List<string> failures)
    {
        if (moment < lowerBound || moment > upperBound)
        {
            failures.Add($"{label}时间 {moment:O} 落在历史区间之外。");
        }

        if (!WorldHistoryCalendar.IsWorkingDay(DateOnly.FromDateTime(moment.UtcDateTime)))
        {
            failures.Add($"{label}时间 {moment:O} 落在周日（停产保养日）。");
        }
    }

    #endregion

    #region 7) 隔离性

    private static void CheckIsolation(
        IReadOnlyList<PrintBatchProjection> batches,
        IReadOnlyList<ScanProjection> scans,
        List<string> failures)
    {
        // 只查「本引擎自己造的号」——单据号与条码值。
        // 打印变量 JSON **不查**：箱贴上印的客户编码合法地包含 CUST-DEMO-001，
        // 那是 MAN-519 固定演示事实里的客户主数据，本引擎只引用不创建（见 WorldHistorySpec §6）。
        foreach (var infix in ReservedInfixes)
        {
            var batch = batches.FirstOrDefault(x =>
                x.SourceDocumentId.Contains(infix, StringComparison.Ordinal) ||
                (x.FirstLabelValue ?? string.Empty).Contains(infix, StringComparison.Ordinal));
            if (batch is not null)
            {
                failures.Add($"打印批次 {batch.IdempotencyKey}（源单据 {batch.SourceDocumentId}）落进了保留号段 '{infix}'。");
            }

            var scan = scans.FirstOrDefault(x =>
                x.SourceDocumentId.Contains(infix, StringComparison.Ordinal) ||
                x.ScannedValue.Contains(infix, StringComparison.Ordinal));
            if (scan is not null)
            {
                failures.Add($"扫码记录 {scan.IdempotencyKey}（源单据 {scan.SourceDocumentId}）落进了保留号段 '{infix}'。");
            }
        }
    }

    #endregion

    #region 载入紧凑投影

    private async Task<Dictionary<string, TemplateProjection>> LoadTemplatesAsync(
        string organizationId, string environmentId, CancellationToken cancellationToken)
    {
        var codes = WorldHistoryLabelSpec.Templates.Select(x => x.TemplateCode).ToArray();
        return (await dbContext.LabelTemplates
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                    codes.Contains(x.TemplateCode))
                .Select(x => new TemplateProjection(x.TemplateCode, x.TemplateName, x.TemplateFileId, x.Status))
                .ToArrayAsync(cancellationToken))
            .ToDictionary(x => x.TemplateCode, StringComparer.Ordinal);
    }

    private async Task<Dictionary<string, BarcodeRule>> LoadRulesAsync(
        string organizationId, string environmentId, CancellationToken cancellationToken)
    {
        var codes = WorldHistoryLabelSpec.Templates.Select(x => x.RuleCode).ToArray();
        return (await dbContext.BarcodeRules
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                    codes.Contains(x.RuleCode))
                .ToArrayAsync(cancellationToken))
            .ToDictionary(x => x.RuleCode, StringComparer.Ordinal);
    }

    private async Task<IReadOnlyList<PrintBatchProjection>> LoadPrintBatchesAsync(
        string organizationId, string environmentId, CancellationToken cancellationToken) =>
        await dbContext.LabelPrintBatches
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.IdempotencyKey.StartsWith(PrintBatchKeyPrefix))
            .Select(x => new PrintBatchProjection(
                x.IdempotencyKey,
                x.SourceDocumentType,
                x.SourceDocumentId,
                x.LabelValuesJson,
                x.RequestedQuantity,
                x.Items.Count,
                x.Items.OrderBy(item => item.SequenceNo).Select(item => item.LabelValue).FirstOrDefault(),
                x.Items.Count(item => item.Status == "voided"),
                x.Items.Count(item => item.Status == "reprinted"),
                x.Status,
                x.CreatedAtUtc,
                x.CompletedAtUtc))
            .ToArrayAsync(cancellationToken);

    private async Task<IReadOnlyList<ScanProjection>> LoadScansAsync(
        string organizationId, string environmentId, CancellationToken cancellationToken) =>
        await dbContext.ScanRecords
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.IdempotencyKey.StartsWith(ScanKeyPrefix))
            .Select(x => new ScanProjection(
                x.IdempotencyKey,
                x.DeviceCode,
                x.ScannedValue,
                x.SourceWorkflow,
                x.SourceDocumentId,
                x.Result,
                x.RejectionReason,
                x.SkuCode,
                x.UomCode,
                x.SiteCode,
                x.LocationCode,
                x.QualityStatus,
                x.OwnerType,
                x.Quantity,
                x.ScannedAtUtc))
            .ToArrayAsync(cancellationToken);

    #endregion

    /// <summary>8) 20 条「源单据 → 打印批次 → 扫码」全链抽样。</summary>
    private static IReadOnlyList<string> BuildSample(
        IReadOnlyList<PrintBatchProjection> batches,
        IReadOnlyList<ScanProjection> scans)
    {
        var ordered = batches
            .OrderBy(x => x.SourceDocumentType, StringComparer.Ordinal)
            .ThenBy(x => x.SourceDocumentId, StringComparer.Ordinal)
            .ToArray();
        if (ordered.Length == 0)
        {
            return [];
        }

        var scansByDocument = scans
            .GroupBy(x => x.SourceDocumentId, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.OrderBy(scan => scan.ScannedAtUtc).ToArray(), StringComparer.Ordinal);

        var stride = Math.Max(1, ordered.Length / SampleSize);
        var sample = new List<string>(SampleSize);
        for (var index = 0; index < ordered.Length && sample.Count < SampleSize; index += stride)
        {
            var batch = ordered[index];
            var builder = new StringBuilder();
            builder.Append(CultureInfo.InvariantCulture,
                $"{batch.SourceDocumentType} {batch.SourceDocumentId} → {batch.IdempotencyKey}[{batch.Status}] ");
            builder.Append(CultureInfo.InvariantCulture,
                $"×{batch.ItemCount}(作废 {batch.VoidedItemCount}/补打 {batch.ReprintedItemCount}) ");
            builder.Append(CultureInfo.InvariantCulture, $"打印={batch.CreatedAtUtc:yyyy-MM-dd HH:mm}Z");
            if (batch.FirstLabelValue is { } labelValue)
            {
                builder.Append(CultureInfo.InvariantCulture, $" 首张={Shorten(labelValue)}");
            }

            if (scansByDocument.TryGetValue(batch.SourceDocumentId, out var documentScans))
            {
                foreach (var scan in documentScans)
                {
                    builder.Append(CultureInfo.InvariantCulture,
                        $" → 扫码 {scan.SourceWorkflow}/{scan.DeviceCode}[{scan.Result}] {scan.ScannedAtUtc:MM-dd HH:mm}Z");
                }
            }
            else
            {
                // 箱贴 / 工位标签本就只打印不回扫；批次 / 物料标签则可能只是本次扫码抽样没覆盖到这张单。
                builder.Append(" → 无同单据扫码");
            }

            sample.Add(builder.ToString());
        }

        return sample;
    }

    private static string Shorten(string value) =>
        value.Length <= 40 ? value : value[..40] + "…";

    private sealed record TemplateProjection(string TemplateCode, string TemplateName, string TemplateFileId, string Status);

    private sealed record PrintBatchProjection(
        string IdempotencyKey,
        string SourceDocumentType,
        string SourceDocumentId,
        string LabelValuesJson,
        int RequestedQuantity,
        int ItemCount,
        string? FirstLabelValue,
        int VoidedItemCount,
        int ReprintedItemCount,
        string Status,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset? CompletedAtUtc);

    private sealed record ScanProjection(
        string IdempotencyKey,
        string DeviceCode,
        string ScannedValue,
        string SourceWorkflow,
        string SourceDocumentId,
        string Result,
        string? RejectionReason,
        string? SkuCode,
        string? UomCode,
        string? SiteCode,
        string? LocationCode,
        string? QualityStatus,
        string? OwnerType,
        decimal? Quantity,
        DateTimeOffset ScannedAtUtc);
}

/// <summary>
/// 从共享形状现场重算的「源单据 → 动作时刻 / 库位」索引。
///
/// 这是扫码对账的独立事实源：它只依赖 <see cref="WorldHistoryPhase2Spec"/> /
/// <see cref="WorldHistoryProcurementSpec"/>，不读 <see cref="WorldHistoryLabelSpec"/> 的扫码表，
/// 因此「扫码挂在真实存在的单据上、时刻与源单据一致」是被真正验证的，而不是自证的。
/// </summary>
public sealed class WorldHistorySourceDocumentIndex
{
    private readonly Dictionary<string, DateTimeOffset> materialIssues;
    private readonly Dictionary<string, DateTimeOffset> finishedGoodsReceipts;
    private readonly Dictionary<string, DateTimeOffset> purchaseReceipts;
    private readonly Dictionary<string, DateTimeOffset> productionReports;
    private readonly Dictionary<string, DateTimeOffset> qualityInspections;
    private readonly Dictionary<string, string> materialIssueLocations;
    private readonly Dictionary<string, string> purchaseReceiptSkus;

    private WorldHistorySourceDocumentIndex(
        Dictionary<string, DateTimeOffset> materialIssues,
        Dictionary<string, DateTimeOffset> finishedGoodsReceipts,
        Dictionary<string, DateTimeOffset> purchaseReceipts,
        Dictionary<string, DateTimeOffset> productionReports,
        Dictionary<string, DateTimeOffset> qualityInspections,
        Dictionary<string, string> materialIssueLocations,
        Dictionary<string, string> purchaseReceiptSkus)
    {
        this.materialIssues = materialIssues;
        this.finishedGoodsReceipts = finishedGoodsReceipts;
        this.purchaseReceipts = purchaseReceipts;
        this.productionReports = productionReports;
        this.qualityInspections = qualityInspections;
        this.materialIssueLocations = materialIssueLocations;
        this.purchaseReceiptSkus = purchaseReceiptSkus;
    }

    public static WorldHistorySourceDocumentIndex Build(DateOnly asOfDate, double scale)
    {
        var workOrderFacts = WorldHistoryPhase2Spec.BuildWorkOrderFacts(asOfDate, scale);
        var issues = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        var issueLocations = new Dictionary<string, string>(StringComparer.Ordinal);
        var finishedGoods = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        var reports = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        var inspections = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);

        foreach (var fact in workOrderFacts)
        {
            foreach (var issue in WorldHistoryPhase2Spec.MaterialIssues(fact))
            {
                issues[issue.RequestNo] = WorldHistoryPhase2Spec.MomentOn(
                    WorldHistoryLabelSpec.ClampToHistory(issue.IssueDate, asOfDate), issue.RequestNo, "stock-issue");
                // 领料扫码是「物料到线边」那一腿，库存域的 issue-in 用的正是线边库。
                issueLocations[issue.RequestNo] = WorldHistoryPhase2Spec.LineSideLocationCode;
            }

            if (!fact.HasFinishedGoodsReceipt)
            {
                continue;
            }

            var completionDay = WorldHistoryLabelSpec.ClampToHistory(fact.Timeline.ProductionCompletionDate, asOfDate);
            finishedGoods[fact.FinishedGoodsReceiptNo] = WorldHistoryPhase2Spec.MomentOn(
                completionDay, fact.FinishedGoodsReceiptNo, "stock-fg-receipt");
            reports[fact.Plan.WorkOrderNo] = WorldHistoryPhase2Spec.MomentOn(
                completionDay, fact.Plan.WorkOrderNo, "production-report");
            if (fact.HasFinalInspection)
            {
                inspections[fact.Plan.WorkOrderNo] = WorldHistoryPhase2Spec.MomentOn(
                    completionDay, fact.Plan.WorkOrderNo, "quality-inspection");
            }
        }

        var receipts = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        var receiptSkus = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var purchase in WorldHistoryProcurementSpec.BuildPurchasePlans(asOfDate, scale)
                     .Where(plan => plan.IsReceived))
        {
            receipts[purchase.PurchaseReceiptNo] = WorldHistoryPhase2Spec.MomentOn(
                WorldHistoryLabelSpec.ClampToHistory(purchase.ReceiptDate, asOfDate),
                purchase.PurchaseReceiptNo,
                "stock-receipt");
            receiptSkus[purchase.PurchaseReceiptNo] = purchase.SkuCode;
        }

        return new WorldHistorySourceDocumentIndex(
            issues, finishedGoods, receipts, reports, inspections, issueLocations, receiptSkus);
    }

    public bool Contains(string sourceWorkflow, string sourceDocumentId) =>
        Lookup(sourceWorkflow, sourceDocumentId) is not null;

    public DateTimeOffset MomentFor(string sourceWorkflow, string sourceDocumentId) =>
        Lookup(sourceWorkflow, sourceDocumentId)
        ?? throw new WorldHistoryLabelConsistencyException(
            $"源单据 {sourceDocumentId}（{sourceWorkflow}）不在共享形状产出的号段内。");

    /// <summary>该动作在库存域使用的库位（非库存类工作流返回 <c>null</c>，不做库位断言）。</summary>
    public string? ExpectedLocation(string sourceWorkflow, string sourceDocumentId) => sourceWorkflow switch
    {
        "inventory.issue" => materialIssueLocations.GetValueOrDefault(sourceDocumentId),
        "inventory.receipt" when finishedGoodsReceipts.ContainsKey(sourceDocumentId) =>
            WorldHistoryPhase2Spec.FinishedGoodsLocationCode,
        "inventory.receipt" when purchaseReceiptSkus.ContainsKey(sourceDocumentId) =>
            WorldHistoryPhase2Spec.ReceivingStagingLocationCode,
        _ => null,
    };

    private DateTimeOffset? Lookup(string sourceWorkflow, string sourceDocumentId) => sourceWorkflow switch
    {
        "inventory.issue" => Get(materialIssues, sourceDocumentId),
        // 完工入库与采购收货共用 inventory.receipt，用号段区分（FGR-* 对 PR-*）。
        "inventory.receipt" => Get(finishedGoodsReceipts, sourceDocumentId) ?? Get(purchaseReceipts, sourceDocumentId),
        "wms.receiving" => Get(purchaseReceipts, sourceDocumentId),
        "production.report" => Get(productionReports, sourceDocumentId),
        "quality.inspection" => Get(qualityInspections, sourceDocumentId),
        _ => null,
    };

    private static DateTimeOffset? Get(Dictionary<string, DateTimeOffset> source, string key) =>
        source.TryGetValue(key, out var moment) ? moment : null;
}

/// <summary>条码标签域侧一致性校验器的产出摘要。</summary>
public sealed record WorldHistoryLabelValidationReport(
    int LabelTemplatesChecked,
    int BarcodeRulesChecked,
    int PrintBatchesChecked,
    int PrintedBatchesChecked,
    int FailedBatchesChecked,
    int PrintItemsChecked,
    int ScanRecordsChecked,
    int AcceptedScansChecked,
    int RejectedScansChecked,
    int DeviceFleetSize,
    IReadOnlyList<string> Sample);

/// <summary>一致性校验失败。抛出即代表 seed 失败（fail-closed）。</summary>
public sealed class WorldHistoryLabelConsistencyException : InvalidOperationException
{
    public WorldHistoryLabelConsistencyException(IReadOnlyList<string> failures)
        : base(BuildMessage(failures))
    {
        Failures = failures;
    }

    public WorldHistoryLabelConsistencyException()
        : base("World-history barcode-label consistency validation failed.")
    {
        Failures = [];
    }

    public WorldHistoryLabelConsistencyException(string message)
        : base(message)
    {
        Failures = [message];
    }

    public WorldHistoryLabelConsistencyException(string message, Exception innerException)
        : base(message, innerException)
    {
        Failures = [message];
    }

    public IReadOnlyList<string> Failures { get; }

    private static string BuildMessage(IReadOnlyList<string> failures)
    {
        ArgumentNullException.ThrowIfNull(failures);
        var builder = new StringBuilder("L1 背景历史一致性校验失败（条码标签域），共 ");
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
