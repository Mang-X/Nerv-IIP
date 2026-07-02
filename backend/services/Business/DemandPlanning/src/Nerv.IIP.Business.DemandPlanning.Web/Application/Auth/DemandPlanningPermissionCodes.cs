namespace Nerv.IIP.Business.DemandPlanning.Web.Application.Auth;

public static class DemandPlanningPermissionCodes
{
    public const string DemandsRead = "business.planning.demands.read";
    public const string DemandsManage = "business.planning.demands.manage";
    public const string MpsRead = "business.planning.mps.read";
    public const string MpsManage = "business.planning.mps.manage";
    public const string MpsRelease = "business.planning.mps.release";
    public const string MrpRead = "business.planning.mrp.read";
    public const string MrpRun = "business.planning.mrp.run";
    public const string SuggestionsManage = "business.planning.suggestions.manage";

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
