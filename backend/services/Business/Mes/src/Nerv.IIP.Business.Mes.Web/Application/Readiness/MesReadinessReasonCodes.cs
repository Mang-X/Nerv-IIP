using Nerv.IIP.Contracts.EquipmentRuntime;

namespace Nerv.IIP.Business.Mes.Web.Application.Readiness;

/// <summary>
/// 开工/报工门禁阻断原因的**码注册表**，也是 #3155 建立的跨语言契约在后端这一侧的权威面。
///
/// 契约：本类的每个 <c>public const string</c> 都必须在前端
/// <c>frontend/packages/business-core/src/labels/mesReadinessReasons.ts</c> 的
/// <c>MES_READINESS_REASON_DISPLAYS</c> 里登记，由
/// <c>scripts/verify-stable-code-frontend-vocabulary.ps1</c> 在 CI 强制。方向是单向包含
/// （后端 ⊆ 前端）：前端为已下线的历史码保留展示是合法的，反向要求会误报。
///
/// ⚠️ 因此本类只装**码**，不装完整原因串——注册表里混进 <c>CODE: 中文</c> 形态的常量会让
/// 检查器把整句当成一个码去前端词表里找，永远找不到。成句的原因串放
/// <see cref="MesReadinessReasonTexts"/>。
///
/// ⚠️ 登记行为就是「在本类声明一个 public const」。以裸字面量形式直接写进
/// <c>blockReasons</c> 的码**不在检查器的扫描面内**，也就享受不到这条契约；
/// <c>WORK_ORDER_NOT_FOUND</c> 曾经就是这样漏掉的（#3155）。
/// 私有常量刻意排除在外：它们不会作为阻断码外发（<see cref="SourceUnavailable"/> 在
/// <see cref="NormalizeIndustrialTelemetryReasonCode"/> 里被归一化掉，从不上读面）。
/// </summary>
public static class MesReadinessReasonCodes
{
    /// <summary>
    /// 工单尚未下达（#3119）。工单在 <c>created</c> 状态就能带 <c>InProgress</c> 工序并被报工受理是缺陷
    /// （定性见 #3113），本码是准入侧的拒绝理由：开工由
    /// <c>MesOperationTaskActionReadinessEvaluator</c> 产出，报工由
    /// <c>RecordProductionReportCommandHandler</c> 产出，两处共用
    /// <see cref="MesReadinessReasonTexts.WorkOrderNotReleasedReason"/> 这一份措辞。
    /// </summary>
    public const string WorkOrderNotReleased = "WORK_ORDER_NOT_RELEASED";

    /// <summary>
    /// 阻断动作的工序找不到所属生产工单。#3155 的票面反例：本码与
    /// <see cref="WorkOrderNotReleased"/> 出自同一个 evaluator、相隔一行，却因为写成裸字面量
    /// 而长期不在任何契约的扫描面内。
    /// </summary>
    public const string WorkOrderNotFound = "WORK_ORDER_NOT_FOUND";

    public const string PreviousOperationIncomplete = "PREVIOUS_OPERATION_INCOMPLETE";
    public const string MaterialShortage = "MATERIAL_SHORTAGE";
    public const string MaterialRequirementSnapshotMissing = "MATERIAL_REQUIREMENT_SNAPSHOT_MISSING";
    public const string QualityPlanMissing = "QUALITY_PLAN_MISSING";
    public const string QualityHoldActive = "QUALITY_HOLD_ACTIVE";
    public const string ActiveAlarm = EquipmentRuntimeReasonCodes.ActiveAlarm;
    public const string StateUnavailable = EquipmentRuntimeReasonCodes.StateUnavailable;
    public const string Downtime = EquipmentRuntimeReasonCodes.Downtime;
    public const string MaintenanceWindow = EquipmentRuntimeReasonCodes.MaintenanceWindow;
    public const string InspectionRequired = EquipmentRuntimeReasonCodes.InspectionRequired;
    public const string SourceStale = EquipmentRuntimeReasonCodes.SourceStale;
    public const string TagMappingMissing = EquipmentRuntimeReasonCodes.TagMappingMissing;
    public const string NoEligibleSubstitute = EquipmentRuntimeReasonCodes.NoEligibleSubstitute;

    private const string SourceUnavailable = "equipment.sourceUnavailable";
    private const string PlannedMaintenanceDowntimeReasonCode = "DT-PM";

    public static EquipmentReadinessClassification ClassifyEquipmentReason(string reason)
    {
        var normalizedReason = string.IsNullOrWhiteSpace(reason) ? Downtime : reason.Trim();

        var exactClassification = ClassifyKnownEquipmentRuntimeReason(normalizedReason);
        if (exactClassification is not null)
        {
            return exactClassification;
        }

        if (IsMaintenanceReason(normalizedReason))
        {
            return new EquipmentReadinessClassification(
                MaintenanceWindow,
                "Maintenance",
                "设备存在维修或保养占用，当前工序不能派工或开工。",
                "调整维修窗口、选择替代设备或等待维修释放");
        }

        if (IsIndustrialTelemetryReason(normalizedReason))
        {
            return new EquipmentReadinessClassification(
                NormalizeIndustrialTelemetryReasonCode(normalizedReason),
                "IndustrialTelemetry",
                "工业遥测存在未解除报警，设备不可用于当前工序。",
                "处理并解除设备报警后重新检查");
        }

        return ClassifyDowntime();
    }

    private static EquipmentReadinessClassification? ClassifyKnownEquipmentRuntimeReason(string reason)
    {
        if (reason.Equals(NoEligibleSubstitute, StringComparison.OrdinalIgnoreCase))
        {
            return new EquipmentReadinessClassification(
                NoEligibleSubstitute,
                "BusinessScheduling",
                "排程未找到可替代设备，当前工序不能派工或开工。",
                "调整设备候选范围、释放替代设备或重新运行排程");
        }

        if (reason.Equals(MaintenanceWindow, StringComparison.OrdinalIgnoreCase) ||
            reason.Equals(InspectionRequired, StringComparison.OrdinalIgnoreCase))
        {
            return new EquipmentReadinessClassification(
                reason.Equals(InspectionRequired, StringComparison.OrdinalIgnoreCase)
                    ? InspectionRequired
                    : MaintenanceWindow,
                "Maintenance",
                "设备存在维修或保养占用，当前工序不能派工或开工。",
                "调整维修窗口、选择替代设备或等待维修释放");
        }

        if (reason.Equals(ActiveAlarm, StringComparison.OrdinalIgnoreCase) ||
            reason.Equals(StateUnavailable, StringComparison.OrdinalIgnoreCase) ||
            reason.Equals(SourceStale, StringComparison.OrdinalIgnoreCase) ||
            reason.Equals(TagMappingMissing, StringComparison.OrdinalIgnoreCase) ||
            reason.Equals(SourceUnavailable, StringComparison.OrdinalIgnoreCase))
        {
            return new EquipmentReadinessClassification(
                NormalizeIndustrialTelemetryReasonCode(reason),
                "IndustrialTelemetry",
                "工业遥测存在未解除报警，设备不可用于当前工序。",
                "处理并解除设备报警后重新检查");
        }

        return reason.Equals(Downtime, StringComparison.OrdinalIgnoreCase)
            ? ClassifyDowntime()
            : null;
    }

    private static EquipmentReadinessClassification ClassifyDowntime()
    {
        return new EquipmentReadinessClassification(
            Downtime,
            "BusinessMes",
            "MES 停机记录显示设备或工作中心当前不可用。",
            "关闭停机事件、选择替代设备或调整派工时间");
    }

    private static bool IsMaintenanceReason(string reason) =>
        reason.Equals(MaintenanceWindow, StringComparison.OrdinalIgnoreCase) ||
        reason.Equals(InspectionRequired, StringComparison.OrdinalIgnoreCase) ||
        reason.Equals(PlannedMaintenanceDowntimeReasonCode, StringComparison.OrdinalIgnoreCase) ||
        reason.Contains("maintenance", StringComparison.OrdinalIgnoreCase) ||
        reason.Contains("保养", StringComparison.OrdinalIgnoreCase) ||
        reason.Contains("维修", StringComparison.OrdinalIgnoreCase);

    private static bool IsIndustrialTelemetryReason(string reason) =>
        reason.Equals(ActiveAlarm, StringComparison.OrdinalIgnoreCase) ||
        reason.Equals(StateUnavailable, StringComparison.OrdinalIgnoreCase) ||
        reason.Equals(SourceStale, StringComparison.OrdinalIgnoreCase) ||
        reason.Equals(TagMappingMissing, StringComparison.OrdinalIgnoreCase) ||
        reason.Equals(SourceUnavailable, StringComparison.OrdinalIgnoreCase) ||
        reason.Contains("alarm", StringComparison.OrdinalIgnoreCase) ||
        reason.Contains("telemetry", StringComparison.OrdinalIgnoreCase) ||
        reason.Contains("报警", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeIndustrialTelemetryReasonCode(string reason)
    {
        if (reason.Equals(ActiveAlarm, StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("alarm", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("报警", StringComparison.OrdinalIgnoreCase))
        {
            return ActiveAlarm;
        }

        if (reason.Equals(StateUnavailable, StringComparison.OrdinalIgnoreCase) ||
            reason.Equals(SourceUnavailable, StringComparison.OrdinalIgnoreCase))
        {
            return StateUnavailable;
        }

        if (reason.Equals(SourceStale, StringComparison.OrdinalIgnoreCase))
        {
            return SourceStale;
        }

        if (reason.Contains("telemetry", StringComparison.OrdinalIgnoreCase))
        {
            return SourceStale;
        }

        if (reason.Equals(TagMappingMissing, StringComparison.OrdinalIgnoreCase))
        {
            return TagMappingMissing;
        }

        return Downtime;
    }
}

/// <summary>
/// 成句的 <c>CODE: 中文</c> 阻断原因串。与 <see cref="MesReadinessReasonCodes"/> 分开放，
/// 是因为那边是跨语言契约的扫描面、只能装码（见该类注释）。
/// </summary>
public static class MesReadinessReasonTexts
{
    /// <summary>
    /// <see cref="MesReadinessReasonCodes.WorkOrderNotReleased"/> 的完整 <c>CODE: 中文</c> 原因串。
    /// 读面原样上屏（前端 <c>describeMesReadinessReason</c> 按码取标签与下一步动作），
    /// 写操作被拒时经 <c>MaterialReadinessGuards.DescribeForUser</c> 剥掉英文码后再进 KnownException。
    /// </summary>
    public const string WorkOrderNotReleasedReason =
        MesReadinessReasonCodes.WorkOrderNotReleased + ": 工单尚未下达，请先下达工单后再开工或报工。";
}

public sealed record EquipmentReadinessClassification(
    string Code,
    string SourceSystem,
    string Message,
    string FixHint);
