using Nerv.IIP.Contracts.Iam;

namespace Nerv.IIP.Business.DemandPlanning.Web.Application.Auth;

public static class DemandPlanningPermissionCodes
{
    public const string DemandsRead = NervIipPermissionCodes.PlanningDemandsRead;
    public const string DemandsManage = NervIipPermissionCodes.PlanningDemandsManage;
    public const string MpsRead = NervIipPermissionCodes.PlanningMpsRead;
    public const string MpsManage = NervIipPermissionCodes.PlanningMpsManage;
    public const string MpsRelease = NervIipPermissionCodes.PlanningMpsRelease;
    public const string MrpRead = NervIipPermissionCodes.PlanningMrpRead;
    public const string MrpRun = NervIipPermissionCodes.PlanningMrpRun;
    public const string SuggestionsManage = NervIipPermissionCodes.PlanningSuggestionsManage;

    public static readonly IReadOnlyCollection<string> All =
    [
        DemandsRead,
        DemandsManage,
        MpsRead,
        MpsManage,
        MpsRelease,
        MrpRead,
        MrpRun,
        SuggestionsManage,
    ];
}
