using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockCountTaskAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockMovementAggregate;

namespace Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockCountAdjustmentAggregate;

public partial record StockCountAdjustmentId : IGuidStronglyTypedId;

public static class StockCountAdjustmentStatuses
{
    public const string PendingApproval = "pending-approval";
    public const string Posted = "posted";
    public const string Voided = "voided";
}

public sealed class StockCountAdjustment : Entity<StockCountAdjustmentId>, IAggregateRoot
{
    private StockCountAdjustment()
    {
    }

    private StockCountAdjustment(
        StockCountTask task,
        string idempotencyKey,
        string status,
        string? approvalChainId,
        StockMovement? movement,
        decimal varianceAmount)
    {
        CountTaskCode = InventoryText.Required(task.CountTaskCode);
        OrganizationId = InventoryText.Required(task.OrganizationId);
        EnvironmentId = InventoryText.Required(task.EnvironmentId);
        IdempotencyKey = InventoryText.Required(idempotencyKey);
        MovementId = movement?.Id?.ToString();
        ApprovalChainId = InventoryText.Optional(approvalChainId);
        SkuCode = InventoryText.Required(task.SkuCode);
        UomCode = InventoryText.Required(task.UomCode);
        SiteCode = InventoryText.Required(task.SiteCode);
        LocationCode = InventoryText.Required(task.LocationCode);
        LotNo = InventoryText.Optional(task.LotNo);
        SerialNo = InventoryText.Optional(task.SerialNo);
        QualityStatus = InventoryText.Required(task.QualityStatus);
        OwnerType = InventoryText.Required(task.OwnerType);
        OwnerId = InventoryText.Optional(task.OwnerId);
        CountedQuantity = task.CountedQuantity ?? throw new InvalidOperationException("Count task has no counted quantity.");
        VarianceQuantity = task.VarianceQuantity ?? throw new InvalidOperationException("Count task has no variance quantity.");
        VarianceAmount = varianceAmount;
        Status = InventoryText.Required(status);
        ConfirmedAtUtc = movement is null ? null : DateTime.UtcNow;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string CountTaskCode { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string? MovementId { get; private set; }
    public string? ApprovalChainId { get; private set; }
    public string SkuCode { get; private set; } = string.Empty;
    public string UomCode { get; private set; } = string.Empty;
    public string SiteCode { get; private set; } = string.Empty;
    public string LocationCode { get; private set; } = string.Empty;
    public string? LotNo { get; private set; }
    public string? SerialNo { get; private set; }
    public string QualityStatus { get; private set; } = string.Empty;
    public string OwnerType { get; private set; } = string.Empty;
    public string? OwnerId { get; private set; }
    public decimal CountedQuantity { get; private set; }
    public decimal VarianceQuantity { get; private set; }
    public decimal VarianceAmount { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public DateTime? ConfirmedAtUtc { get; private set; }

    public static StockCountAdjustment Record(StockCountTask task, StockMovement movement, string idempotencyKey)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(movement);
        return new StockCountAdjustment(task, idempotencyKey, StockCountAdjustmentStatuses.Posted, null, movement, Math.Abs(movement.MovementAmount ?? 0m));
    }

    public static StockCountAdjustment RecordPendingApproval(
        StockCountTask task,
        string idempotencyKey,
        string approvalChainId,
        decimal varianceAmount)
    {
        ArgumentNullException.ThrowIfNull(task);
        return new StockCountAdjustment(task, idempotencyKey, StockCountAdjustmentStatuses.PendingApproval, approvalChainId, null, varianceAmount);
    }

    public void MarkPosted(StockMovement movement)
    {
        ArgumentNullException.ThrowIfNull(movement);
        if (Status == StockCountAdjustmentStatuses.Posted)
        {
            return;
        }

        if (Status != StockCountAdjustmentStatuses.PendingApproval)
        {
            throw new InvalidOperationException("Only pending stock count adjustments can be posted.");
        }

        MovementId = movement.Id?.ToString()
            ?? throw new ArgumentException("Stock movement id must be assigned before posting a count adjustment.", nameof(movement));
        VarianceAmount = Math.Abs(movement.MovementAmount ?? 0m);
        Status = StockCountAdjustmentStatuses.Posted;
        ConfirmedAtUtc = DateTime.UtcNow;
    }

    public void VoidAfterApprovalRejection()
    {
        if (Status == StockCountAdjustmentStatuses.Voided)
        {
            return;
        }

        if (Status != StockCountAdjustmentStatuses.PendingApproval)
        {
            throw new InvalidOperationException("Only pending stock count adjustments can be voided after approval rejection.");
        }

        Status = StockCountAdjustmentStatuses.Voided;
    }
}
