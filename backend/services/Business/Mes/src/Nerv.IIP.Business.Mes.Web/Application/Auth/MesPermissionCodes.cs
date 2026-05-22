namespace Nerv.IIP.Business.Mes.Web.Application.Auth;

public static class MesPermissionCodes
{
    public const string WorkOrdersManage = "business.mes.work-orders.manage";
    public const string SchedulesManage = "business.mes.schedules.manage";

    public static readonly IReadOnlyCollection<string> All =
    [
        WorkOrdersManage,
        SchedulesManage
    ];
}
