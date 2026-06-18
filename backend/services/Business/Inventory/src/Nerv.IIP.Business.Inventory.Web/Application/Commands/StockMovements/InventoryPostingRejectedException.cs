namespace Nerv.IIP.Business.Inventory.Web.Application.Commands.StockMovements;

public static class InventoryPostingFailureCodes
{
    public const string PostingRejected = "POSTING_REJECTED";
    public const string NegativeOnHand = "NEGATIVE_ON_HAND";
    public const string IdempotencyConflict = "IDEMPOTENCY_CONFLICT";
    public const string DimensionMismatch = "DIMENSION_MISMATCH";
    public const string LedgerFrozen = "LEDGER_FROZEN";
    public const string ReservationNotFound = "RESERVATION_NOT_FOUND";
    public const string ReservationAllocationRejected = "RESERVATION_ALLOCATION_REJECTED";
    public const string InvalidReservationId = "INVALID_RESERVATION_ID";
}

public sealed class InventoryPostingRejectedException : KnownException
{
    public InventoryPostingRejectedException(string failureCode, string failureMessage)
        : base(failureMessage)
    {
        FailureCode = NormalizeFailureCode(failureCode);
        FailureMessage = failureMessage;
    }

    public InventoryPostingRejectedException(string failureCode, string failureMessage, Exception innerException)
        : base(failureMessage, innerException)
    {
        FailureCode = NormalizeFailureCode(failureCode);
        FailureMessage = failureMessage;
    }

    public string FailureCode { get; }

    public string FailureMessage { get; }

    public static InventoryPostingRejectedException FromDomain(InvalidOperationException exception)
    {
        return new InventoryPostingRejectedException(ResolveDomainFailureCode(exception.Message), exception.Message, exception);
    }

    private static string NormalizeFailureCode(string failureCode)
    {
        return string.IsNullOrWhiteSpace(failureCode)
            ? InventoryPostingFailureCodes.PostingRejected
            : failureCode;
    }

    private static string ResolveDomainFailureCode(string message)
    {
        if (message.Contains("negative", StringComparison.OrdinalIgnoreCase))
        {
            return InventoryPostingFailureCodes.NegativeOnHand;
        }

        if (message.Contains("idempotency", StringComparison.OrdinalIgnoreCase))
        {
            return InventoryPostingFailureCodes.IdempotencyConflict;
        }

        if (message.Contains("dimension", StringComparison.OrdinalIgnoreCase))
        {
            return InventoryPostingFailureCodes.DimensionMismatch;
        }

        if (message.Contains("frozen", StringComparison.OrdinalIgnoreCase))
        {
            return InventoryPostingFailureCodes.LedgerFrozen;
        }

        if (message.Contains("allocate", StringComparison.OrdinalIgnoreCase)
            || message.Contains("reservation", StringComparison.OrdinalIgnoreCase)
            || message.Contains("reserved", StringComparison.OrdinalIgnoreCase))
        {
            return InventoryPostingFailureCodes.ReservationAllocationRejected;
        }

        return InventoryPostingFailureCodes.PostingRejected;
    }
}
