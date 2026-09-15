using Nerv.IIP.Contracts.Iam;

namespace Nerv.IIP.Business.Inventory.Web.Application.Auth;

public static class InventoryPermissionCodes
{
    public const string LocationsManage = NervIipPermissionCodes.InventoryLocationsManage;
    public const string MovementsCreate = NervIipPermissionCodes.InventoryMovementsCreate;
    public const string LedgerRead = NervIipPermissionCodes.InventoryLedgerRead;
    public const string CountsManage = NervIipPermissionCodes.InventoryCountsManage;
    public const string ReservationsManage = "business.inventory.reservations.manage";
    public const string ExpiredStockOverride = NervIipPermissionCodes.InventoryExpiredStockOverride;

    public static readonly IReadOnlyCollection<string> All =
    [
        LocationsManage,
        MovementsCreate,
        LedgerRead,
        CountsManage,
        ReservationsManage,
        ExpiredStockOverride,
    ];
}
