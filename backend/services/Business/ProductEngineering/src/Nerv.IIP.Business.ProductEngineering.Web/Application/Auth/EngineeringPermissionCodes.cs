using Nerv.IIP.Contracts.Iam;

namespace Nerv.IIP.Business.ProductEngineering.Web.Application.Auth;

public static class EngineeringPermissionCodes
{
    public const string DocumentsRead = NervIipPermissionCodes.EngineeringDocumentsRead;
    public const string DocumentsManage = NervIipPermissionCodes.EngineeringDocumentsManage;
    public const string ItemsRead = NervIipPermissionCodes.EngineeringItemsRead;
    public const string ItemsManage = NervIipPermissionCodes.EngineeringItemsManage;
    public const string BomsRead = NervIipPermissionCodes.EngineeringBomsRead;
    public const string BomsManage = NervIipPermissionCodes.EngineeringBomsManage;
    public const string RoutingsRead = NervIipPermissionCodes.EngineeringRoutingsRead;
    public const string RoutingsManage = NervIipPermissionCodes.EngineeringRoutingsManage;
    public const string StandardOperationsRead = NervIipPermissionCodes.EngineeringStandardOperationsRead;
    public const string StandardOperationsManage = NervIipPermissionCodes.EngineeringStandardOperationsManage;
    public const string ChangesRead = NervIipPermissionCodes.EngineeringChangesRead;
    public const string ChangesManage = NervIipPermissionCodes.EngineeringChangesManage;
    public const string ProductionVersionsRead = NervIipPermissionCodes.EngineeringProductionVersionsRead;
    public const string ProductionVersionsManage = NervIipPermissionCodes.EngineeringProductionVersionsManage;

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
