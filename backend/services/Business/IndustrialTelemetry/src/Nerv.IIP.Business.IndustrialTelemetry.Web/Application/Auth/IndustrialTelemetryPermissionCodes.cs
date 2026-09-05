using Nerv.IIP.Contracts.Iam;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Auth;

public static class IndustrialTelemetryPermissionCodes
{
    public const string TagsManage = NervIipPermissionCodes.IiotTagsManage;
    public const string AlarmRulesManage = NervIipPermissionCodes.IiotAlarmRulesManage;
    public const string TelemetryRead = NervIipPermissionCodes.IiotTelemetryRead;
    public const string TelemetryWrite = NervIipPermissionCodes.IiotTelemetryWrite;
    public const string DeviceControlWrite = NervIipPermissionCodes.IiotDeviceControlWrite;
    public const string DeviceControlManage = NervIipPermissionCodes.IiotDeviceControlManage;
    public const string DeviceControlRead = NervIipPermissionCodes.IiotDeviceControlRead;
    public const string AlarmsRead = NervIipPermissionCodes.IiotAlarmsRead;
    public const string AlarmsWrite = NervIipPermissionCodes.IiotAlarmsWrite;

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        TagsManage,
        AlarmRulesManage,
        TelemetryRead,
        TelemetryWrite,
        DeviceControlWrite,
        DeviceControlManage,
        DeviceControlRead,
        AlarmsRead,
        AlarmsWrite,
    };
}
