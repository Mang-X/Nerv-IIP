namespace Nerv.IIP.Business.ProductEngineering.Web.Application.Auth;

public static class EngineeringPermissionCodes
{
    public const string ProductionVersionsRead = "business.engineering.production-versions.read";
    public const string ProductionVersionsManage = "business.engineering.production-versions.manage";

    public static readonly IReadOnlyCollection<string> All =
    [
        ProductionVersionsRead,
        ProductionVersionsManage
    ];
}
