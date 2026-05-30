namespace Nerv.IIP.Business.Mes.Web.Application.Readiness;

public static class MesReadinessReasonCodes
{
    public const string QualityPlanMissing = "QUALITY_PLAN_MISSING";
    public const string QualityHoldActive = "QUALITY_HOLD_ACTIVE";
    public const string EquipmentUnavailable = "EQUIPMENT_UNAVAILABLE";
    public const string EquipmentMaintenanceConflict = "EQUIPMENT_MAINTENANCE_CONFLICT";

    public static EquipmentReadinessClassification ClassifyEquipmentReason(string reason)
    {
        if (reason.Contains("maintenance", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("保养", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("维修", StringComparison.OrdinalIgnoreCase))
        {
            return new EquipmentReadinessClassification(
                EquipmentMaintenanceConflict,
                "Maintenance",
                "设备存在维修或保养占用，当前工序不能派工或开工。",
                "调整维修窗口、选择替代设备或等待维修释放");
        }

        if (reason.Contains("alarm", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("telemetry", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("报警", StringComparison.OrdinalIgnoreCase))
        {
            return new EquipmentReadinessClassification(
                EquipmentUnavailable,
                "IndustrialTelemetry",
                "工业遥测存在未解除报警，设备不可用于当前工序。",
                "处理并解除设备报警后重新检查");
        }

        return new EquipmentReadinessClassification(
            EquipmentUnavailable,
            "BusinessMes",
            "MES 停机记录显示设备或工作中心当前不可用。",
            "关闭停机事件、选择替代设备或调整派工时间");
    }
}

public sealed record EquipmentReadinessClassification(
    string Code,
    string SourceSystem,
    string Message,
    string FixHint);
