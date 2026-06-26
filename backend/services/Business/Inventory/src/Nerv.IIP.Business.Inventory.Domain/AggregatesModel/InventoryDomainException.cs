namespace Nerv.IIP.Business.Inventory.Domain.AggregatesModel;

public enum InventoryDomainFailureReason
{
    // Default fallback for structured domain failures that do not need a more specific public failure code.
    PostingRejected,
    NegativeOnHand,
    IdempotencyConflict,
    DimensionMismatch,
    LedgerFrozen,
    ReservationAllocationRejected,
    CommittedStockProtection,
}

public sealed class InventoryDomainException(InventoryDomainFailureReason reason, string message)
    : InvalidOperationException(message)
{
    public InventoryDomainFailureReason Reason { get; } = reason;
}
