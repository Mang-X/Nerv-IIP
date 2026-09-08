using Nerv.IIP.Contracts.EquipmentRuntime;

namespace Nerv.IIP.Business.Mes.Web.Application.Readiness;

public static class MesReadinessReasonCodes
{
    /// <summary>
    /// 工单尚未下达（#3119）。工单在 <c>created</c> 状态就能带 <c>InProgress</c> 工序并被报工受理是缺陷
    /// （定性见 #3113），本码是准入侧的拒绝理由：开工由
    /// <c>MesOperationTaskActionReadinessEvaluator</c> 产出，报工由
    /// <c>RecordProductionReportCommandHandler</c> 产出，两处共用本文件这一份措辞。
    /// </summary>
    public const string WorkOrderNotReleased = "WORK_ORDER_NOT_RELEASED";

    /// <summary>
    /// <see cref="WorkOrderNotReleased"/> 的完整 <c>CODE: 中文</c> 原因串。
    /// 读面原样上屏（前端 <c>describeMesReadinessReason</c> 按码取标签与下一步动作），
    /// 写操作被拒时经 <c>MaterialReadinessGuards.DescribeForUser</c> 剥掉英文码后再进 KnownException。
    /// </summary>
    public const string WorkOrderNotReleasedReason =
        WorkOrderNotReleased + ": 工单尚未下达，请先下达工单后再开工或报工。";

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

public sealed record EquipmentReadinessClassification(
    string Code,
    string SourceSystem,
    string Message,
    string FixHint);
