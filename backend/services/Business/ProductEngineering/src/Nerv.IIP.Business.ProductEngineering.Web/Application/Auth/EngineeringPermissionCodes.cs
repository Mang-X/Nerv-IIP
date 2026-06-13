namespace Nerv.IIP.Business.ProductEngineering.Web.Application.Auth;

public static class EngineeringPermissionCodes
{
    public const string DocumentsRead = "business.engineering.documents.read";
    public const string DocumentsManage = "business.engineering.documents.manage";
    public const string ItemsRead = "business.engineering.items.read";
    public const string ItemsManage = "business.engineering.items.manage";
    public const string BomsRead = "business.engineering.boms.read";
    public const string BomsManage = "business.engineering.boms.manage";
    public const string RoutingsRead = "business.engineering.routings.read";
    public const string RoutingsManage = "business.engineering.routings.manage";
    public const string StandardOperationsRead = "business.engineering.standard-operations.read";
    public const string StandardOperationsManage = "business.engineering.standard-operations.manage";
    public const string ChangesRead = "business.engineering.changes.read";
    public const string ChangesManage = "business.engineering.changes.manage";
    public const string ProductionVersionsRead = "business.engineering.production-versions.read";
    public const string ProductionVersionsManage = "business.engineering.production-versions.manage";

    public static readonly IReadOnlyCollection<string> All =
    [
        DocumentsRead,
        DocumentsManage,
        ItemsRead,
        ItemsManage,
        BomsRead,
        BomsManage,
        RoutingsRead,
        RoutingsManage,
        StandardOperationsRead,
        StandardOperationsManage,
        ChangesRead,
        ChangesManage,
        ProductionVersionsRead,
        ProductionVersionsManage
    ];
}
