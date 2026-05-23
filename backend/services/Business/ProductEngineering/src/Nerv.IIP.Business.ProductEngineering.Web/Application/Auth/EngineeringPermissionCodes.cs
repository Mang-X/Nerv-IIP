namespace Nerv.IIP.Business.ProductEngineering.Web.Application.Auth;

public static class EngineeringPermissionCodes
{
    public const string DocumentsRead = "business.engineering.documents.read";
    public const string DocumentsManage = "business.engineering.documents.manage";
    public const string BomsRead = "business.engineering.boms.read";
    public const string BomsManage = "business.engineering.boms.manage";
    public const string ChangesRead = "business.engineering.changes.read";
    public const string ChangesManage = "business.engineering.changes.manage";
    public const string ProductionVersionsRead = "business.engineering.production-versions.read";
    public const string ProductionVersionsManage = "business.engineering.production-versions.manage";

    public static readonly IReadOnlyCollection<string> All =
    [
        DocumentsRead,
        DocumentsManage,
        BomsRead,
        BomsManage,
        ChangesRead,
        ChangesManage,
        ProductionVersionsRead,
        ProductionVersionsManage
    ];
}
