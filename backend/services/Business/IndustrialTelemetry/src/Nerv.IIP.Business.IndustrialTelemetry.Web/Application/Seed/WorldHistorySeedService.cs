using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.AlarmEventAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.AlarmRuleAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.DeviceStateSnapshotAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.OeeProductionFactAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetryRawSampleAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetryRollupAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetrySummaryAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Infrastructure;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》L1 背景历史引擎 **IndustrialTelemetry 侧（三期）**：
/// 2026-01-05 上线至今 29 周的设备遥测/报警/状态/OEE 历史。
///
/// 数据分层取舍（§7「分钟级聚合回填 29 周 + 近 7 天原始级」的体积落地）：
/// - 全历史（上线日 → 截止日）：**日级** <c>telemetry_rollups(Daily)</c>，长趋势图的数据源；
/// - 近 7 天：**小时级** rollup + **15 分钟桶**的 <c>telemetry_raw_samples</c>；
/// - 近 24 小时：**5 分钟桶**的 <c>telemetry_summaries</c>（ingest 面/健康评分读的表）。
/// L3 常驻模拟从截止日 00:00 UTC 起继续以实时样本续写，两段无断层。
///
/// MAN-519 修订四条款的落地与一期 ERP/MES 相同：历史时间戳（变更跟踪器改写）、独立号段
/// （<c>WH-*</c> 规则 / <c>MWO-2026-*</c>）、fail-closed 校验器、按 sourceSequence/编号幂等。
/// 批量写入走 <c>SaveChangesAsync</c>（不派发领域事件），避免十万级样本触发报警/健康事件风暴；
/// 报警事实由本引擎直写（与 Maintenance 侧共享 <see cref="WorldHistoryDeviceSpec.BuildAlarmPlans"/>），
/// 不经过规则评估管道——历史样本因 <c>HasNewerSummaryAsync</c> 短路也不会被在线评估误触发。
/// </summary>
public sealed class WorldHistorySeedService(ApplicationDbContext dbContext)
{
    /// <summary>历史近段的粒度参数（体积主旋钮，实测行数写入 PR）。</summary>
    public const int RecentRawDays = 7;
    public const int RawBucketMinutes = 15;
    public const int RecentSummaryHours = 24;
    public const int SummaryBucketMinutes = 5;

    /// <summary>批量写入批宽：批末 SaveChanges + 清变更跟踪器（一期同款）。</summary>
    public const int BatchSize = 2000;

    private const string SequencePrefix = "seed:world-history";

    private int pendingWrites;

    public async Task<WorldHistoryDeviceSeedReport> SeedAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        double scale,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        var alarmPlans = WorldHistoryDeviceSpec.BuildAlarmPlans(asOfDate, scale);

        var rulesWritten = await SeedAlarmRulesAsync(organizationId, environmentId, cancellationToken);
        var alarmsWritten = await SeedAlarmEventsAsync(organizationId, environmentId, alarmPlans, cancellationToken);
        var telemetryWritten = await SeedTelemetryAsync(organizationId, environmentId, asOfDate, alarmPlans, cancellationToken);
        var statesWritten = await SeedDeviceStatesAsync(organizationId, environmentId, asOfDate, alarmPlans, cancellationToken);
        var oeeFactsWritten = await SeedOeeFactsAsync(organizationId, environmentId, asOfDate, alarmPlans, cancellationToken);

        var validation = await new WorldHistoryConsistencyValidator(dbContext)
            .ValidateAsync(organizationId, environmentId, asOfDate, scale, cancellationToken);

        return new WorldHistoryDeviceSeedReport(
            rulesWritten,
            alarmsWritten,
            telemetryWritten.DailyRollups,
            telemetryWritten.HourlyRollups,
            telemetryWritten.RawSamples,
            telemetryWritten.Summaries,
            statesWritten,
            oeeFactsWritten,
            validation);
    }

    private async Task<int> SeedAlarmRulesAsync(string organizationId, string environmentId, CancellationToken cancellationToken)
    {
        var existing = await dbContext.AlarmRules
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .Where(x => x.RuleCode.StartsWith("WH-"))
            .Select(x => x.RuleCode)
            .ToArrayAsync(cancellationToken);
        var existingSet = existing.ToHashSet(StringComparer.Ordinal);

        var written = 0;
        foreach (var device in WorldHistoryDeviceSpec.Devices)
        {
            foreach (var tag in device.Class.Tags.Where(x => x.HasAlarmRule))
            {
                var ruleCode = WorldHistoryDeviceSpec.RuleCode(device.DeviceAssetId, tag.TagKey);
                if (existingSet.Contains(ruleCode))
                {
                    continue;
                }

                var rule = AlarmRule.Configure(
                    organizationId,
                    environmentId,
                    device.DeviceAssetId,
                    ruleCode,
                    WorldHistoryDeviceSpec.AlarmCode(tag),
                    tag.AlarmSeverity,
                    tag.TagKey,
                    tag.ComparisonOperator,
                    tag.AlarmThreshold,
                    tag.UnitCode,
                    isEnabled: true,
                    deadbandValue: tag.NoiseBand,
                    onDelaySeconds: 4,
                    offDelaySeconds: 4,
                    minDurationSeconds: 4,
                    priority: tag.AlarmSeverity);
                dbContext.AlarmRules.Add(rule);
                BackdateOffset(rule, x => x.CreatedAtUtc, GoLiveUtc);
                BackdateOffset(rule, x => x.UpdatedAtUtc, GoLiveUtc);
                written++;
            }
        }

        await FlushAsync(cancellationToken, force: true);
        return written;
    }

    private async Task<int> SeedAlarmEventsAsync(
        string organizationId,
        string environmentId,
        IReadOnlyList<WorldHistoryAlarmPlan> alarmPlans,
        CancellationToken cancellationToken)
    {
        // 回填号段是 `{RuleCode}:{ordinal}`（带冒号）；运行时规则评估触发的报警 externalAlarmId
        // 是纯 RuleCode（同为 WH- 前缀），必须靠冒号区分，否则 L3 实时流会污染幂等与校验计数。
        var existing = await dbContext.AlarmEvents
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .Where(x => x.ExternalAlarmId.StartsWith("WH-") && x.ExternalAlarmId.Contains(":"))
            .Select(x => x.ExternalAlarmId)
            .ToArrayAsync(cancellationToken);
        var existingSet = existing.ToHashSet(StringComparer.Ordinal);

        // 截止日随启动日推进：上次 seed 留在 raised 态的尾部报警，若本次计划已判定应清除，
        // 在这里追平（幂等 catch-up），否则重复启动时校验器会 fail-closed。
        var shouldBeCleared = alarmPlans
            .Where(plan => !plan.IsOpenAtAsOf && existingSet.Contains(plan.ExternalAlarmId))
            .ToDictionary(x => x.ExternalAlarmId, StringComparer.Ordinal);
        if (shouldBeCleared.Count > 0)
        {
            var externalIds = shouldBeCleared.Keys.ToArray();
            var staleOpen = await dbContext.AlarmEvents
                .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
                .Where(x => externalIds.Contains(x.ExternalAlarmId) && x.Status != "cleared")
                .ToArrayAsync(cancellationToken);
            foreach (var alarm in staleOpen)
            {
                alarm.Clear(shouldBeCleared[alarm.ExternalAlarmId].ClearedAtUtc, "system:industrial-telemetry", "return-to-normal");
            }

            await FlushAsync(cancellationToken, force: true);
        }

        var written = 0;
        foreach (var plan in alarmPlans.Where(plan => !existingSet.Contains(plan.ExternalAlarmId)))
        {
            var alarm = AlarmEvent.Raise(
                organizationId,
                environmentId,
                plan.DeviceAssetId,
                plan.AlarmCode,
                plan.Severity,
                plan.RaisedAtUtc,
                plan.ExternalAlarmId,
                priority: plan.Severity,
                tagKey: plan.TagKey,
                observedValue: plan.ObservedValue,
                thresholdValue: plan.ThresholdValue,
                unitCode: plan.UnitCode);
            if (!plan.IsOpenAtAsOf)
            {
                alarm.Clear(plan.ClearedAtUtc, "system:industrial-telemetry", "return-to-normal");
            }

            dbContext.AlarmEvents.Add(alarm);
            BackdateOffset(alarm, x => x.RecordedAtUtc, plan.RaisedAtUtc);
            written++;
            await FlushAsync(cancellationToken);
        }

        await FlushAsync(cancellationToken, force: true);
        return written;
    }

    private async Task<TelemetryWriteCounts> SeedTelemetryAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        IReadOnlyList<WorldHistoryAlarmPlan> alarmPlans,
        CancellationToken cancellationToken)
    {
        var counts = new TelemetryWriteCounts();
        var historyEndUtc = StartOfDayUtc(asOfDate);
        var recentStartUtc = historyEndUtc.AddDays(-RecentRawDays);
        var summaryStartUtc = historyEndUtc.AddHours(-RecentSummaryHours);
        var alarmWindows = alarmPlans
            .GroupBy(x => (x.DeviceAssetId, x.TagKey))
            .ToDictionary(x => x.Key, x => x.ToArray());

        foreach (var device in WorldHistoryDeviceSpec.Devices)
        {
            foreach (var tag in device.Class.Tags)
            {
                alarmWindows.TryGetValue((device.DeviceAssetId, tag.TagKey), out var tagAlarms);

                counts.DailyRollups += await SeedRollupGrainAsync(
                    organizationId, environmentId, device, tag, TelemetryRollupGrain.Daily,
                    GoLiveUtc, historyEndUtc, TimeSpan.FromDays(1), tagAlarms, cancellationToken);
                counts.HourlyRollups += await SeedRollupGrainAsync(
                    organizationId, environmentId, device, tag, TelemetryRollupGrain.Hourly,
                    recentStartUtc, historyEndUtc, TimeSpan.FromHours(1), tagAlarms, cancellationToken);
                counts.RawSamples += await SeedRawSamplesAsync(
                    organizationId, environmentId, device, tag, recentStartUtc, historyEndUtc, tagAlarms, cancellationToken);
                counts.Summaries += await SeedSummariesAsync(
                    organizationId, environmentId, device, tag, summaryStartUtc, historyEndUtc, tagAlarms, cancellationToken);
            }
        }

        await FlushAsync(cancellationToken, force: true);
        return counts;
    }

    private async Task<int> SeedRollupGrainAsync(
        string organizationId,
        string environmentId,
        WorldHistoryDeviceProfile device,
        WorldHistoryTagBehavior tag,
        TelemetryRollupGrain grain,
        DateTimeOffset rangeStartUtc,
        DateTimeOffset rangeEndUtc,
        TimeSpan windowLength,
        WorldHistoryAlarmPlan[]? tagAlarms,
        CancellationToken cancellationToken)
    {
        var existingStarts = await dbContext.TelemetryRollups
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .Where(x => x.DeviceAssetId == device.DeviceAssetId && x.TagKey == tag.TagKey && x.Grain == grain)
            .Where(x => x.WindowStartUtc >= rangeStartUtc && x.WindowStartUtc < rangeEndUtc)
            .Select(x => x.WindowStartUtc)
            .ToArrayAsync(cancellationToken);
        var existingSet = existingStarts.ToHashSet();

        var grainKey = grain == TelemetryRollupGrain.Daily ? "daily" : "hourly";
        var written = 0;
        for (var windowStart = rangeStartUtc; windowStart < rangeEndUtc; windowStart += windowLength)
        {
            var windowEnd = windowStart + windowLength;
            if (existingSet.Contains(windowStart)
                || WorldHistoryDeviceSpec.ActiveMinutes(device, windowStart, windowEnd) == 0)
            {
                continue;
            }

            var shape = WorldHistoryDeviceSpec.Synthesize(
                device, tag, windowStart, windowEnd, AlarmObservedValue(tagAlarms, windowStart, windowEnd));
            var rollup = TelemetryRollup.Record(
                organizationId,
                environmentId,
                device.DeviceAssetId,
                tag.TagKey,
                grain,
                windowStart,
                windowEnd,
                shape.SampleCount,
                shape.MinValue,
                shape.MaxValue,
                shape.AverageValue,
                shape.FirstValue,
                shape.LastValue,
                $"{SequencePrefix}:{grainKey}:{device.DeviceAssetId}:{tag.TagKey}:{windowStart.ToUnixTimeMilliseconds()}");
            dbContext.TelemetryRollups.Add(rollup);
            BackdateOffset(rollup, x => x.RolledUpAtUtc, windowEnd);
            written++;
            await FlushAsync(cancellationToken);
        }

        return written;
    }

    private async Task<int> SeedRawSamplesAsync(
        string organizationId,
        string environmentId,
        WorldHistoryDeviceProfile device,
        WorldHistoryTagBehavior tag,
        DateTimeOffset rangeStartUtc,
        DateTimeOffset rangeEndUtc,
        WorldHistoryAlarmPlan[]? tagAlarms,
        CancellationToken cancellationToken)
    {
        var existingStarts = await dbContext.TelemetryRawSamples
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .Where(x => x.DeviceAssetId == device.DeviceAssetId && x.TagKey == tag.TagKey)
            .Where(x => x.SourceSequence.StartsWith(SequencePrefix))
            .Where(x => x.BucketStartUtc >= rangeStartUtc && x.BucketStartUtc < rangeEndUtc)
            .Select(x => x.BucketStartUtc)
            .ToArrayAsync(cancellationToken);
        var existingSet = existingStarts.ToHashSet();

        var written = 0;
        var bucket = TimeSpan.FromMinutes(RawBucketMinutes);
        for (var bucketStart = rangeStartUtc; bucketStart < rangeEndUtc; bucketStart += bucket)
        {
            var bucketEnd = bucketStart + bucket;
            if (existingSet.Contains(bucketStart)
                || WorldHistoryDeviceSpec.ActiveMinutes(device, bucketStart, bucketEnd) == 0)
            {
                continue;
            }

            var shape = WorldHistoryDeviceSpec.Synthesize(
                device, tag, bucketStart, bucketEnd, AlarmObservedValue(tagAlarms, bucketStart, bucketEnd));
            var raw = TelemetryRawSample.Record(
                organizationId,
                environmentId,
                device.DeviceAssetId,
                tag.TagKey,
                bucketStart,
                bucketEnd,
                shape.SampleCount,
                shape.MinValue,
                shape.MaxValue,
                shape.AverageValue,
                shape.FirstValue,
                shape.LastValue,
                $"{SequencePrefix}:raw:{device.DeviceAssetId}:{tag.TagKey}:{bucketStart.ToUnixTimeMilliseconds()}",
                WorldHistoryDeviceSpec.SourceSystem,
                device.Class.CollectionConnectorId,
                device.Class.CollectionConnectorId);
            dbContext.TelemetryRawSamples.Add(raw);
            BackdateOffset(raw, x => x.RecordedAtUtc, bucketEnd);
            written++;
            await FlushAsync(cancellationToken);
        }

        return written;
    }

    private async Task<int> SeedSummariesAsync(
        string organizationId,
        string environmentId,
        WorldHistoryDeviceProfile device,
        WorldHistoryTagBehavior tag,
        DateTimeOffset rangeStartUtc,
        DateTimeOffset rangeEndUtc,
        WorldHistoryAlarmPlan[]? tagAlarms,
        CancellationToken cancellationToken)
    {
        var existingStarts = await dbContext.TelemetrySummaries
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .Where(x => x.DeviceAssetId == device.DeviceAssetId && x.TagKey == tag.TagKey)
            .Where(x => x.SourceSequence != null && x.SourceSequence.StartsWith(SequencePrefix))
            .Where(x => x.BucketStartUtc >= rangeStartUtc && x.BucketStartUtc < rangeEndUtc)
            .Select(x => x.BucketStartUtc)
            .ToArrayAsync(cancellationToken);
        var existingSet = existingStarts.ToHashSet();

        var written = 0;
        var bucket = TimeSpan.FromMinutes(SummaryBucketMinutes);
        for (var bucketStart = rangeStartUtc; bucketStart < rangeEndUtc; bucketStart += bucket)
        {
            var bucketEnd = bucketStart + bucket;
            if (existingSet.Contains(bucketStart)
                || WorldHistoryDeviceSpec.ActiveMinutes(device, bucketStart, bucketEnd) == 0)
            {
                continue;
            }

            var shape = WorldHistoryDeviceSpec.Synthesize(
                device, tag, bucketStart, bucketEnd, AlarmObservedValue(tagAlarms, bucketStart, bucketEnd));
            var summary = TelemetrySummary.Record(
                organizationId,
                environmentId,
                device.DeviceAssetId,
                tag.TagKey,
                bucketStart,
                bucketEnd,
                shape.SampleCount,
                shape.MinValue,
                shape.MaxValue,
                shape.AverageValue,
                $"{SequencePrefix}:summary:{device.DeviceAssetId}:{tag.TagKey}:{bucketStart.ToUnixTimeMilliseconds()}",
                WorldHistoryDeviceSpec.SourceSystem,
                device.Class.CollectionConnectorId,
                device.Class.CollectionConnectorId);
            dbContext.TelemetrySummaries.Add(summary);
            BackdateOffset(summary, x => x.RecordedAtUtc, bucketEnd);
            written++;
            await FlushAsync(cancellationToken);
        }

        return written;
    }

    private async Task<int> SeedDeviceStatesAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        IReadOnlyList<WorldHistoryAlarmPlan> alarmPlans,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.DeviceStateSnapshots
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .Where(x => x.SourceSequence.StartsWith(SequencePrefix))
            .Select(x => x.SourceSequence)
            .ToArrayAsync(cancellationToken);
        var existingSet = existing.ToHashSet(StringComparer.Ordinal);
        var downtimeAlarms = alarmPlans
            .Where(x => x.HasWorkOrder)
            .GroupBy(x => x.DeviceAssetId)
            .ToDictionary(x => x.Key, x => x.ToArray());

        var written = 0;
        foreach (var device in WorldHistoryDeviceSpec.Devices)
        {
            var moments = new List<(DateTimeOffset OccurredAtUtc, string State)>();
            if (device.Class.RunsContinuously)
            {
                moments.Add((GoLiveUtc, "running"));
            }
            else
            {
                for (var day = WorldHistoryCalendar.GoLiveDate; day < asOfDate; day = day.AddDays(1))
                {
                    var intervals = WorldHistoryDeviceSpec.ActiveIntervals(device, day);
                    if (intervals.Count == 0)
                    {
                        continue;
                    }

                    moments.Add((intervals[0].StartUtc, "running"));
                    moments.Add((intervals[^1].EndUtc, "planned-down"));
                }
            }

            if (downtimeAlarms.TryGetValue(device.DeviceAssetId, out var deviceAlarms))
            {
                foreach (var alarm in deviceAlarms)
                {
                    moments.Add((alarm.RaisedAtUtc, "faulted"));
                    if (!alarm.IsOpenAtAsOf)
                    {
                        moments.Add((alarm.ClearedAtUtc, "running"));
                    }
                }
            }

            foreach (var (occurredAtUtc, state) in moments.OrderBy(x => x.OccurredAtUtc))
            {
                var sourceSequence = $"{SequencePrefix}:state:{device.DeviceAssetId}:{occurredAtUtc.ToUnixTimeMilliseconds()}";
                if (existingSet.Contains(sourceSequence))
                {
                    continue;
                }

                var snapshot = DeviceStateSnapshot.Record(
                    organizationId,
                    environmentId,
                    device.DeviceAssetId,
                    state,
                    occurredAtUtc,
                    sourceSequence,
                    WorldHistoryDeviceSpec.SourceSystem,
                    device.Class.CollectionConnectorId,
                    raiseChangedEvent: false);
                dbContext.DeviceStateSnapshots.Add(snapshot);
                BackdateOffset(snapshot, x => x.RecordedAtUtc, occurredAtUtc);
                written++;
                await FlushAsync(cancellationToken);
            }
        }

        await FlushAsync(cancellationToken, force: true);
        return written;
    }

    private async Task<int> SeedOeeFactsAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        IReadOnlyList<WorldHistoryAlarmPlan> alarmPlans,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.OeeProductionFacts
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .Where(x => x.SourceReportNo.StartsWith("RPT-WH-"))
            .Select(x => x.SourceReportNo)
            .ToArrayAsync(cancellationToken);
        var existingSet = existing.ToHashSet(StringComparer.Ordinal);
        var downtimeByDevice = alarmPlans
            .Where(x => x.HasWorkOrder)
            .GroupBy(x => x.DeviceAssetId)
            .ToDictionary(x => x.Key, x => x.ToArray());

        var written = 0;
        foreach (var device in WorldHistoryDeviceSpec.Devices.Where(x => x.Class.TheoreticalRatePerHour is not null))
        {
            var theoreticalRate = device.Class.TheoreticalRatePerHour!.Value;
            downtimeByDevice.TryGetValue(device.DeviceAssetId, out var deviceAlarms);
            for (var day = WorldHistoryCalendar.GoLiveDate; day < asOfDate; day = day.AddDays(1))
            {
                foreach (var shiftIndex in WorldHistoryDeviceSpec.ActiveShifts(device, day).Where(x => x <= 1))
                {
                    var sourceReportNo = $"RPT-WH-{device.DeviceAssetId}-{day:yyyyMMdd}-S{shiftIndex}";
                    if (existingSet.Contains(sourceReportNo))
                    {
                        continue;
                    }

                    var shiftStartUtc = WorldHistoryCalendar.ShiftMoment(day, shiftIndex, 0);
                    var shiftEndUtc = shiftStartUtc.AddHours(WorldHistoryCalendar.ShiftLengthHours);
                    var downtimeMinutes = DowntimeMinutesWithin(deviceAlarms, shiftStartUtc, shiftEndUtc);
                    var productiveHours = Math.Max(0m, (decimal)(WorldHistoryCalendar.ShiftLengthHours * 60 - downtimeMinutes) / 60m);
                    var random = new WorldHistoryRandom($"oee:{device.DeviceAssetId}:{day:yyyy-MM-dd}:{shiftIndex}");
                    var performance = 0.86m + (decimal)random.NextDouble() * 0.10m;
                    var surge = WorldHistoryCalendar.IsMonthEndSurge(day) ? 1.04m : 1.00m;
                    var output = decimal.Round(theoreticalRate * productiveHours * performance * surge, 0, MidpointRounding.AwayFromZero);
                    if (output <= 0m)
                    {
                        continue;
                    }

                    var scrap = random.Chance(0.30)
                        ? decimal.Round(output * (random.NextInt(1, 4) / 100m), 0, MidpointRounding.AwayFromZero)
                        : 0m;
                    var rework = random.Chance(0.10) ? Math.Max(1m, decimal.Round(output * 0.01m, 0)) : 0m;
                    var good = Math.Max(0m, output - scrap - rework);

                    var fact = OeeProductionFact.Project(
                        organizationId,
                        environmentId,
                        sourceReportNo,
                        WorldHistoryDeviceSpec.WorkCenterCode(device),
                        device.DeviceAssetId,
                        good,
                        scrap,
                        rework,
                        "pcs",
                        theoreticalRate,
                        shiftEndUtc);
                    dbContext.OeeProductionFacts.Add(fact);
                    written++;
                    await FlushAsync(cancellationToken);
                }
            }
        }

        await FlushAsync(cancellationToken, force: true);
        return written;
    }

    private static int DowntimeMinutesWithin(
        WorldHistoryAlarmPlan[]? deviceAlarms,
        DateTimeOffset windowStartUtc,
        DateTimeOffset windowEndUtc)
    {
        if (deviceAlarms is null)
        {
            return 0;
        }

        var minutes = 0d;
        foreach (var alarm in deviceAlarms)
        {
            var overlapStart = alarm.RaisedAtUtc > windowStartUtc ? alarm.RaisedAtUtc : windowStartUtc;
            var overlapEnd = alarm.ClearedAtUtc < windowEndUtc ? alarm.ClearedAtUtc : windowEndUtc;
            if (overlapEnd > overlapStart)
            {
                minutes += (overlapEnd - overlapStart).TotalMinutes;
            }
        }

        return (int)Math.Round(minutes, MidpointRounding.AwayFromZero);
    }

    private static decimal? AlarmObservedValue(
        WorldHistoryAlarmPlan[]? tagAlarms,
        DateTimeOffset windowStartUtc,
        DateTimeOffset windowEndUtc)
    {
        if (tagAlarms is null)
        {
            return null;
        }

        foreach (var alarm in tagAlarms)
        {
            if (alarm.RaisedAtUtc < windowEndUtc && alarm.ClearedAtUtc > windowStartUtc)
            {
                return alarm.ObservedValue;
            }
        }

        return null;
    }

    private static DateTimeOffset GoLiveUtc => StartOfDayUtc(WorldHistoryCalendar.GoLiveDate);

    private static DateTimeOffset StartOfDayUtc(DateOnly date) =>
        new(date, TimeOnly.MinValue, TimeSpan.Zero);

    /// <summary>
    /// 把聚合构造函数写死的 <c>DateTimeOffset.UtcNow</c> 改写为历史时刻（一期 Backdate 家族的
    /// DateTimeOffset 变体；IIoT 侧时间戳列全部是 <c>DateTimeOffset</c>）。
    /// </summary>
    private void BackdateOffset<TEntity>(
        TEntity entity,
        System.Linq.Expressions.Expression<Func<TEntity, DateTimeOffset>> property,
        DateTimeOffset value)
        where TEntity : class
    {
        dbContext.Entry(entity).Property(property).CurrentValue = value;
    }

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

    private sealed class TelemetryWriteCounts
    {
        public int DailyRollups { get; set; }
        public int HourlyRollups { get; set; }
        public int RawSamples { get; set; }
        public int Summaries { get; set; }
    }
}

/// <summary>一次 L1 设备域历史生成（IndustrialTelemetry 侧）的产出摘要。</summary>
public sealed record WorldHistoryDeviceSeedReport(
    int AlarmRulesWritten,
    int AlarmEventsWritten,
    int DailyRollupsWritten,
    int HourlyRollupsWritten,
    int RawSamplesWritten,
    int SummariesWritten,
    int DeviceStateSnapshotsWritten,
    int OeeFactsWritten,
    WorldHistoryDeviceValidationReport Validation);
