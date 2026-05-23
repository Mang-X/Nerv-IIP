namespace Nerv.IIP.Business.DemandPlanning.Web.Application.Auth;

public static class DemandPlanningPermissionCodes
{
    public const string DemandsRead = "business.planning.demands.read";
    public const string DemandsManage = "business.planning.demands.manage";
    public const string MrpRead = "business.planning.mrp.read";
    public const string MrpRun = "business.planning.mrp.run";
    public const string SuggestionsManage = "business.planning.suggestions.manage";

    public static readonly IReadOnlyCollection<string> All =
    [
        DemandsRead,
        DemandsManage,
        MrpRead,
        MrpRun,
        SuggestionsManage,
    ];
}
