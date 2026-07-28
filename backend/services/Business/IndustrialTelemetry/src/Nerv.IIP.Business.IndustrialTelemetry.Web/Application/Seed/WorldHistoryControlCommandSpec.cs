using System.Globalization;
using System.Text;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》L1 背景历史 **四期（IndustrialTelemetry 侧）**：设备控制指令台账
/// （<c>device_control_commands</c>，号段 <c>OPS-WH-*</c>）的确定性纯函数 Spec。
///
/// <para><b>这张表是什么</b>：它**不是**执行记录，而是 Ops operation task 的投影——
/// 真正的执行权威在 Ops，本表存的是「下发那一刻，谁对哪台设备的哪个点位下了什么值、为什么下」
/// 的业务快照，供设备详情页的「控制指令」区做历史读面。因此历史回填只写台账，
/// **绝不调用 <c>IDeviceControlOpsClient</c>**，不会有任何 Ops 任务被创建、也就不会有任何下发。</para>
///
/// <para><b>历史指令一律是终态——这是硬条款，不是风格</b>：
/// <c>queued</c> / <c>approval-pending</c> / <c>dispatched</c> 三个态在读面上意味着
/// 「这条命令还在路上」：详情页会持续轮询 Ops、审批页会把它列成待办，演示时任何一次
/// 审批通过都可能变成对真实设备（或 L3 模拟连接器）的一次真实下发。历史数据绝不能留下这种引信。
/// 因此本 Spec 只产出 <c>completed</c> / <c>failed</c> / <c>rejected</c> 三个终态，
/// 且每条都带 <c>FinishedAtUtc</c>；「按下按钮真的动一次设备」留给演示当场走真实路径（L2）。
/// 该条款在 <see cref="WorldHistoryConsistencyValidator"/> 里 fail-closed 复核。</para>
///
/// <para><b>确定性</b>：每条指令由自己的 <c>OPS-WH-*</c> 任务号单独取流，与截止日、缩放比例、
/// 其他指令是否存在无关；终态配额也按任务号的 FNV 折叠取（**不用全局序号**——截止日推进会让
/// 「当周被 <c>day &gt; asOf</c> 暂时挡下的草稿」事后补进序列中段，位置化编号会整体漂移，
/// 幂等与两侧对账随之断裂，这是报警号段已经踩过的坑）。</para>
/// </summary>
public static class WorldHistoryControlCommandSpec
{
    /// <summary>本块产出的号段前缀（设定集 §9 四期补登记），供隔离性回归断言。</summary>
    public const string OperationTaskPrefix = "OPS-WH-";

    /// <summary>会触发下发的待执行态——历史台账里一条都不许有（fail-closed 条款）。</summary>
    public static readonly string[] PendingDispatchStatuses = ["queued", "approval-pending", "dispatched"];

    /// <summary>Ops 终态（与 <c>DeviceControlCommand.TerminalStatuses</c> 同字面量，规格层不反向依赖领域层）。</summary>
    public static readonly string[] TerminalStatuses = ["completed", "failed", "rejected"];

    /// <summary>报警处置停机指令的发生概率（critical 现场基本都会停机，warning 多数只是观察）。</summary>
    public const double CriticalStopProbability = 0.62;
    public const double WarningStopProbability = 0.24;

    /// <summary>
    /// 各设备类别的**可写设定值点位**。传感器（振动 / 温度 / 计数器）是只读采集点，
    /// 把它们写进控制指令会让演示当场被问住——现场没人「写振动」。
    /// <c>DEV-PKG-</c> 只有节拍计数器，因此不产生参数类指令。
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> WritableSetpointsByClass =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["DEV-CNC-"] = ["spindle-speed"],
            ["DEV-GRD-"] = ["wheel-speed"],
            ["DEV-WLD-"] = ["weld-current"],
            ["DEV-ASM-"] = ["press-force"],
            ["DEV-TST-"] = ["damping-force"],
            ["DEV-CTG-"] = ["bath-temperature", "bath-ph"],
            ["DEV-AUX-"] = ["air-pressure"],
        };

    /// <summary>
    /// 停机 / 启机指令走的是控制通道的运行命令位，不是采集清单里的点位——
    /// 采集点位是只读的过程量，运行命令是控制侧独有的写点。显式命名以免与采集点混淆。
    /// </summary>
    public const string RunCommandTagKey = "run-command";

    /// <summary>下发人：设备部维修技师（L0 §5 EMP-043..046）与点检员（EMP-047）。</summary>
    public static readonly string[] OperatorUserIds =
        [.. WorldHistoryDeviceSpec.TechnicianUserIds, WorldHistoryDeviceSpec.InspectorUserId];

    #region 号段

    /// <summary>报警处置指令的任务号：直接派生自报警号，一起报警最多一条处置指令，天然幂等。</summary>
    public static string AlarmResponseTaskId(string externalAlarmId) => $"OPS-{externalAlarmId}";

    /// <summary>参数下发指令的任务号：设备 × 周，位置无关。</summary>
    public static string SetpointTaskId(string deviceAssetId, int week) =>
        $"{OperationTaskPrefix}SET-{deviceAssetId}-{week:D3}";

    #endregion

    /// <summary>
    /// 全量控制指令计划。seed 与校验器共用它，于是「写入的东西」与「校验的东西」不可能漂移。
    /// 已按下发时刻排序，仅为日志样本可读，不参与任何编号。
    /// </summary>
    public static IReadOnlyList<WorldHistoryControlCommandPlan> BuildCommandPlans(DateOnly asOfDate, double scale)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);

        var plans = new List<WorldHistoryControlCommandPlan>(512);
        AppendAlarmResponses(asOfDate, scale, plans);
        AppendSetpointCommands(asOfDate, plans);

        return
        [
            .. plans
                .OrderBy(x => x.RequestedAtUtc)
                .ThenBy(x => x.OperationTaskId, StringComparer.Ordinal),
        ];
    }

    #region 一 · 报警处置停机指令（start-stop，需审批）

    private static void AppendAlarmResponses(DateOnly asOfDate, double scale, List<WorldHistoryControlCommandPlan> plans)
    {
        foreach (var alarm in WorldHistoryDeviceSpec.BuildAlarmPlans(asOfDate, scale))
        {
            var random = new WorldHistoryRandom($"device-control:{alarm.ExternalAlarmId}");
            var probability = alarm.Severity == "critical" ? CriticalStopProbability : WarningStopProbability;
            if (!random.Chance(probability))
            {
                continue;
            }

            var operationTaskId = AlarmResponseTaskId(alarm.ExternalAlarmId);
            var requestedAtUtc = alarm.RaisedAtUtc.AddMinutes(random.NextInt(3, 18));
            if (DateOnly.FromDateTime(requestedAtUtc.UtcDateTime) > asOfDate)
            {
                continue;
            }

            var device = DeviceOf(alarm.DeviceAssetId);
            var outcome = ResolveOutcome(operationTaskId, requiresApproval: true);
            var reason = string.Format(
                CultureInfo.InvariantCulture,
                "{0}{1}（实测 {2:0.###}{3}，阈值 {4:0.###}{3}），现场确认后远程停机检修",
                TagDisplayName(alarm.TagKey),
                alarm.Severity == "critical" ? "严重超限" : "超限",
                alarm.ObservedValue,
                alarm.UnitCode,
                alarm.ThresholdValue);

            plans.Add(new WorldHistoryControlCommandPlan(
                OperationTaskId: operationTaskId,
                DeviceAssetId: alarm.DeviceAssetId,
                InstanceKey: device.Class.CollectionConnectorId,
                CommandType: "start-stop",
                TagKey: RunCommandTagKey,
                Value: "stop",
                ParametersJson: null,
                RequestedByUserId: alarm.TechnicianUserId,
                Reason: reason,
                RequiresApproval: true,
                Outcome: outcome,
                RequestedAtUtc: requestedAtUtc,
                FinishedAtUtc: requestedAtUtc.AddMinutes(FinishDelayMinutes(operationTaskId, outcome))));
        }
    }

    #endregion

    #region 二 · 工艺参数下发（write-tag / parameter-set，免审批）

    /// <summary>
    /// 每周一台设备一次设定值调整：每个有可写设定点的类别按 <c>周次 % 台数</c> 轮转，
    /// 落在当周第二个工作日的早班。多点位类别（电泳线的槽温 + PH）走 <c>parameter-set</c>
    /// 一次下发两个参数，单点位类别走 <c>write-tag</c>——三种指令类型在历史里都有样本。
    /// </summary>
    private static void AppendSetpointCommands(DateOnly asOfDate, List<WorldHistoryControlCommandPlan> plans)
    {
        var weeks = WorldHistoryCalendar.WeekCount(asOfDate);
        foreach (var deviceClass in WorldHistoryDeviceSpec.DeviceClasses)
        {
            if (!WritableSetpointsByClass.TryGetValue(deviceClass.CodePrefix, out var setpointKeys) || setpointKeys.Length == 0)
            {
                continue;
            }

            var tags = setpointKeys
                .Select(key => deviceClass.Tags.Single(tag => string.Equals(tag.TagKey, key, StringComparison.Ordinal)))
                .ToArray();

            for (var week = 0; week < weeks; week++)
            {
                var ordinalInClass = (week % deviceClass.DeviceCount) + 1;
                var deviceAssetId = $"{deviceClass.CodePrefix}{ordinalInClass:D2}";
                var operationTaskId = SetpointTaskId(deviceAssetId, week);
                var random = new WorldHistoryRandom($"device-setpoint:{operationTaskId}");

                var day = WorldHistoryCalendar.SnapToWorkingDay(WorldHistoryCalendar.WeekStart(week).AddDays(1));
                if (day > asOfDate)
                {
                    continue;
                }

                var requestedAtUtc = WorldHistoryCalendar.ShiftMoment(day, 0, random.NextInt(45, 400));
                if (DateOnly.FromDateTime(requestedAtUtc.UtcDateTime) > asOfDate)
                {
                    continue;
                }

                var outcome = ResolveOutcome(operationTaskId, requiresApproval: false);
                var values = tags.ToDictionary(
                    tag => tag.TagKey,
                    tag => FormatSetpoint(SetpointValue(tag, random)),
                    StringComparer.Ordinal);
                var isParameterSet = tags.Length > 1;
                var reason = isParameterSet
                    ? "换型前按工艺卡下发电泳槽参数组"
                    : $"批次换型，按工艺卡调整{TagDisplayName(tags[0].TagKey)}设定值";

                plans.Add(new WorldHistoryControlCommandPlan(
                    OperationTaskId: operationTaskId,
                    DeviceAssetId: deviceAssetId,
                    InstanceKey: deviceClass.CollectionConnectorId,
                    CommandType: isParameterSet ? "parameter-set" : "write-tag",
                    TagKey: isParameterSet ? null : tags[0].TagKey,
                    Value: isParameterSet ? null : values[tags[0].TagKey],
                    ParametersJson: isParameterSet ? SerializeParameters(values) : null,
                    RequestedByUserId: OperatorUserIds[random.NextInt(0, OperatorUserIds.Length)],
                    Reason: reason,
                    RequiresApproval: false,
                    Outcome: outcome,
                    RequestedAtUtc: requestedAtUtc,
                    FinishedAtUtc: requestedAtUtc.AddMinutes(FinishDelayMinutes(operationTaskId, outcome))));
            }
        }
    }

    /// <summary>设定值落在正常运行带内、恒不越过报警阈值——历史参数下发不该自己制造一起报警。</summary>
    private static decimal SetpointValue(WorldHistoryTagBehavior tag, WorldHistoryRandom random)
    {
        var jitter = (decimal)((random.NextDouble() * 2d) - 1d) * tag.RunningSwing * 0.35m;
        var candidate = tag.RunningBase + jitter;
        return tag.ComparisonOperator is "<" or "<="
            ? Math.Max(candidate, tag.AlarmThreshold + tag.NoiseBand)
            : Math.Min(candidate, tag.AlarmThreshold - tag.NoiseBand);
    }

    private static string FormatSetpoint(decimal value) =>
        decimal.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>参数组 JSON：键按序数排序后手写，保证同一计划每次得到逐字节相同的字符串。</summary>
    private static string SerializeParameters(IReadOnlyDictionary<string, string> values)
    {
        var builder = new StringBuilder("{");
        var first = true;
        foreach (var (key, value) in values.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (!first)
            {
                builder.Append(',');
            }

            builder.Append('"').Append(key).Append("\":\"").Append(value).Append('"');
            first = false;
        }

        return builder.Append('}').ToString();
    }

    #endregion

    #region 终态配额

    /// <summary>
    /// 终态配额（每 20 条）：成功 17 / 失败 2 / 驳回 1。用任务号的 FNV 折叠分档而不是全局序号，
    /// 位置不随截止日漂移；免审批的指令没有「驳回」可言，其驳回档并入失败档。
    /// </summary>
    public static WorldHistoryControlOutcome ResolveOutcome(string operationTaskId, bool requiresApproval)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationTaskId);
        var slot = (int)(WorldHistoryRandom.Fnv1a64($"outcome:{operationTaskId}") % 20UL);
        return slot switch
        {
            < 17 => WorldHistoryControlOutcome.Completed,
            < 19 => WorldHistoryControlOutcome.Failed,
            _ => requiresApproval ? WorldHistoryControlOutcome.Rejected : WorldHistoryControlOutcome.Failed,
        };
    }

    private static int FinishDelayMinutes(string operationTaskId, WorldHistoryControlOutcome outcome)
    {
        var random = new WorldHistoryRandom($"finish:{operationTaskId}");
        return outcome switch
        {
            WorldHistoryControlOutcome.Rejected => random.NextInt(6, 45),
            WorldHistoryControlOutcome.Failed => random.NextInt(1, 6),
            _ => random.NextInt(1, 9),
        };
    }

    #endregion

    private static WorldHistoryDeviceProfile DeviceOf(string deviceAssetId) =>
        WorldHistoryDeviceSpec.Devices.Single(x => string.Equals(x.DeviceAssetId, deviceAssetId, StringComparison.Ordinal));

    /// <summary>点位中文名（演示页面与指令原因文案全中文）。</summary>
    public static string TagDisplayName(string tagKey) => tagKey switch
    {
        "spindle-temperature" => "主轴温度",
        "vibration" => "振动",
        "spindle-speed" => "主轴转速",
        "wheel-speed" => "砂轮转速",
        "weld-current" => "焊接电流",
        "temperature" => "温度",
        "press-force" => "压装力",
        "cycle-count" => "节拍计数",
        "damping-force" => "阻尼力",
        "bath-temperature" => "槽液温度",
        "bath-ph" => "槽液 PH",
        "air-pressure" => "气源压力",
        RunCommandTagKey => "运行命令",
        _ => tagKey,
    };
}

/// <summary>历史控制指令的终态（三者都是 Ops 终态，绝不含待执行态）。</summary>
public enum WorldHistoryControlOutcome
{
    /// <summary>下发成功，设备回执正常。</summary>
    Completed,

    /// <summary>下发失败（通道不可达 / 设备拒写 / 值越界）。</summary>
    Failed,

    /// <summary>审批驳回，未下发到设备。</summary>
    Rejected,
}

/// <summary>一条历史设备控制指令（台账即下发时快照 + Ops 终态回写结果）。</summary>
public sealed record WorldHistoryControlCommandPlan(
    string OperationTaskId,
    string DeviceAssetId,
    string InstanceKey,
    string CommandType,
    string? TagKey,
    string? Value,
    string? ParametersJson,
    string RequestedByUserId,
    string Reason,
    bool RequiresApproval,
    WorldHistoryControlOutcome Outcome,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset FinishedAtUtc)
{
    /// <summary>幂等键：与 Ops 任务号同值（真实链路上二者本就一一对应）。</summary>
    public string IdempotencyKey => OperationTaskId;

    public string CorrelationId => $"corr-{OperationTaskId}";

    /// <summary>下发时的状态快照：需审批的落 <c>approval-pending</c>，其余落 <c>queued</c>。</summary>
    public string DispatchStatus => RequiresApproval ? "approval-pending" : "queued";

    /// <summary>下发时的审批快照：需审批的落 <c>pending</c>，免审批的为空。</summary>
    public string? DispatchApprovalStatus => RequiresApproval ? "pending" : null;

    /// <summary>Ops 回写的终态。</summary>
    public string TerminalStatus => Outcome switch
    {
        WorldHistoryControlOutcome.Completed => "completed",
        WorldHistoryControlOutcome.Failed => "failed",
        _ => "rejected",
    };

    /// <summary>终态回写后的审批态（与 <c>DeviceControlCommand.ResolveApprovalStatus</c> 同语义）。</summary>
    public string ExpectedApprovalStatus => Outcome == WorldHistoryControlOutcome.Rejected ? "rejected" : "approved";

    public string? FailureCode => Outcome == WorldHistoryControlOutcome.Failed
        ? FailureCodes[(int)(WorldHistoryRandom.Fnv1a64($"failure:{OperationTaskId}") % (ulong)FailureCodes.Length)]
        : null;

    public string? DeviceReceiptCode => Outcome switch
    {
        WorldHistoryControlOutcome.Completed => "Good",
        WorldHistoryControlOutcome.Failed => "BadDeviceFailure",
        _ => null,
    };

    public string? DeviceReceiptMessage => Outcome switch
    {
        WorldHistoryControlOutcome.Completed => "设备已确认接收并写入",
        WorldHistoryControlOutcome.Failed => "设备未确认写入，已按失败结案",
        _ => null,
    };

    private static readonly string[] FailureCodes =
        ["connector-unreachable", "tag-write-rejected", "device-busy"];
}
