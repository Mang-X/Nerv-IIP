using Nerv.IIP.Business.MasterData.Domain.DomainEvents;

namespace Nerv.IIP.Business.MasterData.Domain.AggregatesModel.DeviceAssetAggregate;

public partial record DeviceAssetId : IGuidStronglyTypedId;

public partial record DeviceAssetComponentId : IGuidStronglyTypedId;

public sealed record DeviceAssetComponentDraft(string ComponentCode, string ComponentName, decimal Quantity, bool Critical);

public class DeviceAsset : Entity<DeviceAssetId>, IAggregateRoot
{
    private readonly Dictionary<string, string> externalReferences = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<DeviceAssetComponent> components = [];

    protected DeviceAsset()
    {
    }

    private DeviceAsset(
        string organizationId,
        string environmentId,
        string code,
        string model,
        string lineCode,
        string workCenterCode,
        string assetClassCode,
        string manufacturer,
        string serialNo,
        decimal? minimumCapacity,
        decimal? maximumCapacity,
        string capacityUomCode,
        string criticality,
        bool maintainable,
        bool telemetryEnabled,
        IReadOnlyDictionary<string, string> externalReferences)
    {
        if (minimumCapacity.HasValue && maximumCapacity.HasValue && maximumCapacity.Value < minimumCapacity.Value)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCapacity), "Maximum capacity cannot be less than minimum capacity.");
        }

        OrganizationId = Required(organizationId);
        EnvironmentId = Required(environmentId);
        Code = Required(code);
        Model = Required(model);
        LineCode = Required(lineCode);
        WorkCenterCode = Required(workCenterCode);
        AssetClassCode = Required(assetClassCode);
        Manufacturer = Optional(manufacturer);
        SerialNo = Optional(serialNo);
        MinimumCapacity = minimumCapacity;
        MaximumCapacity = maximumCapacity;
        CapacityUomCode = Optional(capacityUomCode);
        Criticality = Required(criticality);
        Maintainable = maintainable;
        TelemetryEnabled = telemetryEnabled;
        foreach (var reference in externalReferences)
        {
            this.externalReferences.Add(Required(reference.Key), Required(reference.Value));
        }

        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
        this.AddDomainEvent(new MasterDataAggregateCreatedDomainEvent(nameof(DeviceAsset), OrganizationId, EnvironmentId, Code));
        this.AddDomainEvent(new DeviceAssetChangedDomainEvent(OrganizationId, EnvironmentId, Code));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string Code { get; private set; } = string.Empty;
    public string Model { get; private set; } = string.Empty;
    public string LineCode { get; private set; } = string.Empty;
    public string WorkCenterCode { get; private set; } = string.Empty;
    public string SiteCode { get; private set; } = string.Empty;
    public string WorkshopCode { get; private set; } = string.Empty;
    public string StationCode { get; private set; } = string.Empty;
    public string AssetClassCode { get; private set; } = string.Empty;
    public string Manufacturer { get; private set; } = string.Empty;
    public string SerialNo { get; private set; } = string.Empty;
    public DateOnly? PurchaseDate { get; private set; }
    public decimal? PurchaseCost { get; private set; }
    public string PurchaseCurrencyCode { get; private set; } = string.Empty;
    public DateOnly? WarrantyExpiresOn { get; private set; }
    public string SupplierPartnerCode { get; private set; } = string.Empty;
    public string ParentDeviceId { get; private set; } = string.Empty;
    public DateOnly? RetiredOn { get; private set; }
    public bool Retired => RetiredOn.HasValue;
    public decimal? MinimumCapacity { get; private set; }
    public decimal? MaximumCapacity { get; private set; }
    public string CapacityUomCode { get; private set; } = string.Empty;
    public string Criticality { get; private set; } = string.Empty;
    public bool Maintainable { get; private set; }
    public bool TelemetryEnabled { get; private set; }
    public IReadOnlyDictionary<string, string> ExternalReferences => externalReferences.AsReadOnly();
    public IReadOnlyCollection<DeviceAssetComponent> Components => components.AsReadOnly();
    public bool Disabled { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static DeviceAsset Register(
        string organizationId,
        string environmentId,
        string code,
        string model,
        string lineCode,
        string workCenterCode)
    {
        return new DeviceAsset(organizationId, environmentId, code, model, lineCode, workCenterCode, "equipment", string.Empty, string.Empty, null, null, string.Empty, "normal", true, false, new Dictionary<string, string>());
    }

    public static DeviceAsset RegisterCapability(
        string organizationId,
        string environmentId,
        string code,
        string model,
        string lineCode,
        string workCenterCode,
        string assetClassCode,
        string manufacturer,
        string serialNo,
        decimal? minimumCapacity,
        decimal? maximumCapacity,
        string capacityUomCode,
        string criticality,
        bool maintainable,
        bool telemetryEnabled,
        IReadOnlyDictionary<string, string> externalReferences)
    {
        return new DeviceAsset(
            organizationId,
            environmentId,
            code,
            model,
            lineCode,
            workCenterCode,
            assetClassCode,
            manufacturer,
            serialNo,
            minimumCapacity,
            maximumCapacity,
            capacityUomCode,
            criticality,
            maintainable,
            telemetryEnabled,
            externalReferences);
    }

    public void UpdateCapability(
        string model,
        string lineCode,
        string workCenterCode,
        string assetClassCode,
        string manufacturer,
        string serialNo,
        decimal? minimumCapacity,
        decimal? maximumCapacity,
        string capacityUomCode,
        string criticality,
        bool maintainable,
        bool telemetryEnabled)
    {
        if (minimumCapacity.HasValue && maximumCapacity.HasValue && maximumCapacity.Value < minimumCapacity.Value)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCapacity), "Maximum capacity cannot be less than minimum capacity.");
        }

        EnsureEnabled();
        Model = Required(model);
        LineCode = Required(lineCode);
        WorkCenterCode = Required(workCenterCode);
        AssetClassCode = Required(assetClassCode);
        Manufacturer = Optional(manufacturer);
        SerialNo = Optional(serialNo);
        MinimumCapacity = minimumCapacity;
        MaximumCapacity = maximumCapacity;
        CapacityUomCode = Optional(capacityUomCode);
        Criticality = Required(criticality);
        Maintainable = maintainable;
        TelemetryEnabled = telemetryEnabled;
        Touch();
    }

    public DeviceAsset WithLedger(
        DateOnly? purchaseDate,
        decimal? purchaseCost,
        string purchaseCurrencyCode,
        DateOnly? warrantyExpiresOn,
        string supplierPartnerCode,
        string siteCode,
        string workshopCode,
        string lineCode,
        string stationCode,
        string? parentDeviceId,
        DateOnly? retiredOn)
    {
        UpdateLedger(
            purchaseDate,
            purchaseCost,
            purchaseCurrencyCode,
            warrantyExpiresOn,
            supplierPartnerCode,
            siteCode,
            workshopCode,
            lineCode,
            stationCode,
            parentDeviceId,
            retiredOn);
        return this;
    }

    public void UpdateLedger(
        DateOnly? purchaseDate,
        decimal? purchaseCost,
        string purchaseCurrencyCode,
        DateOnly? warrantyExpiresOn,
        string supplierPartnerCode,
        string siteCode,
        string workshopCode,
        string lineCode,
        string stationCode,
        string? parentDeviceId,
        DateOnly? retiredOn)
    {
        EnsureEnabled();
        PurchaseCost = NonNegative(purchaseCost, nameof(purchaseCost));
        PurchaseDate = purchaseDate;
        PurchaseCurrencyCode = Optional(purchaseCurrencyCode);
        WarrantyExpiresOn = warrantyExpiresOn;
        SupplierPartnerCode = Optional(supplierPartnerCode);
        SiteCode = Optional(siteCode);
        WorkshopCode = Optional(workshopCode);
        LineCode = Required(lineCode);
        StationCode = Optional(stationCode);
        ParentDeviceId = Optional(parentDeviceId);
        RetiredOn = retiredOn;
        Touch();
    }

    public DeviceAsset ReplaceComponents(IEnumerable<DeviceAssetComponentDraft> drafts)
    {
        EnsureEnabled();
        var normalized = drafts
            .Select(DeviceAssetComponent.Create)
            .ToArray();
        var duplicate = normalized
            .GroupBy(x => x.ComponentCode, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException($"Device asset contains duplicate component '{duplicate.Key}'.", nameof(drafts));
        }

        components.Clear();
        components.AddRange(normalized);
        Touch();
        return this;
    }

    public void Disable(string reason)
    {
        var validReason = Required(reason);
        EnsureEnabled();
        Disabled = true;
        UpdatedAtUtc = DateTime.UtcNow;
        this.AddDomainEvent(new MasterDataAggregateDisabledDomainEvent(nameof(DeviceAsset), OrganizationId, EnvironmentId, Code, validReason));
        this.AddDomainEvent(new DeviceAssetChangedDomainEvent(OrganizationId, EnvironmentId, Code));
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
        this.AddDomainEvent(new MasterDataAggregateUpdatedDomainEvent(nameof(DeviceAsset), OrganizationId, EnvironmentId, Code));
        this.AddDomainEvent(new DeviceAssetChangedDomainEvent(OrganizationId, EnvironmentId, Code));
    }

    private void EnsureEnabled()
    {
        if (Disabled)
        {
            throw new InvalidOperationException("Disabled device asset cannot be changed.");
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

    private static decimal? NonNegative(decimal? value, string parameterName)
    {
        if (value is < 0m)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, $"{parameterName} cannot be negative.");
        }

        return value;
    }
}

public sealed class DeviceAssetComponent : Entity<DeviceAssetComponentId>
{
    private DeviceAssetComponent()
    {
    }

    private DeviceAssetComponent(string componentCode, string componentName, decimal quantity, bool critical)
    {
        Id = new DeviceAssetComponentId(Guid.CreateVersion7());
        ComponentCode = Required(componentCode);
        ComponentName = Optional(componentName);
        Quantity = Positive(quantity, nameof(quantity));
        Critical = critical;
    }

    public string ComponentCode { get; private set; } = string.Empty;
    public string ComponentName { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public bool Critical { get; private set; }

    public static DeviceAssetComponent Create(DeviceAssetComponentDraft draft) =>
        new(draft.ComponentCode, draft.ComponentName, draft.Quantity, draft.Critical);

    private static string Required(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value cannot be blank.", nameof(value)) : value.Trim();
    }

    private static string Optional(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

    private static decimal Positive(decimal value, string parameterName)
    {
        return value <= 0m ? throw new ArgumentOutOfRangeException(parameterName, value, $"{parameterName} must be positive.") : value;
    }
}
