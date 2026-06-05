namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Auth;

public static class IndustrialTelemetryPermissionCodes
{
    public const string TagsManage = "business.iiot.tags.manage";
    public const string AlarmRulesManage = "business.iiot.alarm-rules.manage";
    public const string TelemetryRead = "business.iiot.telemetry.read";
    public const string TelemetryWrite = "business.iiot.telemetry.write";
    public const string AlarmsRead = "business.iiot.alarms.read";
    public const string AlarmsWrite = "business.iiot.alarms.write";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        TagsManage,
        TelemetryRead,
        TelemetryWrite,
        AlarmsRead,
        AlarmsWrite,
    };
}
