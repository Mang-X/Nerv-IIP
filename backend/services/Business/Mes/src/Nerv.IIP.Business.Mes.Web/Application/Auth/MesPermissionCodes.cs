using Nerv.IIP.Contracts.Iam;

namespace Nerv.IIP.Business.Mes.Web.Application.Auth;

public static class MesPermissionCodes
{
    public const string FoundationRead = NervIipPermissionCodes.MesFoundationRead;
    public const string OverviewRead = NervIipPermissionCodes.MesOverviewRead;
    public const string PlansRead = NervIipPermissionCodes.MesPlansRead;
    public const string WorkOrdersRead = NervIipPermissionCodes.MesWorkOrdersRead;
    public const string WorkOrdersManage = NervIipPermissionCodes.MesWorkOrdersManage;
    public const string MaterialsRead = NervIipPermissionCodes.MesMaterialsRead;
    public const string MaterialsManage = NervIipPermissionCodes.MesMaterialsManage;
    public const string DispatchRead = NervIipPermissionCodes.MesDispatchRead;
    public const string DispatchManage = NervIipPermissionCodes.MesDispatchManage;
    public const string OperationsRead = NervIipPermissionCodes.MesOperationsRead;
    public const string OperationsManage = NervIipPermissionCodes.MesOperationsManage;
    public const string ReportingRead = NervIipPermissionCodes.MesReportingRead;
    public const string ReportingWrite = NervIipPermissionCodes.MesReportingWrite;
    public const string QualityRead = NervIipPermissionCodes.MesQualityRead;
    public const string QualityWrite = NervIipPermissionCodes.MesQualityWrite;
    public const string ReceiptsRead = NervIipPermissionCodes.MesReceiptsRead;
    public const string ReceiptsManage = NervIipPermissionCodes.MesReceiptsManage;
    public const string DowntimeRead = NervIipPermissionCodes.MesDowntimeRead;
    public const string DowntimeManage = NervIipPermissionCodes.MesDowntimeManage;
    public const string HandoversRead = NervIipPermissionCodes.MesHandoversRead;
    public const string HandoversManage = NervIipPermissionCodes.MesHandoversManage;
    public const string TraceabilityRead = NervIipPermissionCodes.MesTraceabilityRead;
    public const string SchedulesRead = NervIipPermissionCodes.MesSchedulesRead;
    public const string SchedulesManage = NervIipPermissionCodes.MesSchedulesManage;
    public const string CapacityRead = NervIipPermissionCodes.MesCapacityRead;

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
