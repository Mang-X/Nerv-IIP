using Nerv.IIP.Business.Mes.Domain.DomainEvents;

namespace Nerv.IIP.Business.Mes.Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate;

public partial record FinishedGoodsReceiptRequestId : IGuidStronglyTypedId;

public sealed class FinishedGoodsReceiptRequest : Entity<FinishedGoodsReceiptRequestId>, IAggregateRoot
{
    public const string RequestedStatus = "Requested";
    public const string PartiallyPostedStatus = "PartiallyPosted";
    public const string PostedStatus = "Posted";
    public const string InventoryPostingFailedStatus = "InventoryPostingFailed";
    public const string CancelledStatus = "Cancelled";
    public const int FailureMessageMaxLength = 500;
    public const decimal QuantityTolerance = 0.000001m;

    private FinishedGoodsReceiptRequest()
    {
    }

    private FinishedGoodsReceiptRequest(
        string organizationId,
        string environmentId,
        string requestNo,
        string workOrderId,
        string skuId,
        decimal quantity,
        string uomCode,
        DateTimeOffset requestedAtUtc,
        decimal? unitCost,
        string? producedLotNo,
        string? serialNo)
    {
        OrganizationId = DomainGuard.Required(organizationId, nameof(organizationId));
        EnvironmentId = DomainGuard.Required(environmentId, nameof(environmentId));
        RequestNo = DomainGuard.Required(requestNo, nameof(requestNo));
        WorkOrderId = DomainGuard.Required(workOrderId, nameof(workOrderId));
        SkuId = DomainGuard.Required(skuId, nameof(skuId));
        Quantity = DomainGuard.Positive(quantity, nameof(quantity));
        UomCode = DomainGuard.Required(uomCode, nameof(uomCode));
        RequestedAtUtc = requestedAtUtc;
        UnitCost = unitCost is null ? null : DomainGuard.Positive(unitCost.Value, nameof(unitCost));
        ProducedLotNo = string.IsNullOrWhiteSpace(producedLotNo) ? null : producedLotNo.Trim();
        SerialNo = string.IsNullOrWhiteSpace(serialNo) ? null : serialNo.Trim();
        Status = RequestedStatus;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string RequestNo { get; private set; } = string.Empty;
    public string WorkOrderId { get; private set; } = string.Empty;
    public string SkuId { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public string UomCode { get; private set; } = string.Empty;
    public DateTimeOffset RequestedAtUtc { get; private set; }
    public decimal? UnitCost { get; private set; }
    public string? ProducedLotNo { get; private set; }
    public string? SerialNo { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public decimal PostedQuantity { get; private set; }
    public decimal RemainingQuantity => Math.Max(0m, Quantity - PostedQuantity);
    public string? PostedInventoryMovementId { get; private set; }
    public DateTimeOffset? PostedAtUtc { get; private set; }
    public string? InventoryPostingFailureCode { get; private set; }
    public string? InventoryPostingFailureMessage { get; private set; }
    public DateTimeOffset? InventoryPostingFailedAtUtc { get; private set; }

    public static FinishedGoodsReceiptRequest Create(
        string organizationId,
        string environmentId,
        string requestNo,
        string workOrderId,
        string skuId,
        decimal quantity,
        string uomCode,
        DateTimeOffset requestedAtUtc,
        string? producedLotNo = null,
        string? serialNo = null,
        decimal? unitCost = null)
    {
        var request = new FinishedGoodsReceiptRequest(
            organizationId,
            environmentId,
            requestNo,
            workOrderId,
            skuId,
            quantity,
            uomCode,
            requestedAtUtc,
            unitCost,
            producedLotNo,
            serialNo);
        request.AddDomainEvent(new FinishedGoodsReceiptRequestedDomainEvent(
            request,
            request.Quantity,
            BuildInventoryPostingIdempotencyKey(organizationId, environmentId, requestNo)));
        return request;
    }

    public void MarkPosted(string inventoryMovementId, DateTimeOffset postedAtUtc)
    {
        MarkInventoryPosted(inventoryMovementId, RemainingQuantity, postedAtUtc);
    }

    public void MarkInventoryPosted(string inventoryMovementId, decimal postedQuantity, DateTimeOffset postedAtUtc)
    {
        if (Status == CancelledStatus)
        {
            return;
        }

        var normalizedQuantity = DomainGuard.Positive(postedQuantity, nameof(postedQuantity));
        if (PostedQuantity + normalizedQuantity > Quantity + QuantityTolerance)
        {
            throw new InvalidOperationException("Inventory posted quantity exceeds the MES finished-goods receipt request quantity.");
        }

        PostedQuantity = PostedQuantity + normalizedQuantity >= Quantity - QuantityTolerance
            ? Quantity
            : PostedQuantity + normalizedQuantity;
        PostedInventoryMovementId = DomainGuard.Required(inventoryMovementId, nameof(inventoryMovementId));
        PostedAtUtc = postedAtUtc;
        Status = RemainingQuantity <= QuantityTolerance ? PostedStatus : PartiallyPostedStatus;
        InventoryPostingFailureCode = null;
        InventoryPostingFailureMessage = null;
        InventoryPostingFailedAtUtc = null;
    }

    public void MarkInventoryPostingFailed(string failureCode, string failureMessage, DateTimeOffset failedAtUtc)
    {
        if (Status == PostedStatus || Status == CancelledStatus)
        {
            return;
        }

        Status = InventoryPostingFailedStatus;
        if (PostedQuantity <= QuantityTolerance)
        {
            PostedInventoryMovementId = null;
            PostedAtUtc = null;
        }

        InventoryPostingFailureCode = DomainGuard.Required(failureCode, nameof(failureCode));
        InventoryPostingFailureMessage = NormalizeFailureMessage(failureMessage);
        InventoryPostingFailedAtUtc = failedAtUtc;
    }

    public void RetryInventoryPosting(string idempotencyKey)
    {
        if (Status != InventoryPostingFailedStatus)
        {
            throw new InvalidOperationException("Only failed finished-goods receipt requests can retry Inventory posting.");
        }

        if (RemainingQuantity <= QuantityTolerance)
        {
            throw new InvalidOperationException("Finished-goods receipt request has no remaining quantity to post.");
        }

        Status = PostedQuantity > 0m ? PartiallyPostedStatus : RequestedStatus;
        InventoryPostingFailureCode = null;
        InventoryPostingFailureMessage = null;
        InventoryPostingFailedAtUtc = null;
        AddDomainEvent(new FinishedGoodsReceiptRequestedDomainEvent(
            this,
            RemainingQuantity,
            BuildInventoryPostingRetryIdempotencyKey(OrganizationId, EnvironmentId, RequestNo, idempotencyKey)));
    }

    public void Cancel()
    {
        if (Status == PostedStatus)
        {
            return;
        }

        Status = CancelledStatus;
        PostedInventoryMovementId = null;
        PostedAtUtc = null;
        InventoryPostingFailureCode = null;
        InventoryPostingFailureMessage = null;
        InventoryPostingFailedAtUtc = null;
    }

    private static string BuildInventoryPostingIdempotencyKey(string organizationId, string environmentId, string requestNo)
    {
        return $"mes:finished-goods-receipt:{organizationId}:{environmentId}:{requestNo}";
    }

    private static string BuildInventoryPostingRetryIdempotencyKey(
        string organizationId,
        string environmentId,
        string requestNo,
        string idempotencyKey)
    {
        return $"{BuildInventoryPostingIdempotencyKey(organizationId, environmentId, requestNo)}:{DomainGuard.Required(idempotencyKey, nameof(idempotencyKey))}";
    }

    private static string NormalizeFailureMessage(string failureMessage)
    {
        var normalized = DomainGuard.Required(failureMessage, nameof(failureMessage));
        return normalized.Length <= FailureMessageMaxLength
            ? normalized
            : normalized[..FailureMessageMaxLength];
    }
}
