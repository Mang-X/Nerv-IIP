namespace Nerv.IIP.Business.Inventory.Domain.AggregatesModel;

public enum InventoryDomainFailureReason
{
    PostingRejected,
    NegativeOnHand,
    IdempotencyConflict,
    DimensionMismatch,
    LedgerFrozen,
    ReservationAllocationRejected,
    ReservedStockProtection,
}

public sealed class InventoryDomainException(InventoryDomainFailureReason reason, string message)
    : InvalidOperationException(message)
{
    public InventoryDomainFailureReason Reason { get; } = reason;
}
