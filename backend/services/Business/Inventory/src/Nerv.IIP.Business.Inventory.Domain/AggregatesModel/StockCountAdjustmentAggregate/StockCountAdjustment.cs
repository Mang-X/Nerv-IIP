using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockCountTaskAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockMovementAggregate;

namespace Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockCountAdjustmentAggregate;

public partial record StockCountAdjustmentId : IGuidStronglyTypedId;

public sealed class StockCountAdjustment : Entity<StockCountAdjustmentId>, IAggregateRoot
{
    private StockCountAdjustment()
    {
    }

    private StockCountAdjustment(StockCountTask task, StockMovement movement, string idempotencyKey)
    {
        CountTaskCode = StockMovement.Required(task.CountTaskCode);
        OrganizationId = StockMovement.Required(task.OrganizationId);
        EnvironmentId = StockMovement.Required(task.EnvironmentId);
        IdempotencyKey = StockMovement.Required(idempotencyKey);
        MovementId = movement.Id?.ToString() ?? string.Empty;
        SkuCode = StockMovement.Required(task.SkuCode);
        UomCode = StockMovement.Required(task.UomCode);
        SiteCode = StockMovement.Required(task.SiteCode);
        LocationCode = StockMovement.Required(task.LocationCode);
        LotNo = StockMovement.Optional(task.LotNo);
        SerialNo = StockMovement.Optional(task.SerialNo);
        QualityStatus = StockMovement.Required(task.QualityStatus);
        OwnerType = StockMovement.Required(task.OwnerType);
        OwnerId = StockMovement.Optional(task.OwnerId);
        CountedQuantity = task.CountedQuantity ?? throw new InvalidOperationException("Count task has no counted quantity.");
        VarianceQuantity = task.VarianceQuantity ?? throw new InvalidOperationException("Count task has no variance quantity.");
        ConfirmedAtUtc = DateTime.UtcNow;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string CountTaskCode { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string MovementId { get; private set; } = string.Empty;
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
    public DateTime ConfirmedAtUtc { get; private set; }

    public static StockCountAdjustment Record(StockCountTask task, StockMovement movement, string idempotencyKey)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(movement);
        return new StockCountAdjustment(task, movement, idempotencyKey);
    }
}
