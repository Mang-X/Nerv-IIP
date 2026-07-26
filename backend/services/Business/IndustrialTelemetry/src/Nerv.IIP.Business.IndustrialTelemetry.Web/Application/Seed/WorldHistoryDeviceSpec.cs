namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》§3/§7 设备域（L1 三期）的**跨服务共享形状**：46 台设备的遥测行为、
/// 报警计划（约 400 起）、维修工单计划（MWO-2026-####，约 120 张）与点检/保养计划。
///
/// IndustrialTelemetry 与 Maintenance 两侧按同一字面量重复声明本类型（与一期 ERP/MES 的
/// <c>WorldHistorySpec</c> 策略相同），各自的黄金向量测试锁定不漂移：
/// - IndustrialTelemetry 依据它写遥测聚合、报警事件、设备状态与 OEE 产量事实；
/// - Maintenance 依据它写维修工单（含停机原因/工时）、点检保养计划与点检记录。
/// 报警数 / 停机时长因此在两个服务间**按构造一致**，校验器在各自侧 fail-closed 复核。
///
/// 设备/点位/连接器字面量与 L0（IndustrialTelemetry <c>WorldBibleSpec</c>、MasterData 侧 §3）
/// 逐一对应；本类型不新建任何主数据。
/// </summary>
public static class WorldHistoryDeviceSpec
{
    public const string SourceSystem = "seed-world-history";
    public const string SeedActor = "seed:world-history";

    public const string OpcUaConnectorId = "CONN-OPCUA-01";
    public const string ModbusConnectorId = "CONN-MODBUS-01";
    public const string MqttConnectorId = "CONN-MQTT-01";

    /// <summary>报警密度参数：46 台 × 29 周 × 约 0.288 起/台周 ≈ 385 起（设定集 §7「约 400 起」）。</summary>
    public const double WeeklyAlarmProbability = 0.24;
    public const double SecondAlarmProbability = 0.20;

    /// <summary>报警转维修工单概率：critical 0.55 / warning 0.18 ≈ 报警总量的 30% ≈ 120 张（§7）。</summary>
    public const double CriticalWorkOrderProbability = 0.55;
    public const double WarningWorkOrderProbability = 0.18;

    /// <summary>历史截止日前最后 N 天内触发的报警保持 raised 态（工作台「设备预警」卡的历史底座）。</summary>
    public const int OpenAlarmTailDays = 2;

    /// <summary>点检员（L0 §5 设备部 EMP-047）与维修技师（EMP-043..046）。</summary>
    public const string InspectorUserId = "user-emp-047";
    public static readonly string[] TechnicianUserIds =
        ["user-emp-043", "user-emp-044", "user-emp-045", "user-emp-046"];

    /// <summary>Maintenance 停机原因目录（回填工单引用；ReasonCategory/LossCategory 小写）。</summary>
    public static readonly WorldHistoryDowntimeReason[] DowntimeReasons =
    [
        new("DT-MECH", "机械故障（轴承/传动/密封）", "breakdown", "availability"),
        new("DT-ELEC", "电气故障（驱动/传感/线路）", "breakdown", "availability"),
        new("DT-TOOL", "刀具/工装异常", "breakdown", "availability"),
        new("DT-PROC", "工艺参数异常（槽液/压力）", "process", "availability"),
        new("DT-PM", "计划保养", "planned", "planned"),
    ];

    /// <summary>
    /// 8 个设备类别的遥测行为（编码段/台数/连接器与 L0 §3 同字面量）。
    /// 正常波形上界（RunningBase + RunningSwing + NoiseBand）恒低于报警阈值，
    /// 报警窗口内的越限值由 <see cref="WorldHistoryAlarmPlan.ObservedValue"/> 显式给出。
    /// </summary>
    public static readonly WorldHistoryDeviceClassSpec[] DeviceClasses =
    [
        new("DEV-CNC-", 10, OpcUaConnectorId, WorldHistoryDeviceWorkshop.Machining, false, 12m, "high",
            [
                new("spindle-temperature", "degC", false, 24m, 52m, 9m, 1.5m, ">", 78m, "warning"),
                new("vibration", "mm/s", false, 0.2m, 2.6m, 0.9m, 0.3m, ">", 6.5m, "critical"),
                new("spindle-speed", "rpm", false, 0m, 2400m, 500m, 60m, ">", 3600m, "warning"),
            ]),
        new("DEV-GRD-", 4, OpcUaConnectorId, WorldHistoryDeviceWorkshop.Machining, false, 20m, "high",
            [
                new("vibration", "mm/s", false, 0.2m, 2.4m, 0.8m, 0.3m, ">", 5.5m, "critical"),
                new("wheel-speed", "rpm", false, 0m, 1500m, 160m, 40m, ">", 1900m, "warning"),
            ]),
        new("DEV-WLD-", 3, OpcUaConnectorId, WorldHistoryDeviceWorkshop.Machining, false, 25m, "high",
            [
                new("weld-current", "A", false, 0m, 185m, 35m, 10m, ">", 280m, "warning"),
                new("temperature", "degC", false, 22m, 56m, 9m, 2m, ">", 85m, "critical"),
            ]),
        new("DEV-ASM-", 12, MqttConnectorId, WorldHistoryDeviceWorkshop.Assembly, false, 30m, "medium",
            [
                new("press-force", "kN", false, 0m, 12.5m, 2.5m, 0.8m, ">", 17.5m, "warning"),
                new("cycle-count", "count", true, 0m, 28m, 6m, 0m, ">", 0m, "warning", HasAlarmRule: false),
            ]),
        new("DEV-TST-", 4, MqttConnectorId, WorldHistoryDeviceWorkshop.Surface, false, 45m, "high",
            [
                new("damping-force", "N", false, 0m, 980m, 140m, 45m, ">", 1450m, "warning"),
            ]),
        new("DEV-CTG-", 3, ModbusConnectorId, WorldHistoryDeviceWorkshop.Surface, false, 40m, "high",
            [
                new("bath-temperature", "degC", false, 24m, 29m, 2m, 0.6m, ">", 34m, "critical"),
                new("bath-ph", "pH", false, 6.2m, 6.2m, 0.25m, 0.1m, "<", 5.6m, "warning"),
            ]),
        new("DEV-PKG-", 2, ModbusConnectorId, WorldHistoryDeviceWorkshop.Surface, false, 60m, "medium",
            [
                new("cycle-count", "count", true, 0m, 55m, 10m, 0m, ">", 0m, "warning", HasAlarmRule: false),
            ]),
        new("DEV-AUX-", 8, ModbusConnectorId, WorldHistoryDeviceWorkshop.Auxiliary, true, null, "high",
            [
                new("air-pressure", "bar", false, 6.9m, 7.2m, 0.35m, 0.15m, "<", 6.0m, "warning"),
                new("temperature", "degC", false, 30m, 66m, 7m, 2m, ">", 92m, "critical"),
            ]),
    ];

    /// <summary>46 台设备展开（编码 = 类别前缀 + 两位序号，与 L0 §3 同公式）。</summary>
    public static readonly IReadOnlyList<WorldHistoryDeviceProfile> Devices = BuildDevices();

    private static IReadOnlyList<WorldHistoryDeviceProfile> BuildDevices()
    {
        var devices = new List<WorldHistoryDeviceProfile>(46);
        foreach (var deviceClass in DeviceClasses)
        {
            for (var index = 1; index <= deviceClass.DeviceCount; index++)
            {
                devices.Add(new WorldHistoryDeviceProfile($"{deviceClass.CodePrefix}{index:D2}", index, deviceClass));
            }
        }

        return devices;
    }

    /// <summary>
    /// 设备 → 工作中心归属（与 MasterData 侧 L0 <c>WorldBibleSpec.Devices</c> 同一字面量表，
    /// OEE 产量事实的 <c>WorkCenterId</c> 引用点）。
    /// </summary>
    public static string WorkCenterCode(WorldHistoryDeviceProfile device) => device.Class.CodePrefix switch
    {
        "DEV-CNC-" => device.OrdinalInClass <= 6
            ? $"WC-ROD-{((device.OrdinalInClass - 1) / 3) + 1:D2}"
            : $"WC-TUB-{((device.OrdinalInClass - 7) / 2) + 1:D2}",
        "DEV-GRD-" => "WC-GRD-01",
        "DEV-WLD-" => device.OrdinalInClass <= 2 ? "WC-TUB-01" : "WC-TUB-02",
        "DEV-ASM-" => device.OrdinalInClass switch
        {
            <= 6 => $"WC-FA-{((device.OrdinalInClass - 1) / 2) + 1:D2}",
            <= 10 => $"WC-RA-{((device.OrdinalInClass - 7) / 2) + 1:D2}",
            _ => "WC-VA-01",
        },
        "DEV-TST-" => "WC-TS-01",
        "DEV-CTG-" => "WC-CT-01",
        "DEV-PKG-" => "WC-PK-01",
        _ => device.OrdinalInClass <= 3 ? "WC-AUX-MC" : device.OrdinalInClass <= 6 ? "WC-AUX-AS" : "WC-AUX-SP",
    };

    /// <summary>报警规则编码（服务端评估触发的 externalAlarmId 也用它；历史回填用 <c>{RuleCode}:{ordinal}</c> 区分）。</summary>
    public static string RuleCode(string deviceAssetId, string tagKey) => $"WH-{deviceAssetId}-{tagKey}";

    public static string AlarmCode(WorldHistoryTagBehavior tag) =>
        $"{tag.TagKey.ToUpperInvariant()}-{(tag.ComparisonOperator is "<" or "<=" ? "LOW" : "HIGH")}";

    /// <summary>
    /// 某设备在 <paramref name="day"/>（厂区本地日 = UTC 日，见 <see cref="WorldHistoryCalendar.SiteUtcOffset"/>）
    /// 的活动班次：辅助设备 7×24；产线设备周日/春节停产；装配双班；机加与表面早班 + 偶数序号中班（§1「部分中班」）。
    /// </summary>
    public static IReadOnlyList<int> ActiveShifts(WorldHistoryDeviceProfile device, DateOnly day)
    {
        if (device.Class.RunsContinuously)
        {
            return [0, 1, 2];
        }

        if (!WorldHistoryCalendar.IsWorkingDay(day) || WorldHistoryCalendar.IsSpringFestival(day))
        {
            return [];
        }

        return device.Class.Workshop switch
        {
            WorldHistoryDeviceWorkshop.Assembly => [0, 1],
            _ => device.OrdinalInClass % 2 == 0 ? [0, 1] : [0],
        };
    }

    /// <summary>该 UTC 日内的活动区间（分钟粒度，UTC）。班次 0/1 = UTC 00:00–08:00 / 08:00–16:00；辅助设备全天。</summary>
    public static IReadOnlyList<(DateTimeOffset StartUtc, DateTimeOffset EndUtc)> ActiveIntervals(
        WorldHistoryDeviceProfile device,
        DateOnly utcDay)
    {
        var dayStart = new DateTimeOffset(utcDay, TimeOnly.MinValue, TimeSpan.Zero);
        if (device.Class.RunsContinuously)
        {
            return [(dayStart, dayStart.AddDays(1))];
        }

        var intervals = new List<(DateTimeOffset, DateTimeOffset)>(2);
        foreach (var shiftIndex in ActiveShifts(device, utcDay))
        {
            intervals.Add((dayStart.AddHours(shiftIndex * 8), dayStart.AddHours((shiftIndex + 1) * 8)));
        }

        return intervals;
    }

    /// <summary>窗口内的活动分钟数（用于波形合成与 SampleCount）。</summary>
    public static int ActiveMinutes(WorldHistoryDeviceProfile device, DateTimeOffset windowStartUtc, DateTimeOffset windowEndUtc)
    {
        var minutes = 0d;
        var day = DateOnly.FromDateTime(windowStartUtc.UtcDateTime);
        var lastDay = DateOnly.FromDateTime(windowEndUtc.AddTicks(-1).UtcDateTime);
        while (day <= lastDay)
        {
            foreach (var (startUtc, endUtc) in ActiveIntervals(device, day))
            {
                var overlapStart = startUtc > windowStartUtc ? startUtc : windowStartUtc;
                var overlapEnd = endUtc < windowEndUtc ? endUtc : windowEndUtc;
                if (overlapEnd > overlapStart)
                {
                    minutes += (overlapEnd - overlapStart).TotalMinutes;
                }
            }

            day = day.AddDays(1);
        }

        return (int)Math.Round(minutes, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// 确定性波形：给定设备/点位与窗口，产出 min/avg/max/first/last。
    /// 值只依赖流键（设备+点位+窗口起点），与生成顺序、缩放无关（一期同款可复现性）。
    /// <paramref name="alarmObservedValue"/> 非空时表示窗口与某报警越限区间重叠，max 抬到越限值。
    /// </summary>
    public static WorldHistoryTelemetryShape Synthesize(
        WorldHistoryDeviceProfile device,
        WorldHistoryTagBehavior tag,
        DateTimeOffset windowStartUtc,
        DateTimeOffset windowEndUtc,
        decimal? alarmObservedValue)
    {
        var activeMinutes = ActiveMinutes(device, windowStartUtc, windowEndUtc);
        var totalMinutes = (int)Math.Max(1, Math.Round((windowEndUtc - windowStartUtc).TotalMinutes));
        var random = new WorldHistoryRandom($"telemetry:{device.DeviceAssetId}:{tag.TagKey}:{windowStartUtc.ToUnixTimeMilliseconds()}");
        var day = DateOnly.FromDateTime(windowStartUtc.UtcDateTime);
        var surgeFactor = WorldHistoryCalendar.IsMonthEndSurge(day) ? 1.10m : 1.00m;

        if (tag.IsCounter)
        {
            var count = decimal.Round(tag.RunningBase * activeMinutes / 60m * surgeFactor
                + (decimal)(random.NextDouble() * (double)tag.RunningSwing), 0, MidpointRounding.AwayFromZero);
            var sampleCount = Math.Max(1, activeMinutes * 30);
            return new WorldHistoryTelemetryShape(count, count, count, count, count, sampleCount);
        }

        var activeFraction = (decimal)activeMinutes / totalMinutes;
        var runLevel = tag.RunningBase + tag.RunningSwing * (decimal)((random.NextDouble() * 2d) - 1d) * 0.6m;
        var average = decimal.Round(tag.IdleValue * (1m - activeFraction) + runLevel * activeFraction, 3);
        var minValue = decimal.Round(Math.Min(tag.IdleValue, runLevel - tag.NoiseBand), 3);
        var maxValue = decimal.Round(runLevel + tag.NoiseBand, 3);
        if (alarmObservedValue is not null)
        {
            maxValue = alarmObservedValue.Value;
            average = decimal.Round(average + tag.NoiseBand, 3);
        }

        if (maxValue < average)
        {
            maxValue = average;
        }

        if (minValue > average)
        {
            minValue = average;
        }

        var samples = Math.Max(1, activeMinutes * 30);
        var firstValue = decimal.Round(average - tag.NoiseBand / 2m, 3);
        var lastValue = decimal.Round(average + tag.NoiseBand / 2m, 3);
        return new WorldHistoryTelemetryShape(minValue, average, maxValue, firstValue, lastValue, samples);
    }

    /// <summary>
    /// 全量报警计划（两侧同一算法）：按台×周确定性掷点，约 385 起；每起绑定该设备一个可报警点位，
    /// 触发时刻落在活动班次内；约 30% 升级为维修工单，工单号按触发时间全局排序取 <c>MWO-2026-####</c>。
    /// </summary>
    public static IReadOnlyList<WorldHistoryAlarmPlan> BuildAlarmPlans(DateOnly asOfDate, double scale)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        var alarmProbability = Math.Min(1d, WeeklyAlarmProbability * scale);
        var weeks = WorldHistoryCalendar.WeekCount(asOfDate);
        var openTailStartUtc = new DateTimeOffset(asOfDate.AddDays(-OpenAlarmTailDays), TimeOnly.MinValue, TimeSpan.Zero);
        var drafts = new List<AlarmDraft>();

        foreach (var device in Devices)
        {
            var alarmTags = device.Class.Tags.Where(x => x.HasAlarmRule).ToArray();
            if (alarmTags.Length == 0)
            {
                continue;
            }

            for (var week = 0; week < weeks; week++)
            {
                var random = new WorldHistoryRandom($"device-alarm:{device.DeviceAssetId}:{week:D3}");
                if (!random.Chance(alarmProbability))
                {
                    continue;
                }

                var slots = random.Chance(SecondAlarmProbability) ? 2 : 1;
                for (var slot = 0; slot < slots; slot++)
                {
                    var draft = BuildAlarmDraft(device, alarmTags, week, slot, asOfDate, random);
                    if (draft is not null)
                    {
                        drafts.Add(draft);
                    }
                }
            }
        }

        var ordered = drafts
            .OrderBy(x => x.RaisedAtUtc)
            .ThenBy(x => x.Device.DeviceAssetId, StringComparer.Ordinal)
            .ToArray();

        var plans = new List<WorldHistoryAlarmPlan>(ordered.Length);
        var workOrderSequence = 0;
        for (var index = 0; index < ordered.Length; index++)
        {
            var draft = ordered[index];
            var ordinal = index + 1;
            var isOpen = draft.RaisedAtUtc >= openTailStartUtc;
            string? workOrderNo = null;
            if (draft.HasWorkOrder)
            {
                workOrderSequence++;
                workOrderNo = $"MWO-2026-{workOrderSequence:D4}";
            }

            var clearedAtUtc = draft.RaisedAtUtc.AddMinutes(draft.DurationMinutes);
            var repairRandom = new WorldHistoryRandom($"repair:{draft.Device.DeviceAssetId}:{draft.Week:D3}:{draft.Slot}");
            var repairStartedAtUtc = draft.RaisedAtUtc.AddMinutes(repairRandom.NextInt(10, 41));
            var completedAtUtc = clearedAtUtc.AddMinutes(repairRandom.NextInt(5, 21));
            var laborMinutes = Math.Max(15, (int)Math.Round(draft.DurationMinutes * (0.6 + repairRandom.NextDouble() * 0.3)));

            plans.Add(new WorldHistoryAlarmPlan(
                Ordinal: ordinal,
                ExternalAlarmId: $"{RuleCode(draft.Device.DeviceAssetId, draft.Tag.TagKey)}:{ordinal:D4}",
                DeviceAssetId: draft.Device.DeviceAssetId,
                TagKey: draft.Tag.TagKey,
                UnitCode: draft.Tag.UnitCode,
                RuleCode: RuleCode(draft.Device.DeviceAssetId, draft.Tag.TagKey),
                AlarmCode: AlarmCode(draft.Tag),
                Severity: draft.Tag.AlarmSeverity,
                ThresholdValue: draft.Tag.AlarmThreshold,
                ObservedValue: draft.ObservedValue,
                RaisedAtUtc: draft.RaisedAtUtc,
                DurationMinutes: draft.DurationMinutes,
                ClearedAtUtc: clearedAtUtc,
                IsOpenAtAsOf: isOpen,
                HasWorkOrder: draft.HasWorkOrder,
                WorkOrderNo: workOrderNo,
                RepairStartedAtUtc: draft.HasWorkOrder ? repairStartedAtUtc : null,
                CompletedAtUtc: draft.HasWorkOrder && !isOpen ? completedAtUtc : null,
                DowntimeMinutes: Math.Max(15, draft.DurationMinutes),
                LaborMinutes: laborMinutes,
                TechnicianUserId: TechnicianUserIds[repairRandom.NextInt(0, TechnicianUserIds.Length)],
                FailureModeCode: FailureModeCode(draft.Tag),
                FailureCauseCode: FailureCauseCode(draft.Tag, repairRandom),
                DowntimeReasonCode: DowntimeReasonCode(draft.Tag)));
        }

        return plans;
    }

    private static AlarmDraft? BuildAlarmDraft(
        WorldHistoryDeviceProfile device,
        WorldHistoryTagBehavior[] alarmTags,
        int week,
        int slot,
        DateOnly asOfDate,
        WorldHistoryRandom random)
    {
        var weekStart = WorldHistoryCalendar.WeekStart(week);
        var day = weekStart.AddDays(random.NextInt(0, 6));
        if (day > asOfDate || !WorldHistoryCalendar.IsWorkingDay(day))
        {
            return null;
        }

        var shifts = ActiveShifts(device, day).Where(x => x <= 1).ToArray();
        if (shifts.Length == 0)
        {
            return null;
        }

        var shiftIndex = shifts[random.NextInt(0, shifts.Length)];
        var minutesIntoShift = random.NextInt(30, 420);
        var raisedAtUtc = WorldHistoryCalendar.ShiftMoment(day, shiftIndex, minutesIntoShift);
        var tag = alarmTags[random.NextInt(0, alarmTags.Length)];
        var durationMinutes = random.NextInt(20, 161);
        var exceedance = (decimal)(0.04 + random.NextDouble() * 0.10);
        var observedValue = tag.ComparisonOperator is "<" or "<="
            ? decimal.Round(tag.AlarmThreshold * (1m - exceedance), 3)
            : decimal.Round(tag.AlarmThreshold * (1m + exceedance), 3);
        var hasWorkOrder = tag.AlarmSeverity == "critical"
            ? random.Chance(CriticalWorkOrderProbability)
            : random.Chance(WarningWorkOrderProbability);

        return new AlarmDraft(device, tag, week, slot, raisedAtUtc, durationMinutes, observedValue, hasWorkOrder);
    }

    private static string FailureModeCode(WorldHistoryTagBehavior tag) =>
        $"{tag.TagKey}-{(tag.ComparisonOperator is "<" or "<=" ? "low" : "high")}";

    private static string FailureCauseCode(WorldHistoryTagBehavior tag, WorldHistoryRandom random) => tag.TagKey switch
    {
        "vibration" => random.Chance(0.6) ? "bearing-wear" : "fixture-loose",
        "spindle-temperature" or "temperature" => random.Chance(0.5) ? "lubrication" : "cooling",
        "press-force" or "damping-force" => "tooling-drift",
        "weld-current" => "electrical",
        "bath-temperature" or "bath-ph" => "process-drift",
        "air-pressure" => "air-leak",
        _ => "overload",
    };

    private static string DowntimeReasonCode(WorldHistoryTagBehavior tag) => tag.TagKey switch
    {
        "press-force" or "damping-force" => "DT-TOOL",
        "weld-current" => "DT-ELEC",
        "bath-temperature" or "bath-ph" => "DT-PROC",
        _ => "DT-MECH",
    };

    /// <summary>
    /// 点检/保养计划（Maintenance 侧写入）：每台设备一条点检计划（high 关键度 P7D，其余 P14D）
    /// 和一条保养计划（P30D），共 92 条，起始日 = 上线日。
    /// </summary>
    public static IReadOnlyList<WorldHistoryMaintenancePlanSpec> BuildMaintenancePlans()
    {
        var plans = new List<WorldHistoryMaintenancePlanSpec>(Devices.Count * 2);
        foreach (var device in Devices)
        {
            var inspectionInterval = device.Class.Criticality == "high" ? 7 : 14;
            plans.Add(new WorldHistoryMaintenancePlanSpec(
                $"PM-WH-INSP-{device.DeviceAssetId}", device.DeviceAssetId, inspectionInterval, "inspection"));
            plans.Add(new WorldHistoryMaintenancePlanSpec(
                $"PM-WH-SVC-{device.DeviceAssetId}", device.DeviceAssetId, 30, "service"));
        }

        return plans;
    }

    /// <summary>按计划频次展开上线日至截止日的点检/保养记录（每期一条，落工作日早班；约 3% 判不合格）。</summary>
    public static IReadOnlyList<WorldHistoryInspectionOccurrence> BuildInspections(DateOnly asOfDate)
    {
        var occurrences = new List<WorldHistoryInspectionOccurrence>();
        foreach (var plan in BuildMaintenancePlans())
        {
            var dueOn = WorldHistoryCalendar.GoLiveDate;
            while (dueOn <= asOfDate)
            {
                var workingDay = WorldHistoryCalendar.SnapToWorkingDay(dueOn);
                if (workingDay <= asOfDate)
                {
                    var random = new WorldHistoryRandom($"inspection:{plan.PlanCode}:{workingDay:yyyy-MM-dd}");
                    var inspectedAtUtc = WorldHistoryCalendar.ShiftMoment(workingDay, 0, random.NextInt(30, 450));
                    var result = random.Chance(0.03) ? "failed" : "passed";
                    var inspector = plan.Kind == "inspection"
                        ? InspectorUserId
                        : TechnicianUserIds[random.NextInt(0, TechnicianUserIds.Length)];
                    occurrences.Add(new WorldHistoryInspectionOccurrence(
                        plan.PlanCode, plan.DeviceAssetId, workingDay, inspectedAtUtc, result, inspector));
                }

                dueOn = dueOn.AddDays(plan.IntervalDays);
            }
        }

        return occurrences;
    }

    private sealed record AlarmDraft(
        WorldHistoryDeviceProfile Device,
        WorldHistoryTagBehavior Tag,
        int Week,
        int Slot,
        DateTimeOffset RaisedAtUtc,
        int DurationMinutes,
        decimal ObservedValue,
        bool HasWorkOrder);
}

/// <summary>L0 §2 车间归属 + 辅助公用类别（决定班次覆盖与 OEE 参与度）。</summary>
public enum WorldHistoryDeviceWorkshop
{
    Machining,
    Assembly,
    Surface,
    Auxiliary,
}

public sealed record WorldHistoryTagBehavior(
    string TagKey,
    string UnitCode,
    bool IsCounter,
    decimal IdleValue,
    decimal RunningBase,
    decimal RunningSwing,
    decimal NoiseBand,
    string ComparisonOperator,
    decimal AlarmThreshold,
    string AlarmSeverity,
    bool HasAlarmRule = true);

public sealed record WorldHistoryDeviceClassSpec(
    string CodePrefix,
    int DeviceCount,
    string CollectionConnectorId,
    WorldHistoryDeviceWorkshop Workshop,
    bool RunsContinuously,
    decimal? TheoreticalRatePerHour,
    string Criticality,
    WorldHistoryTagBehavior[] Tags);

public sealed record WorldHistoryDeviceProfile(
    string DeviceAssetId,
    int OrdinalInClass,
    WorldHistoryDeviceClassSpec Class);

public sealed record WorldHistoryTelemetryShape(
    decimal MinValue,
    decimal AverageValue,
    decimal MaxValue,
    decimal FirstValue,
    decimal LastValue,
    int SampleCount);

public sealed record WorldHistoryAlarmPlan(
    int Ordinal,
    string ExternalAlarmId,
    string DeviceAssetId,
    string TagKey,
    string UnitCode,
    string RuleCode,
    string AlarmCode,
    string Severity,
    decimal ThresholdValue,
    decimal ObservedValue,
    DateTimeOffset RaisedAtUtc,
    int DurationMinutes,
    DateTimeOffset ClearedAtUtc,
    bool IsOpenAtAsOf,
    bool HasWorkOrder,
    string? WorkOrderNo,
    DateTimeOffset? RepairStartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    int DowntimeMinutes,
    int LaborMinutes,
    string TechnicianUserId,
    string FailureModeCode,
    string FailureCauseCode,
    string DowntimeReasonCode);

public sealed record WorldHistoryDowntimeReason(
    string Code,
    string Description,
    string ReasonCategory,
    string LossCategory);

public sealed record WorldHistoryMaintenancePlanSpec(
    string PlanCode,
    string DeviceAssetId,
    int IntervalDays,
    string Kind)
{
    public string Interval => $"P{IntervalDays}D";
}

public sealed record WorldHistoryInspectionOccurrence(
    string PlanCode,
    string DeviceAssetId,
    DateOnly DueOn,
    DateTimeOffset InspectedAtUtc,
    string Result,
    string InspectorUserId);
