using Nerv.IIP.Contracts.Iam;

namespace Nerv.IIP.Business.Maintenance.Web.Application.Auth;

public static class MaintenancePermissionCodes
{
    public const string WorkOrdersRead = NervIipPermissionCodes.MaintenanceWorkOrdersRead;
    public const string WorkOrdersManage = NervIipPermissionCodes.MaintenanceWorkOrdersManage;
    public const string PlansRead = NervIipPermissionCodes.MaintenancePlansRead;
    public const string PlansManage = NervIipPermissionCodes.MaintenancePlansManage;
    public const string DowntimeReasonsRead = NervIipPermissionCodes.MaintenanceDowntimeReasonsRead;
}
