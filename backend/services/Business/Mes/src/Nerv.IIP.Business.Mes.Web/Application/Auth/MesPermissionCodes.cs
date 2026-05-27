namespace Nerv.IIP.Business.Mes.Web.Application.Auth;

public static class MesPermissionCodes
{
    public const string FoundationRead = "business.mes.foundation.read";
    public const string OverviewRead = "business.mes.overview.read";
    public const string PlansRead = "business.mes.plans.read";
    public const string WorkOrdersRead = "business.mes.work-orders.read";
    public const string WorkOrdersManage = "business.mes.work-orders.manage";
    public const string MaterialsRead = "business.mes.materials.read";
    public const string MaterialsManage = "business.mes.materials.manage";
    public const string DispatchRead = "business.mes.dispatch.read";
    public const string DispatchManage = "business.mes.dispatch.manage";
    public const string OperationsRead = "business.mes.operations.read";
    public const string OperationsManage = "business.mes.operations.manage";
    public const string ReportingRead = "business.mes.reporting.read";
    public const string ReportingWrite = "business.mes.reporting.write";
    public const string QualityRead = "business.mes.quality.read";
    public const string QualityWrite = "business.mes.quality.write";
    public const string ReceiptsRead = "business.mes.receipts.read";
    public const string ReceiptsManage = "business.mes.receipts.manage";
    public const string DowntimeRead = "business.mes.downtime.read";
    public const string DowntimeManage = "business.mes.downtime.manage";
    public const string HandoversRead = "business.mes.handovers.read";
    public const string HandoversManage = "business.mes.handovers.manage";
    public const string TraceabilityRead = "business.mes.traceability.read";
    public const string SchedulesRead = "business.mes.schedules.read";
    public const string SchedulesManage = "business.mes.schedules.manage";
    public const string CapacityRead = "business.mes.capacity.read";

    public static readonly IReadOnlyCollection<string> All =
    [
        FoundationRead,
        OverviewRead,
        PlansRead,
        WorkOrdersRead,
        WorkOrdersManage,
        MaterialsRead,
        MaterialsManage,
        DispatchRead,
        DispatchManage,
        OperationsRead,
        OperationsManage,
        ReportingRead,
        ReportingWrite,
        QualityRead,
        QualityWrite,
        ReceiptsRead,
        ReceiptsManage,
        DowntimeRead,
        DowntimeManage,
        HandoversRead,
        HandoversManage,
        TraceabilityRead,
        SchedulesRead,
        SchedulesManage,
        CapacityRead
    ];
}
