using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.CorrectiveActionAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.MeasuringDeviceAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.SpcControlChartAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Web.Application.Queries.Spc;

namespace Nerv.IIP.Business.Quality.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》L1 背景历史引擎的 **质量域三期**：计量 / SPC / CAPA。
///
/// 二期（<see cref="WorldHistorySeedService"/>）把「检验 → NCR → 处置 → 关单」这条链铺满了，
/// 但三张表始终是 0 行：计量器具台账、SPC 控制图、CAPA。它们不是「上游还没跑」，
/// 而是从来没有任何 seed 写过——于是演示里「拿什么量的」「控制限是谁定的」
/// 「这个问题后来怎么根治的」三个必答问题全部答不上来。
///
/// 三块的挂靠方式与二期同一手法（确定性纯函数 <see cref="WorldHistoryMetrologySpec"/> 镜像上游事实，
/// 不跨库查询、不建跨 schema 外键）：
/// <list type="bullet">
/// <item>SPC 控制限由与检验实测值**同一公式**推出，因此与 <c>inspection_result_lines</c> 天然不打架；</item>
/// <item>CAPA 的 <c>SourceNcrId</c> 指向本库真实存在的 <c>NCR-2026-####</c> 行；</item>
/// <item>CAPA 的效果验证引用本库真实存在的合格检验记录（领域层强约束：没有合格复检不许关单）。</item>
/// </list>
///
/// 领域事件：CAPA 聚合在开单 / 效果验证 / 关单三处 <c>AddDomainEvent</c>。历史事实不驱动下游，
/// 写入前一律 <c>ClearDomainEvents()</c>（与 MES 侧 <c>DefectRecord</c> 同一惯例）。
/// </summary>
public sealed class WorldHistoryMetrologySeedService(ApplicationDbContext dbContext)
{
    /// <summary>每批写入量。批内共享一次预查与一次 <c>SaveChanges</c>，批末清变更跟踪器。</summary>
    public const int BatchSize = 200;

    private const string CapaCloseApprovalChainPrefix = "APPR-CAPA-";

    public async Task<WorldHistoryMetrologySeedReport> SeedAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        double scale,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);

        var deviceReport = await SeedMeasuringDevicesAsync(organizationId, environmentId, asOfDate, cancellationToken);
        var chartsWritten = await SeedSpcControlChartsAsync(organizationId, environmentId, asOfDate, scale, cancellationToken);
        var capaReport = await SeedCorrectiveActionsAsync(organizationId, environmentId, asOfDate, scale, cancellationToken);

        // fail-closed：台账 / 控制限 / CAPA 引用任何一处对不上，就让 seed（进而让启动）失败。
        var validation = await ValidateAsync(organizationId, environmentId, asOfDate, scale, cancellationToken);

        return new WorldHistoryMetrologySeedReport(
            MeasuringDevicesWritten: deviceReport.Devices,
            CalibrationRecordsWritten: deviceReport.Calibrations,
            SpcControlChartsWritten: chartsWritten,
            CorrectiveActionsWritten: capaReport.Capas,
            CorrectiveActionItemsWritten: capaReport.Items,
            Validation: validation);
    }

    #region 计量器具台账 + 校准记录

    private async Task<(int Devices, int Calibrations)> SeedMeasuringDevicesAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        CancellationToken cancellationToken)
    {
        var facts = WorldHistoryMetrologySpec.BuildMeasuringDeviceFacts(asOfDate);
        var devices = 0;
        var calibrations = 0;

        for (var batchStart = 0; batchStart < facts.Count; batchStart += BatchSize)
        {
            var batch = facts.Skip(batchStart).Take(BatchSize).ToArray();
            var codes = batch.Select(fact => fact.DeviceCode).ToArray();
            var existing = (await dbContext.MeasuringDevices
                    .AsNoTracking()
                    .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId
                        && codes.Contains(x.DeviceCode))
                    .Select(x => x.DeviceCode)
                    .ToArrayAsync(cancellationToken))
                .ToHashSet(StringComparer.Ordinal);

            var added = 0;
            foreach (var fact in batch.Where(fact => !existing.Contains(fact.DeviceCode)))
            {
                var device = MeasuringDevice.Create(
                    organizationId,
                    environmentId,
                    fact.DeviceCode,
                    fact.DeviceType,
                    fact.Accuracy,
                    fact.CalibrationIntervalDays,
                    // 建档锚点是上线之前的那一次检定：电子历史里只留窗口内的证书，
                    // 上线前那次仅作为「导入的初始校准状态」体现在到期日上。
                    fact.InitialCalibratedAtUtc);

                foreach (var calibration in fact.Calibrations)
                {
                    device.RecordCalibration(
                        calibration.CalibrationNo,
                        calibration.CalibratedAtUtc,
                        calibration.CalibrationProvider,
                        calibration.CertificateFileId);
                    calibrations++;
                }

                switch (fact.Lifecycle)
                {
                    case WorldHistoryMeasuringDeviceLifecycle.Disabled:
                        device.Disable();
                        break;
                    case WorldHistoryMeasuringDeviceLifecycle.Retired:
                        device.Retire();
                        break;
                    default:
                        break;
                }

                dbContext.MeasuringDevices.Add(device);
                devices++;
                added++;
            }

            if (added > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            dbContext.ChangeTracker.Clear();
        }

        return (devices, calibrations);
    }

    #endregion

    #region SPC 控制图

    private async Task<int> SeedSpcControlChartsAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        double scale,
        CancellationToken cancellationToken)
    {
        var series = WorldHistoryMetrologySpec.BuildSpcSeries(asOfDate, scale);
        var written = 0;

        for (var batchStart = 0; batchStart < series.Count; batchStart += BatchSize)
        {
            var batch = series.Skip(batchStart).Take(BatchSize).ToArray();
            var skuCodes = batch.Select(x => x.SkuCode).Distinct(StringComparer.Ordinal).ToArray();
            var existing = (await dbContext.SpcControlCharts
                    .AsNoTracking()
                    .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId
                        && skuCodes.Contains(x.SkuCode))
                    .Select(x => new { x.SkuCode, x.CharacteristicCode, x.WorkCenterId, x.SubgroupSize })
                    .ToArrayAsync(cancellationToken))
                .Select(x => ChartKey(x.SkuCode, x.CharacteristicCode, x.WorkCenterId, x.SubgroupSize))
                .ToHashSet(StringComparer.Ordinal);

            var added = 0;
            foreach (var candidate in batch)
            {
                var key = ChartKey(candidate.SkuCode, candidate.CharacteristicCode, candidate.WorkCenterId, candidate.SubgroupSize);
                if (existing.Contains(key))
                {
                    continue;
                }

                var chart = BuildLockedChart(organizationId, environmentId, candidate);
                if (chart is null)
                {
                    continue;
                }

                dbContext.SpcControlCharts.Add(chart.Value.Chart);
                Backdate(chart.Value.Chart, x => x.CreatedAtUtc, chart.Value.CalculatedAtUtc);
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

    /// <summary>
    /// 用试运行期（前 25 个子组）的数据锁定控制限——这是 SPC 的标准做法，
    /// 也是「多数受控、个别失控」这种可讲形状的来源：控制限锁在早期，
    /// 后续工序漂移或不合格件越界时才判得出异常。计算走的是查询侧同一套
    /// <c>SpcCalculation</c>，因此台账里的控制限与图上现算的口径逐位一致。
    /// </summary>
    private static (SpcControlChart Chart, DateTime CalculatedAtUtc)? BuildLockedChart(
        string organizationId,
        string environmentId,
        WorldHistorySpcSeries series)
    {
        var points = series.Measurements
            .Select(measurement => new SpcMeasurementPointResponse(
                measurement.SourceDocumentId,
                measurement.SourceDocumentId,
                measurement.MeasuredAtUtc,
                measurement.MeasuredValue,
                null))
            .ToArray();
        var subgroups = SpcCalculation.BuildSubgroups(points, series.SubgroupSize);
        if (subgroups.Count < WorldHistoryMetrologySpec.SpcTrialSubgroupCount)
        {
            return null;
        }

        var trial = subgroups.Take(WorldHistoryMetrologySpec.SpcTrialSubgroupCount).ToArray();
        var limits = SpcCalculation.CalculateLimits(trial, series.SubgroupSize, locked: true);
        var calculatedAtUtc = trial[^1].EndUtc.UtcDateTime;

        var chart = SpcControlChart.Create(
            organizationId,
            environmentId,
            series.SkuCode,
            series.CharacteristicCode,
            series.WorkCenterId,
            series.SubgroupSize);
        chart.LockLimits(
            limits.CenterLine,
            limits.AverageRange,
            limits.XbarUpperControlLimit,
            limits.XbarLowerControlLimit,
            limits.RangeUpperControlLimit,
            limits.RangeLowerControlLimit,
            calculatedAtUtc);
        return (chart, calculatedAtUtc);
    }

    private static string ChartKey(string skuCode, string characteristicCode, string workCenterId, int subgroupSize) =>
        $"{skuCode}|{characteristicCode}|{workCenterId}|{subgroupSize}";

    #endregion

    #region CAPA

    private async Task<(int Capas, int Items)> SeedCorrectiveActionsAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        double scale,
        CancellationToken cancellationToken)
    {
        var facts = WorldHistoryMetrologySpec.BuildCapaFacts(asOfDate, scale);
        if (facts.Count == 0)
        {
            return (0, 0);
        }

        var ncrIds = await LoadNonconformanceReportIdsAsync(organizationId, environmentId, cancellationToken);
        var verificationRecords = await LoadVerificationRecordsAsync(organizationId, environmentId, cancellationToken);
        var capas = 0;
        var items = 0;

        for (var batchStart = 0; batchStart < facts.Count; batchStart += BatchSize)
        {
            var batch = facts.Skip(batchStart).Take(BatchSize).ToArray();
            var codes = batch.Select(fact => fact.CapaCode).ToArray();
            var existing = (await dbContext.CorrectiveActions
                    .AsNoTracking()
                    .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId
                        && codes.Contains(x.CapaCode))
                    .Select(x => x.CapaCode)
                    .ToArrayAsync(cancellationToken))
                .ToHashSet(StringComparer.Ordinal);

            var added = 0;
            foreach (var fact in batch.Where(fact => !existing.Contains(fact.CapaCode)))
            {
                // 挂不上真实 NCR 的 CAPA 一律不写：宁可少一张，也不能在台账里留一个点不进去的来源单。
                if (!ncrIds.TryGetValue(fact.NcrCode, out var sourceNcrId))
                {
                    continue;
                }

                items += WriteCorrectiveAction(organizationId, environmentId, fact, sourceNcrId, verificationRecords);
                capas++;
                added++;
            }

            if (added > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            dbContext.ChangeTracker.Clear();
        }

        return (capas, items);
    }

    private int WriteCorrectiveAction(
        string organizationId,
        string environmentId,
        WorldHistoryCapaFact fact,
        string sourceNcrId,
        IReadOnlyDictionary<string, IReadOnlyList<(InspectionRecordId RecordId, DateTime CreatedAtUtc)>> verificationRecords)
    {
        var capa = CorrectiveAction.OpenStandalone(
            organizationId,
            environmentId,
            fact.CapaCode,
            fact.RootCause,
            fact.ContainmentAction,
            fact.OwnerUserId,
            fact.DueAtUtc);
        dbContext.CorrectiveActions.Add(capa);

        // OpenStandalone 不接受 NCR 引用（OpenFromNcr 需要整个聚合，跨批加载代价大且无收益），
        // 来源 NCR id 直接落列——列上本来就没有跨聚合外键。
        Backdate(capa, x => x.SourceNcrId, (string?)sourceNcrId);
        Backdate(capa, x => x.CreatedAtUtc, fact.OpenedAtUtc);
        Backdate(capa, x => x.UpdatedAtUtc, fact.OpenedAtUtc);

        foreach (var action in fact.Actions)
        {
            capa.AddAction(action.ActionType, action.Description, action.OwnerUserId, action.DueAtUtc);
        }

        var orderedItems = capa.Actions.ToArray();
        for (var index = 0; index < orderedItems.Length; index++)
        {
            Backdate(orderedItems[index], x => x.CreatedAtUtc, fact.Actions[index].DueAtUtc.AddDays(-3));
        }

        for (var index = 0; index < orderedItems.Length; index++)
        {
            if (fact.Actions[index].CompletedAtUtc is not { } completedAtUtc)
            {
                continue;
            }

            capa.CompleteAction(orderedItems[index].Id, fact.Actions[index].OwnerUserId, completedAtUtc);
        }

        if (fact.EffectivenessVerifiedAtUtc is { } verifiedAtUtc)
        {
            // 领域层强约束：效果验证必须引用一条**已通过**的检验记录。
            // 这里挑本库里该 SKU 在验证时刻之后最早的一条合格记录——它真实存在，页面可点进去。
            var inspectionRecordId = ResolveVerificationRecordId(verificationRecords, fact.SkuCode, verifiedAtUtc);
            if (inspectionRecordId is not null)
            {
                capa.VerifyEffectiveness(
                    fact.VerifiedByUserId!,
                    fact.EffectivenessResult!,
                    verifiedAtUtc,
                    inspectionRecordId,
                    "passed");
                Backdate(capa, x => x.UpdatedAtUtc, verifiedAtUtc);

                if (fact.ClosedAtUtc is { } closedAtUtc)
                {
                    capa.Close(fact.ClosedByUserId!, $"{CapaCloseApprovalChainPrefix}{fact.CapaCode}");
                    Backdate(capa, x => x.ClosedAtUtc, (DateTimeOffset?)closedAtUtc);
                    Backdate(capa, x => x.UpdatedAtUtc, closedAtUtc);
                }
            }
        }

        // 历史回填不重放当时的领域事件：CAPA 开单/验证/关单事件会带着 29 周前的业务事实、
        // 今天的时间戳流向下游（通知、审批、集成事件），把演示环境搅乱。
        capa.ClearDomainEvents();
        return orderedItems.Length;
    }

    private static InspectionRecordId? ResolveVerificationRecordId(
        IReadOnlyDictionary<string, IReadOnlyList<(InspectionRecordId RecordId, DateTime CreatedAtUtc)>> verificationRecords,
        string skuCode,
        DateTimeOffset verifiedAtUtc)
    {
        if (!verificationRecords.TryGetValue(skuCode, out var candidates) || candidates.Count == 0)
        {
            return null;
        }

        // 优先取验证时刻之前最近的一条合格记录（「连续复检合格」的证据），否则退回该 SKU 最新的一条。
        var boundary = verifiedAtUtc.UtcDateTime;
        var before = candidates.LastOrDefault(x => x.CreatedAtUtc <= boundary);
        return before.RecordId ?? candidates[^1].RecordId;
    }

    private async Task<Dictionary<string, string>> LoadNonconformanceReportIdsAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.NonconformanceReports
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .Select(x => new { x.NcrCode, x.Id })
            .ToArrayAsync(cancellationToken);
        return rows
            .GroupBy(x => x.NcrCode, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Id.ToString(), StringComparer.Ordinal);
    }

    private async Task<Dictionary<string, IReadOnlyList<(InspectionRecordId RecordId, DateTime CreatedAtUtc)>>>
        LoadVerificationRecordsAsync(
            string organizationId,
            string environmentId,
            CancellationToken cancellationToken)
    {
        var rows = await dbContext.InspectionRecords
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Result == "passed")
            .Select(x => new { x.SkuCode, x.Id, x.CreatedAtUtc })
            .ToArrayAsync(cancellationToken);

        return rows
            .GroupBy(x => x.SkuCode, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<(InspectionRecordId, DateTime)>)[.. group
                    .OrderBy(x => x.CreatedAtUtc)
                    .Select(x => (x.Id, x.CreatedAtUtc))],
                StringComparer.Ordinal);
    }

    #endregion

    #region 一致性校验（fail-closed）

    private async Task<WorldHistoryMetrologyValidationReport> ValidateAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        double scale,
        CancellationToken cancellationToken)
    {
        var nowUtc = new DateTimeOffset(asOfDate.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc));
        var goLiveUtc = new DateTimeOffset(
            WorldHistoryCalendar.GoLiveDate.ToDateTime(new TimeOnly(0, 0), DateTimeKind.Utc));

        var devices = await dbContext.MeasuringDevices
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId
                && x.DeviceCode.StartsWith("MD-"))
            .ToArrayAsync(cancellationToken);
        var deviceIds = devices.Select(x => x.Id).ToArray();
        var calibrations = await dbContext.CalibrationRecords
            .AsNoTracking()
            .Where(x => deviceIds.Contains(x.MeasuringDeviceId))
            .ToArrayAsync(cancellationToken);

        var expectedDevices = WorldHistoryMetrologySpec.BuildMeasuringDeviceFacts(asOfDate);
        if (devices.Length < expectedDevices.Count)
        {
            throw new WorldHistoryConsistencyException(
                $"计量器具台账缺项：期望 {expectedDevices.Count} 台，实到 {devices.Length} 台。");
        }

        foreach (var device in devices)
        {
            if (device.LastCalibratedAtUtc is not { } lastCalibratedAtUtc)
            {
                throw new WorldHistoryConsistencyException($"{device.DeviceCode} 没有末次校准时刻，台账无法判定有效性。");
            }

            if (device.CalibrationDueAtUtc != lastCalibratedAtUtc.AddDays(device.CalibrationIntervalDays))
            {
                throw new WorldHistoryConsistencyException(
                    $"{device.DeviceCode} 的下次到期日与「末次校准 + 检定周期」不符，计量台账不自洽。");
            }
        }

        foreach (var calibration in calibrations)
        {
            if (calibration.CalibratedAtUtc < goLiveUtc || calibration.CalibratedAtUtc > nowUtc)
            {
                throw new WorldHistoryConsistencyException(
                    $"校准记录 {calibration.CalibrationNo} 落在 [上线日, asOfDate] 之外，电子历史区间被击穿。");
            }
        }

        var overdue = devices.Count(x =>
            x.ComputeCalibrationState(nowUtc, WorldHistoryMetrologySpec.WarningDays)
                == MeasuringDeviceCalibrationStates.Overdue);
        var warning = devices.Count(x =>
            x.ComputeCalibrationState(nowUtc, WorldHistoryMetrologySpec.WarningDays)
                == MeasuringDeviceCalibrationStates.Warning);
        var unavailable = devices.Count(x =>
            x.ComputeCalibrationState(nowUtc, WorldHistoryMetrologySpec.WarningDays)
                == MeasuringDeviceCalibrationStates.Unavailable);
        if (overdue != WorldHistoryMetrologySpec.OverdueDeviceCount
            || warning != WorldHistoryMetrologySpec.WarningDeviceCount
            || unavailable != WorldHistoryMetrologySpec.DisabledDeviceCount + WorldHistoryMetrologySpec.RetiredDeviceCount)
        {
            throw new WorldHistoryConsistencyException(
                $"计量校准状态分布对不上：过期 {overdue}/{WorldHistoryMetrologySpec.OverdueDeviceCount}、"
                + $"临期 {warning}/{WorldHistoryMetrologySpec.WarningDeviceCount}、"
                + $"停用报废 {unavailable}/{WorldHistoryMetrologySpec.DisabledDeviceCount + WorldHistoryMetrologySpec.RetiredDeviceCount}。");
        }

        var charts = await dbContext.SpcControlCharts
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .ToArrayAsync(cancellationToken);
        foreach (var chart in charts.Where(x => x.Locked))
        {
            if (chart.XbarUpperControlLimit <= chart.CenterLine || chart.XbarLowerControlLimit >= chart.CenterLine)
            {
                throw new WorldHistoryConsistencyException(
                    $"SPC 控制图 {chart.SkuCode}/{chart.CharacteristicCode} 的上下控制限没有夹住中心线。");
            }
        }

        var capaFacts = WorldHistoryMetrologySpec.BuildCapaFacts(asOfDate, scale);
        var capas = await dbContext.CorrectiveActions
            .AsNoTracking()
            .Include(x => x.Actions)
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId
                && x.CapaCode.StartsWith("CAPA-2026-"))
            .ToArrayAsync(cancellationToken);
        var ncrIdSet = (await dbContext.NonconformanceReports
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
                .Select(x => x.Id)
                .ToArrayAsync(cancellationToken))
            .Select(id => id.ToString())
            .ToHashSet(StringComparer.Ordinal);

        foreach (var capa in capas)
        {
            if (capa.SourceNcrId is null || !ncrIdSet.Contains(capa.SourceNcrId))
            {
                throw new WorldHistoryConsistencyException($"{capa.CapaCode} 的来源 NCR 在本库不存在，CAPA 台账挂空。");
            }

            if (capa.Status == "closed" && capa.EffectivenessInspectionRecordId is null)
            {
                throw new WorldHistoryConsistencyException($"{capa.CapaCode} 已关单却没有效果验证检验记录。");
            }
        }

        var sample = capaFacts
            .Take(5)
            .Select(WorldHistoryMetrologySpec.Describe)
            .ToArray();

        return new WorldHistoryMetrologyValidationReport(
            MeasuringDevicesChecked: devices.Length,
            CalibrationRecordsChecked: calibrations.Length,
            OverdueDevices: overdue,
            WarningDevices: warning,
            UnavailableDevices: unavailable,
            SpcControlChartsChecked: charts.Length,
            CorrectiveActionsChecked: capas.Length,
            CorrectiveActionItemsChecked: capas.Sum(x => x.Actions.Count),
            ClosedCorrectiveActions: capas.Count(x => x.Status == "closed"),
            OverdueCorrectiveActions: capas.Count(x => x.Status != "closed" && x.DueAtUtc < nowUtc),
            Sample: sample);
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
}

/// <summary>一次 L1 计量 / SPC / CAPA 历史生成的产出摘要。</summary>
public sealed record WorldHistoryMetrologySeedReport(
    int MeasuringDevicesWritten,
    int CalibrationRecordsWritten,
    int SpcControlChartsWritten,
    int CorrectiveActionsWritten,
    int CorrectiveActionItemsWritten,
    WorldHistoryMetrologyValidationReport Validation);

/// <summary>一致性校验摘要（启动日志据此打印，演示前可肉眼核账）。</summary>
public sealed record WorldHistoryMetrologyValidationReport(
    int MeasuringDevicesChecked,
    int CalibrationRecordsChecked,
    int OverdueDevices,
    int WarningDevices,
    int UnavailableDevices,
    int SpcControlChartsChecked,
    int CorrectiveActionsChecked,
    int CorrectiveActionItemsChecked,
    int ClosedCorrectiveActions,
    int OverdueCorrectiveActions,
    IReadOnlyCollection<string> Sample);
