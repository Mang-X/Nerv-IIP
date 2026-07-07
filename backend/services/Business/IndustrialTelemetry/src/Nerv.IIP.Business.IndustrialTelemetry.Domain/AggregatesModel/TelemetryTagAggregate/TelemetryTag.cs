using Nerv.IIP.Business.IndustrialTelemetry.Domain.DomainEvents;
using System.Text.Json;

namespace Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetryTagAggregate;

public partial record TelemetryTagId : IGuidStronglyTypedId;

public sealed class TelemetryTag : Entity<TelemetryTagId>, IAggregateRoot
{
    private TelemetryTag()
    {
    }

    private TelemetryTag(
        string organizationId,
        string environmentId,
        string deviceAssetId,
        string tagKey,
        string valueType,
        string unitCode,
        string samplingPolicy)
    {
        Id = new TelemetryTagId(Guid.CreateVersion7());
        OrganizationId = IndustrialTelemetryText.Required(organizationId, nameof(organizationId));
        EnvironmentId = IndustrialTelemetryText.Required(environmentId, nameof(environmentId));
        DeviceAssetId = IndustrialTelemetryText.Required(deviceAssetId, nameof(deviceAssetId));
        TagKey = IndustrialTelemetryText.RequiredLower(tagKey, nameof(tagKey));
        ValueType = IndustrialTelemetryText.RequiredLower(valueType, nameof(valueType));
        UnitCode = IndustrialTelemetryText.Required(unitCode, nameof(unitCode));
        SamplingPolicy = IndustrialTelemetryText.RequiredLower(samplingPolicy, nameof(samplingPolicy));
        CreatedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
        this.AddDomainEvent(new TelemetryTagCreatedDomainEvent(this));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string DeviceAssetId { get; private set; } = string.Empty;
    public string TagKey { get; private set; } = string.Empty;
    public string ValueType { get; private set; } = string.Empty;
    public string UnitCode { get; private set; } = string.Empty;
    public string SamplingPolicy { get; private set; } = string.Empty;
    public bool IsWritable { get; private set; }
    public decimal? ControlMinValue { get; private set; }
    public decimal? ControlMaxValue { get; private set; }
    public string ControlAllowedValuesJson { get; private set; } = "[]";
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static TelemetryTag Create(
        string organizationId,
        string environmentId,
        string deviceAssetId,
        string tagKey,
        string valueType,
        string unitCode,
        string samplingPolicy)
    {
        return new TelemetryTag(organizationId, environmentId, deviceAssetId, tagKey, valueType, unitCode, samplingPolicy);
    }

    public void UpdateDefinition(string valueType, string unitCode, string samplingPolicy)
    {
        ValueType = IndustrialTelemetryText.RequiredLower(valueType, nameof(valueType));
        UnitCode = IndustrialTelemetryText.Required(unitCode, nameof(unitCode));
        SamplingPolicy = IndustrialTelemetryText.RequiredLower(samplingPolicy, nameof(samplingPolicy));
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void ConfigureControl(
        bool isWritable,
        decimal? minValue,
        decimal? maxValue,
        IReadOnlyCollection<string> allowedValues)
    {
        if (minValue.HasValue && maxValue.HasValue && minValue.Value > maxValue.Value)
        {
            throw new ArgumentOutOfRangeException(nameof(minValue), "Control min value cannot be greater than max value.");
        }

        IsWritable = isWritable;
        ControlMinValue = minValue;
        ControlMaxValue = maxValue;
        ControlAllowedValuesJson = JsonSerializer.Serialize(NormalizeAllowedValues(allowedValues));
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public IReadOnlyCollection<string> ControlAllowedValues =>
        JsonSerializer.Deserialize<IReadOnlyCollection<string>>(ControlAllowedValuesJson) ?? [];

    public bool HasSameBusinessKey(TelemetryTag other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return OrganizationId == other.OrganizationId
            && EnvironmentId == other.EnvironmentId
            && DeviceAssetId == other.DeviceAssetId
            && TagKey == other.TagKey;
    }

    private static IReadOnlyCollection<string> NormalizeAllowedValues(IReadOnlyCollection<string> values)
    {
        return values
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
