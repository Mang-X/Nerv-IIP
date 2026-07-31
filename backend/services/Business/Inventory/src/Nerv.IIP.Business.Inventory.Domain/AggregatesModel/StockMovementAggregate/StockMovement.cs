using Nerv.IIP.Business.Inventory.Domain.DomainEvents;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel;

namespace Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockMovementAggregate;

public partial record StockMovementId : IGuidStronglyTypedId;

public sealed class StockMovement : Entity<StockMovementId>, IAggregateRoot
{
    private static readonly HashSet<string> SupportedMovementTypes =
    [
        "inbound",
        "outbound",
        "transfer",
        "adjustment",
        "count-adjustment",
        "status-transfer-out",
        "status-transfer-in",
    ];

    private StockMovement()
    {
    }

    private StockMovement(
        string organizationId,
        string environmentId,
        string movementType,
        string sourceService,
        string sourceDocumentId,
        string? sourceDocumentLineId,
        string idempotencyKey,
        string skuCode,
        string uomCode,
        string siteCode,
        string locationCode,
        string? lotNo,
        string? serialNo,
        string qualityStatus,
        string ownerType,
        string? ownerId,
        decimal quantity,
        decimal? unitCost,
        DateOnly? productionDate,
        DateOnly? expiryDate)
    {
        OrganizationId = InventoryText.Required(organizationId);
        EnvironmentId = InventoryText.Required(environmentId);
        MovementType = InventoryText.Supported(movementType, SupportedMovementTypes, nameof(movementType));
        SourceService = InventoryText.Required(sourceService);
        SourceDocumentId = InventoryText.Required(sourceDocumentId);
        SourceDocumentLineId = InventoryText.Optional(sourceDocumentLineId);
        IdempotencyKey = InventoryText.Required(idempotencyKey);
        SkuCode = InventoryText.Required(skuCode);
        UomCode = InventoryText.Required(uomCode);
        SiteCode = InventoryText.Required(siteCode);
        LocationCode = InventoryText.Required(locationCode);
        LotNo = InventoryText.Optional(lotNo);
        SerialNo = InventoryText.Optional(serialNo);
        QualityStatus = StockQualityStatus.Normalize(qualityStatus);
        OwnerType = StockOwnerType.Normalize(ownerType);
        OwnerId = InventoryText.Optional(ownerId);
        ProductionDate = productionDate;
        ExpiryDate = expiryDate;
        Quantity = NonZero(quantity, nameof(quantity));
        RequestedUnitCost = unitCost is null ? null : NonNegative(unitCost.Value, nameof(unitCost));
        UnitCost = RequestedUnitCost;
        MovementAmount = UnitCost * Quantity;
        PostedAtUtc = DateTime.UtcNow;
        this.AddDomainEvent(new StockMovementPostedDomainEvent(this));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string MovementType { get; private set; } = string.Empty;
    public string SourceService { get; private set; } = string.Empty;
    public string SourceDocumentId { get; private set; } = string.Empty;
    public string? SourceDocumentLineId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string SkuCode { get; private set; } = string.Empty;
    public string UomCode { get; private set; } = string.Empty;
    public string SiteCode { get; private set; } = string.Empty;
    public string LocationCode { get; private set; } = string.Empty;
    public string? LotNo { get; private set; }
    public string? SerialNo { get; private set; }
    public string QualityStatus { get; private set; } = string.Empty;
    public string OwnerType { get; private set; } = string.Empty;
    public string? OwnerId { get; private set; }
    public DateOnly? ProductionDate { get; private set; }
    public DateOnly? ExpiryDate { get; private set; }
    public decimal Quantity { get; private set; }

    /// <summary>
    /// 调用方随请求提交的单位成本原始事实，落库后永不改写；为 null 表示调用方未指定、由台账派生。
    /// 幂等重放的载荷比较只认这一列，<see cref="UnitCost"/> 不参与——后者是派生结果，见 <see cref="ApplyValuation"/>。
    /// </summary>
    public decimal? RequestedUnitCost { get; private set; }

    /// <summary>
    /// 生效单位成本：出库一律被 <see cref="ApplyValuation"/> 用台账移动平均成本改写，入库沿用调用方值或回落移动平均。
    /// 这是派生事实，不是调用方载荷。
    /// </summary>
    public decimal? UnitCost { get; private set; }
    public decimal? MovementAmount { get; private set; }
    public DateTime PostedAtUtc { get; private set; }

    public static StockMovement Post(
        string organizationId,
        string environmentId,
        string movementType,
        string sourceService,
        string sourceDocumentId,
        string? sourceDocumentLineId,
        string idempotencyKey,
        string skuCode,
        string uomCode,
        string siteCode,
        string locationCode,
        string? lotNo,
        string? serialNo,
        string qualityStatus,
        string ownerType,
        string? ownerId,
        decimal quantity,
        decimal? unitCost = null,
        DateOnly? ProductionDate = null,
        DateOnly? ExpiryDate = null)
    {
        return new StockMovement(
            organizationId,
            environmentId,
            movementType,
            sourceService,
            sourceDocumentId,
            sourceDocumentLineId,
            idempotencyKey,
            skuCode,
            uomCode,
            siteCode,
            locationCode,
            lotNo,
            serialNo,
            qualityStatus,
            ownerType,
            ownerId,
            quantity,
            unitCost,
            ProductionDate,
            ExpiryDate);
    }

    public void ApplyValuation(decimal unitCost)
    {
        var valuationUnitCost = NonNegative(unitCost, nameof(unitCost));
        if (UnitCost is not null || Quantity < 0)
        {
            UnitCost = valuationUnitCost;
        }

        MovementAmount = valuationUnitCost * Quantity;
    }

    /// <summary>
    /// 幂等重放判定：逐字段比较「调用方载荷」，任一字段不同即为 IDEMPOTENCY_CONFLICT。
    /// 只比较调用方能决定的事实——<see cref="UnitCost"/> 与 <see cref="MovementAmount"/> 是
    /// <see cref="ApplyValuation"/> 落库前改写的派生结果，拿它跟重放请求比会造成假冲突，因此比较
    /// <see cref="RequestedUnitCost"/>。反过来也不能整体跳过成本比较：调拨的入库腿由调用方 UnitCost 定价，
    /// 而调拨幂等只查出库腿，跳过就会把「改了成本的重放」静默判成幂等成功。
    /// </summary>
    public bool HasSamePayload(StockMovement other)
    {
        return OrganizationId == other.OrganizationId
            && EnvironmentId == other.EnvironmentId
            && MovementType == other.MovementType
            && SourceService == other.SourceService
            && SourceDocumentId == other.SourceDocumentId
            && SourceDocumentLineId == other.SourceDocumentLineId
            && IdempotencyKey == other.IdempotencyKey
            && SkuCode == other.SkuCode
            && UomCode == other.UomCode
            && SiteCode == other.SiteCode
            && LocationCode == other.LocationCode
            && LotNo == other.LotNo
            && SerialNo == other.SerialNo
            && QualityStatus == other.QualityStatus
            && OwnerType == other.OwnerType
            && OwnerId == other.OwnerId
            && ProductionDate == other.ProductionDate
            && ExpiryDate == other.ExpiryDate
            && Quantity == other.Quantity
            && RequestedUnitCost == other.RequestedUnitCost;
    }

    private static decimal NonZero(decimal value, string parameterName)
    {
        return value == 0 ? throw new ArgumentOutOfRangeException(parameterName, "Quantity cannot be zero.") : value;
    }

    private static decimal NonNegative(decimal value, string parameterName)
    {
        return value < 0 ? throw new ArgumentOutOfRangeException(parameterName, "Unit cost cannot be negative.") : value;
    }
}
