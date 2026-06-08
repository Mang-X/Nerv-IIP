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
        IEnumerable<string> complianceTags)
    {
        OrganizationId = Required(organizationId);
        EnvironmentId = Required(environmentId);
        Code = Required(code);
        Name = Required(name);
        Unit = Required(unit);
        BaseUomCode = Unit;
        InventoryUomCode = Unit;
        PurchaseUomCode = Unit;
        SalesUomCode = Unit;
        ManufacturingUomCode = Unit;
        Category = Required(category);
        MaterialType = Required(materialType);
        BatchTrackingPolicy = Required(batchTrackingPolicy);
        SerialTrackingPolicy = Required(serialTrackingPolicy);
        ShelfLifePolicyCode = Optional(shelfLifePolicyCode);
        StorageConditionCode = Optional(storageConditionCode);
        DefaultBarcodeRuleCode = Optional(defaultBarcodeRuleCode);
        QualityRequired = qualityRequired;
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
    public bool Disabled { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public IReadOnlyCollection<string> ComplianceTags => complianceTags.AsReadOnly();

    public static Sku Create(string organizationId, string environmentId, string code, string name, string unit, string category)
    {
        return new Sku(organizationId, environmentId, code, name, unit, category, category, "not-tracked", "not-serialized", string.Empty, string.Empty, string.Empty, false, []);
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
        IEnumerable<string> complianceTags)
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
            complianceTags);
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
        bool qualityRequired)
    {
        EnsureEnabled();
        Name = Required(name);
        Unit = Required(baseUomCode);
        BaseUomCode = Unit;
        InventoryUomCode = Unit;
        PurchaseUomCode = Unit;
        SalesUomCode = Unit;
        ManufacturingUomCode = Unit;
        Category = Required(category);
        MaterialType = Required(materialType);
        BatchTrackingPolicy = Required(batchTrackingPolicy);
        SerialTrackingPolicy = Required(serialTrackingPolicy);
        ShelfLifePolicyCode = Optional(shelfLifePolicyCode);
        StorageConditionCode = Optional(storageConditionCode);
        DefaultBarcodeRuleCode = Optional(defaultBarcodeRuleCode);
        QualityRequired = qualityRequired;
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

    private static string Optional(string value)
    {
        return value?.Trim() ?? string.Empty;
    }
}
