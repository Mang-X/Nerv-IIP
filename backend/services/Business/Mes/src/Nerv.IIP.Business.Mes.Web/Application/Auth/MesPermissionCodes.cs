namespace Nerv.IIP.Business.Mes.Web.Application.Auth;

public static class MesPermissionCodes
{
    public const string FoundationRead = "business.mes.foundation.read";
    public const string OverviewRead = "business.mes.overview.read";
    public const string WorkOrdersRead = "business.mes.work-orders.read";
    public const string WorkOrdersManage = "business.mes.work-orders.manage";
    public const string MaterialsRead = "business.mes.materials.read";
    public const string OperationsRead = "business.mes.operations.read";
    public const string ReportingRead = "business.mes.reporting.read";
    public const string ReportingWrite = "business.mes.reporting.write";
    public const string ReceiptsRead = "business.mes.receipts.read";
    public const string ReceiptsManage = "business.mes.receipts.manage";
    public const string SchedulesManage = "business.mes.schedules.manage";
    public const string CapacityRead = "business.mes.capacity.read";

    public static readonly IReadOnlyCollection<string> All =
    [
        FoundationRead,
        OverviewRead,
        WorkOrdersRead,
        WorkOrdersManage,
        MaterialsRead,
        OperationsRead,
        ReportingRead,
        ReportingWrite,
        ReceiptsRead,
        ReceiptsManage,
        SchedulesManage,
        CapacityRead
    ];
}
