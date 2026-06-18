using Nerv.IIP.Business.MasterData.Domain.DomainEvents;

namespace Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SkuAggregate;

public partial record SkuId : IGuidStronglyTypedId;

public class Sku : Entity<SkuId>, IAggregateRoot
{
    private readonly List<string> complianceTags = [];

    protected Sku()
    {
    }

    private Sku(
        string organizationId,
        string environmentId,
        string code,
        string name,
        string unit,
        string category,
        string materialType,
        string batchTrackingPolicy,
        string serialTrackingPolicy,
        string shelfLifePolicyCode,
        string storageConditionCode,
        string defaultBarcodeRuleCode,
        bool qualityRequired,
        IEnumerable<string> complianceTags,
        string? inventoryUomCode,
        string? purchaseUomCode,
        string? salesUomCode,
        string? manufacturingUomCode,
        string? procurementType,
        string? mrpType,
        string? lotSizingPolicy,
        decimal? minimumLotSize,
        decimal? maximumLotSize,
        decimal? lotSizeMultiple,
        decimal? safetyStockQuantity,
        decimal? reorderPointQuantity,
        int? plannedDeliveryTimeDays,
        int? inHouseProductionTimeDays,
        int? goodsReceiptProcessingTimeDays,
        string? abcClass,
        string? lifecycleStatus,
        bool purchasingEnabled,
        bool manufacturingEnabled,
        bool salesEnabled)
    {
        OrganizationId = Required(organizationId);
        EnvironmentId = Required(environmentId);
        Code = Required(code);
        Name = Required(name);
        Unit = Required(unit);
        BaseUomCode = Unit;
        InventoryUomCode = UomOrBase(inventoryUomCode, Unit);
        PurchaseUomCode = UomOrBase(purchaseUomCode, Unit);
        SalesUomCode = UomOrBase(salesUomCode, Unit);
        ManufacturingUomCode = UomOrBase(manufacturingUomCode, Unit);
        Category = Required(category);
        MaterialType = Required(materialType);
        BatchTrackingPolicy = Required(batchTrackingPolicy);
        SerialTrackingPolicy = Required(serialTrackingPolicy);
        ShelfLifePolicyCode = Optional(shelfLifePolicyCode);
        StorageConditionCode = Optional(storageConditionCode);
        DefaultBarcodeRuleCode = Optional(defaultBarcodeRuleCode);
        QualityRequired = qualityRequired;
        SetPlanningProfile(
            procurementType,
            mrpType,
            lotSizingPolicy,
            minimumLotSize,
            maximumLotSize,
            lotSizeMultiple,
            safetyStockQuantity,
            reorderPointQuantity,
            plannedDeliveryTimeDays,
            inHouseProductionTimeDays,
            goodsReceiptProcessingTimeDays,
            abcClass);
        LifecycleStatus = NormalizeLifecycleStatus(lifecycleStatus);
        PurchasingEnabled = purchasingEnabled;
        ManufacturingEnabled = manufacturingEnabled;
        SalesEnabled = salesEnabled;
        this.complianceTags.AddRange(complianceTags.Select(Required).Distinct(StringComparer.OrdinalIgnoreCase));
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
        this.AddDomainEvent(new MasterDataAggregateCreatedDomainEvent(nameof(Sku), OrganizationId, EnvironmentId, Code));
        this.AddDomainEvent(new SkuChangedDomainEvent(OrganizationId, EnvironmentId, Code));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Unit { get; private set; } = string.Empty;
    public string BaseUomCode { get; private set; } = string.Empty;
    public string InventoryUomCode { get; private set; } = string.Empty;
    public string PurchaseUomCode { get; private set; } = string.Empty;
    public string SalesUomCode { get; private set; } = string.Empty;
    public string ManufacturingUomCode { get; private set; } = string.Empty;
    public string Category { get; private set; } = string.Empty;
    public string MaterialType { get; private set; } = string.Empty;
    public string BatchTrackingPolicy { get; private set; } = string.Empty;
    public string SerialTrackingPolicy { get; private set; } = string.Empty;
    public string ShelfLifePolicyCode { get; private set; } = string.Empty;
    public string StorageConditionCode { get; private set; } = string.Empty;
    public string DefaultBarcodeRuleCode { get; private set; } = string.Empty;
    public bool QualityRequired { get; private set; }
    public string ProcurementType { get; private set; } = string.Empty;
    public string MrpType { get; private set; } = string.Empty;
    public string LotSizingPolicy { get; private set; } = string.Empty;
    public decimal? MinimumLotSize { get; private set; }
    public decimal? MaximumLotSize { get; private set; }
    public decimal? LotSizeMultiple { get; private set; }
    public decimal? SafetyStockQuantity { get; private set; }
    public decimal? ReorderPointQuantity { get; private set; }
    public int? PlannedDeliveryTimeDays { get; private set; }
    public int? InHouseProductionTimeDays { get; private set; }
    public int? GoodsReceiptProcessingTimeDays { get; private set; }
    public string AbcClass { get; private set; } = string.Empty;
    public string LifecycleStatus { get; private set; } = "active";
    public bool PurchasingEnabled { get; private set; } = true;
    public bool ManufacturingEnabled { get; private set; } = true;
    public bool SalesEnabled { get; private set; } = true;
    public bool Disabled { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public IReadOnlyCollection<string> ComplianceTags => complianceTags.AsReadOnly();

    public static Sku Create(string organizationId, string environmentId, string code, string name, string unit, string category)
    {
        return new Sku(organizationId, environmentId, code, name, unit, category, category, "not-tracked", "not-serialized", string.Empty, string.Empty, string.Empty, false, [], null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, "active", true, true, true);
    }

    public static Sku CreateIndustrial(
        string organizationId,
        string environmentId,
        string code,
        string name,
        string baseUomCode,
        string category,
        string materialType,
        string batchTrackingPolicy,
        string serialTrackingPolicy,
        string shelfLifePolicyCode,
        string storageConditionCode,
        string defaultBarcodeRuleCode,
        bool qualityRequired,
        IEnumerable<string> complianceTags,
        string? inventoryUomCode = null,
        string? purchaseUomCode = null,
        string? salesUomCode = null,
        string? manufacturingUomCode = null,
        string? procurementType = null,
        string? mrpType = null,
        string? lotSizingPolicy = null,
        decimal? minimumLotSize = null,
        decimal? maximumLotSize = null,
        decimal? lotSizeMultiple = null,
        decimal? safetyStockQuantity = null,
        decimal? reorderPointQuantity = null,
        int? plannedDeliveryTimeDays = null,
        int? inHouseProductionTimeDays = null,
        int? goodsReceiptProcessingTimeDays = null,
        string? abcClass = null,
        string? lifecycleStatus = "active",
        bool purchasingEnabled = true,
        bool manufacturingEnabled = true,
        bool salesEnabled = true)
    {
        return new Sku(
            organizationId,
            environmentId,
            code,
            name,
            baseUomCode,
            category,
            materialType,
            batchTrackingPolicy,
            serialTrackingPolicy,
            shelfLifePolicyCode,
            storageConditionCode,
            defaultBarcodeRuleCode,
            qualityRequired,
            complianceTags,
            inventoryUomCode,
            purchaseUomCode,
            salesUomCode,
            manufacturingUomCode,
            procurementType,
            mrpType,
            lotSizingPolicy,
            minimumLotSize,
            maximumLotSize,
            lotSizeMultiple,
            safetyStockQuantity,
            reorderPointQuantity,
            plannedDeliveryTimeDays,
            inHouseProductionTimeDays,
            goodsReceiptProcessingTimeDays,
            abcClass,
            lifecycleStatus,
            purchasingEnabled,
            manufacturingEnabled,
            salesEnabled);
    }

    public void Rename(string name)
    {
        var validName = Required(name);
        EnsureEnabled();
        Name = validName;
        Touch();
    }

    public void UpdateIndustrial(
        string name,
        string baseUomCode,
        string category,
        string materialType,
        string batchTrackingPolicy,
        string serialTrackingPolicy,
        string shelfLifePolicyCode,
        string storageConditionCode,
        string defaultBarcodeRuleCode,
        bool qualityRequired,
        string? inventoryUomCode = null,
        string? purchaseUomCode = null,
        string? salesUomCode = null,
        string? manufacturingUomCode = null,
        string? procurementType = null,
        string? mrpType = null,
        string? lotSizingPolicy = null,
        decimal? minimumLotSize = null,
        decimal? maximumLotSize = null,
        decimal? lotSizeMultiple = null,
        decimal? safetyStockQuantity = null,
        decimal? reorderPointQuantity = null,
        int? plannedDeliveryTimeDays = null,
        int? inHouseProductionTimeDays = null,
        int? goodsReceiptProcessingTimeDays = null,
        string? abcClass = null,
        string? lifecycleStatus = null,
        bool? purchasingEnabled = null,
        bool? manufacturingEnabled = null,
        bool? salesEnabled = null)
    {
        EnsureEnabled();
        Name = Required(name);
        Unit = Required(baseUomCode);
        BaseUomCode = Unit;
        InventoryUomCode = UomOrBase(inventoryUomCode, Unit);
        PurchaseUomCode = UomOrBase(purchaseUomCode, Unit);
        SalesUomCode = UomOrBase(salesUomCode, Unit);
        ManufacturingUomCode = UomOrBase(manufacturingUomCode, Unit);
        Category = Required(category);
        MaterialType = Required(materialType);
        BatchTrackingPolicy = Required(batchTrackingPolicy);
        SerialTrackingPolicy = Required(serialTrackingPolicy);
        ShelfLifePolicyCode = Optional(shelfLifePolicyCode);
        StorageConditionCode = Optional(storageConditionCode);
        DefaultBarcodeRuleCode = Optional(defaultBarcodeRuleCode);
        QualityRequired = qualityRequired;
        SetPlanningProfile(
            procurementType,
            mrpType,
            lotSizingPolicy,
            minimumLotSize,
            maximumLotSize,
            lotSizeMultiple,
            safetyStockQuantity,
            reorderPointQuantity,
            plannedDeliveryTimeDays,
            inHouseProductionTimeDays,
            goodsReceiptProcessingTimeDays,
            abcClass);
        if (lifecycleStatus is not null)
        {
            LifecycleStatus = NormalizeLifecycleStatus(lifecycleStatus);
        }

        PurchasingEnabled = purchasingEnabled ?? PurchasingEnabled;
        ManufacturingEnabled = manufacturingEnabled ?? ManufacturingEnabled;
        SalesEnabled = salesEnabled ?? SalesEnabled;
        Touch();
    }

    public void Disable(string reason)
    {
        var validReason = Required(reason);
        EnsureEnabled();
        Disabled = true;
        UpdatedAtUtc = DateTime.UtcNow;
        this.AddDomainEvent(new MasterDataAggregateDisabledDomainEvent(nameof(Sku), OrganizationId, EnvironmentId, Code, validReason));
        this.AddDomainEvent(new SkuDisabledDomainEvent(OrganizationId, EnvironmentId, Code, validReason));
    }

    public void Enable(string reason)
    {
        _ = Required(reason);
        if (!Disabled)
        {
            return;
        }

        Disabled = false;
        Touch();
    }

    private void Touch()
    {
        UpdatedAtUtc = DateTime.UtcNow;
        this.AddDomainEvent(new MasterDataAggregateUpdatedDomainEvent(nameof(Sku), OrganizationId, EnvironmentId, Code));
        this.AddDomainEvent(new SkuChangedDomainEvent(OrganizationId, EnvironmentId, Code));
    }

    private void EnsureEnabled()
    {
        if (Disabled)
        {
            throw new InvalidOperationException("Disabled SKU cannot be changed.");
        }
    }

    private static string Required(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value cannot be blank.", nameof(value)) : value.Trim();
    }

    private static string Optional(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

    private static string UomOrBase(string? value, string baseUomCode)
    {
        return string.IsNullOrWhiteSpace(value) ? baseUomCode : value.Trim();
    }

    private void SetPlanningProfile(
        string? procurementType,
        string? mrpType,
        string? lotSizingPolicy,
        decimal? minimumLotSize,
        decimal? maximumLotSize,
        decimal? lotSizeMultiple,
        decimal? safetyStockQuantity,
        decimal? reorderPointQuantity,
        int? plannedDeliveryTimeDays,
        int? inHouseProductionTimeDays,
        int? goodsReceiptProcessingTimeDays,
        string? abcClass)
    {
        ValidateNonNegative(minimumLotSize, nameof(minimumLotSize));
        ValidateNonNegative(maximumLotSize, nameof(maximumLotSize));
        ValidateNonNegative(lotSizeMultiple, nameof(lotSizeMultiple));
        ValidateNonNegative(safetyStockQuantity, nameof(safetyStockQuantity));
        ValidateNonNegative(reorderPointQuantity, nameof(reorderPointQuantity));
        ValidateNonNegative(plannedDeliveryTimeDays, nameof(plannedDeliveryTimeDays));
        ValidateNonNegative(inHouseProductionTimeDays, nameof(inHouseProductionTimeDays));
        ValidateNonNegative(goodsReceiptProcessingTimeDays, nameof(goodsReceiptProcessingTimeDays));
        if (minimumLotSize.HasValue && maximumLotSize.HasValue && maximumLotSize.Value < minimumLotSize.Value)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumLotSize), "Maximum lot size cannot be smaller than minimum lot size.");
        }

        ProcurementType = Optional(procurementType);
        MrpType = Optional(mrpType);
        LotSizingPolicy = Optional(lotSizingPolicy);
        MinimumLotSize = minimumLotSize;
        MaximumLotSize = maximumLotSize;
        LotSizeMultiple = lotSizeMultiple;
        SafetyStockQuantity = safetyStockQuantity;
        ReorderPointQuantity = reorderPointQuantity;
        PlannedDeliveryTimeDays = plannedDeliveryTimeDays;
        InHouseProductionTimeDays = inHouseProductionTimeDays;
        GoodsReceiptProcessingTimeDays = goodsReceiptProcessingTimeDays;
        AbcClass = Optional(abcClass);
    }

    private static string NormalizeLifecycleStatus(string? lifecycleStatus)
    {
        var value = string.IsNullOrWhiteSpace(lifecycleStatus) ? "active" : lifecycleStatus.Trim();
        return value.ToLowerInvariant() switch
        {
            "draft" or "active" or "blocked" or "obsolete" => value,
            _ => throw new ArgumentException("Unsupported SKU lifecycle status.", nameof(lifecycleStatus)),
        };
    }

    private static void ValidateNonNegative(decimal? value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Value cannot be negative.");
        }
    }

    private static void ValidateNonNegative(int? value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Value cannot be negative.");
        }
    }
}
