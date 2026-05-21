using Nerv.IIP.Business.MasterData.Domain.DomainEvents;

namespace Nerv.IIP.Business.MasterData.Domain.AggregatesModel.DeviceAssetAggregate;

public partial record DeviceAssetId : IGuidStronglyTypedId;

public class DeviceAsset : Entity<DeviceAssetId>, IAggregateRoot
{
    private readonly Dictionary<string, string> externalReferences = new(StringComparer.OrdinalIgnoreCase);

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
    public string AssetClassCode { get; private set; } = string.Empty;
    public string Manufacturer { get; private set; } = string.Empty;
    public string SerialNo { get; private set; } = string.Empty;
    public decimal? MinimumCapacity { get; private set; }
    public decimal? MaximumCapacity { get; private set; }
    public string CapacityUomCode { get; private set; } = string.Empty;
    public string Criticality { get; private set; } = string.Empty;
    public bool Maintainable { get; private set; }
    public bool TelemetryEnabled { get; private set; }
    public IReadOnlyDictionary<string, string> ExternalReferences => externalReferences.AsReadOnly();
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

    public void Disable(string reason)
    {
        var validReason = Required(reason);
        EnsureEnabled();
        Disabled = true;
        UpdatedAtUtc = DateTime.UtcNow;
        this.AddDomainEvent(new MasterDataAggregateDisabledDomainEvent(nameof(DeviceAsset), OrganizationId, EnvironmentId, Code, validReason));
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

    private static string Optional(string value)
    {
        return value?.Trim() ?? string.Empty;
    }
}
