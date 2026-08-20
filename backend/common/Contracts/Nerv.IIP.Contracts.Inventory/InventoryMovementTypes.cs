namespace Nerv.IIP.Contracts.Inventory;

public static class InventoryMovementTypes
{
    public const string Inbound = "inbound";
    public const string Outbound = "outbound";
    public const string Transfer = "transfer";
    public const string Adjustment = "adjustment";
    public const string CountAdjustment = "count-adjustment";
    public const string StatusTransferOut = "status-transfer-out";
    public const string StatusTransferIn = "status-transfer-in";
}
