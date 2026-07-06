namespace Nerv.IIP.Business.Inventory.Web.Application.Auth;

public static class InventoryPermissionCodes
{
    public const string LocationsManage = "business.inventory.locations.manage";
    public const string MovementsCreate = "business.inventory.movements.create";
    public const string LedgerRead = "business.inventory.ledger.read";
    public const string CountsManage = "business.inventory.counts.manage";
    public const string ReservationsManage = "business.inventory.reservations.manage";
    public const string ExpiredStockOverride = "business.inventory.expired-stock.override";

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
