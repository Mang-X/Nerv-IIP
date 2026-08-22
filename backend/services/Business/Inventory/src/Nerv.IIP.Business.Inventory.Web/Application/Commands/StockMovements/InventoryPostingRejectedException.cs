using Nerv.IIP.Business.Inventory.Domain.AggregatesModel;

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

    /// <summary>调拨（transfer）两腿缺腿或数量不配平：整笔拒绝，避免单腿过账凭空增减库存。</summary>
    public const string TransferLegsUnbalanced = "TRANSFER_LEGS_UNBALANCED";
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

    public static InventoryPostingRejectedException FromDomain(InventoryDomainException exception)
    {
        return new InventoryPostingRejectedException(
            ResolveDomainFailureCode(exception.Reason),
            ResolveDomainFailureMessage(exception.Reason),
            exception);
    }

    private static string NormalizeFailureCode(string failureCode)
    {
        return string.IsNullOrWhiteSpace(failureCode)
            ? InventoryPostingFailureCodes.PostingRejected
            : failureCode;
    }

    private static string ResolveDomainFailureCode(InventoryDomainFailureReason reason)
    {
        return reason switch
        {
            InventoryDomainFailureReason.PostingRejected => InventoryPostingFailureCodes.PostingRejected,
            InventoryDomainFailureReason.NegativeOnHand => InventoryPostingFailureCodes.NegativeOnHand,
            InventoryDomainFailureReason.IdempotencyConflict => InventoryPostingFailureCodes.IdempotencyConflict,
            InventoryDomainFailureReason.DimensionMismatch => InventoryPostingFailureCodes.DimensionMismatch,
            InventoryDomainFailureReason.LedgerFrozen => InventoryPostingFailureCodes.LedgerFrozen,
            InventoryDomainFailureReason.ReservationAllocationRejected => InventoryPostingFailureCodes.ReservationAllocationRejected,
            InventoryDomainFailureReason.CommittedStockProtection => InventoryPostingFailureCodes.ReservationAllocationRejected,
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "未处理的库存过账失败原因。"),
        };
    }

    private static string ResolveDomainFailureMessage(InventoryDomainFailureReason reason)
    {
        return reason switch
        {
            InventoryDomainFailureReason.PostingRejected => "库存过账被拒绝，请核对库存状态后重试。",
            InventoryDomainFailureReason.NegativeOnHand => "库存数量不足，不能完成过账。",
            InventoryDomainFailureReason.IdempotencyConflict => "库存移动幂等键与已有流水冲突，请更换幂等键。",
            InventoryDomainFailureReason.DimensionMismatch => "库存移动维度与现有台账不一致，请核对物料、库位和批次。",
            InventoryDomainFailureReason.LedgerFrozen => "库存台账已冻结，当前操作无法过账。",
            InventoryDomainFailureReason.ReservationAllocationRejected => "库存预留分配被拒绝，请刷新库存后重试。",
            InventoryDomainFailureReason.CommittedStockProtection => "出库数量超过未预留的可用库存，不能完成过账。",
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "未处理的库存过账失败原因。"),
        };
    }
}
