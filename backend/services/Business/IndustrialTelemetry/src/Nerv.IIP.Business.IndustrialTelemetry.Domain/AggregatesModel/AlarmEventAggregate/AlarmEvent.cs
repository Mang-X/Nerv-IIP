using Nerv.IIP.Business.IndustrialTelemetry.Domain.DomainEvents;

namespace Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.AlarmEventAggregate;

public partial record AlarmEventId : IGuidStronglyTypedId;

public sealed class AlarmEvent : Entity<AlarmEventId>, IAggregateRoot
{
    private AlarmEvent()
    {
    }

    private AlarmEvent(
        string organizationId,
        string environmentId,
        string deviceAssetId,
        string alarmCode,
        string severity,
        DateTimeOffset raisedAtUtc,
        string externalAlarmId,
        string? priority,
        string? tagKey,
        decimal? observedValue,
        decimal? thresholdValue,
        string? unitCode)
    {
        Id = new AlarmEventId(Guid.CreateVersion7());
        OrganizationId = IndustrialTelemetryText.Required(organizationId, nameof(organizationId));
        EnvironmentId = IndustrialTelemetryText.Required(environmentId, nameof(environmentId));
        DeviceAssetId = IndustrialTelemetryText.Required(deviceAssetId, nameof(deviceAssetId));
        AlarmCode = IndustrialTelemetryText.Required(alarmCode, nameof(alarmCode));
        Severity = IndustrialTelemetryText.RequiredLower(severity, nameof(severity));
        Priority = IndustrialTelemetryText.Optional(priority) ?? Severity;
        RaisedAtUtc = raisedAtUtc;
        ExternalAlarmId = IndustrialTelemetryText.Required(externalAlarmId, nameof(externalAlarmId));
        TagKey = IndustrialTelemetryText.Optional(tagKey);
        ObservedValue = observedValue;
        ThresholdValue = thresholdValue;
        UnitCode = IndustrialTelemetryText.Optional(unitCode);
        Status = "raised";
        RecordedAtUtc = DateTimeOffset.UtcNow;
        this.AddDomainEvent(new AlarmRaisedDomainEvent(this));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string DeviceAssetId { get; private set; } = string.Empty;
    public string AlarmCode { get; private set; } = string.Empty;
    public string Severity { get; private set; } = string.Empty;
    public string Priority { get; private set; } = string.Empty;
    public string? TagKey { get; private set; }
    public decimal? ObservedValue { get; private set; }
    public decimal? ThresholdValue { get; private set; }
    public string? UnitCode { get; private set; }
    public DateTimeOffset RaisedAtUtc { get; private set; }
    public string ExternalAlarmId { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; private set; }
    public DateTimeOffset? ClearedAtUtc { get; private set; }
    public string? ClearedBy { get; private set; }
    public string? ClearReason { get; private set; }

    public static AlarmEvent Raise(
        string organizationId,
        string environmentId,
        string deviceAssetId,
        string alarmCode,
        string severity,
        DateTimeOffset raisedAtUtc,
        string externalAlarmId,
        string? priority = null,
        string? tagKey = null,
        decimal? observedValue = null,
        decimal? thresholdValue = null,
        string? unitCode = null)
    {
        return new AlarmEvent(organizationId, environmentId, deviceAssetId, alarmCode, severity, raisedAtUtc, externalAlarmId, priority, tagKey, observedValue, thresholdValue, unitCode);
    }

    public void Clear(DateTimeOffset clearedAtUtc, string clearedBy, string? clearReason = null)
    {
        if (clearedAtUtc < RaisedAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(clearedAtUtc), "Alarm cannot be cleared before it was raised.");
        }

        var normalizedClearedBy = IndustrialTelemetryText.Required(clearedBy, nameof(clearedBy));
        var normalizedReason = IndustrialTelemetryText.Optional(clearReason);
        if (Status == "cleared")
        {
            if (ClearedAtUtc == clearedAtUtc && ClearedBy == normalizedClearedBy && ClearReason == normalizedReason)
            {
                return;
            }

            throw new InvalidOperationException("Alarm was already cleared with different payload.");
        }

        Status = "cleared";
        ClearedAtUtc = clearedAtUtc;
        ClearedBy = normalizedClearedBy;
        ClearReason = normalizedReason;
        this.AddDomainEvent(new AlarmClearedDomainEvent(this));
    }

    public bool IsSameExternalAlarm(AlarmEvent other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return OrganizationId == other.OrganizationId
            && EnvironmentId == other.EnvironmentId
            && DeviceAssetId == other.DeviceAssetId
            && AlarmCode == other.AlarmCode
            && ExternalAlarmId == other.ExternalAlarmId;
    }

    public bool HasSameRaisePayload(AlarmEvent other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return IsSameExternalAlarm(other)
            && DeviceAssetId == other.DeviceAssetId
            && AlarmCode == other.AlarmCode
            && Severity == other.Severity
            && Priority == other.Priority
            && TagKey == other.TagKey
            && ObservedValue == other.ObservedValue
            && ThresholdValue == other.ThresholdValue
            && UnitCode == other.UnitCode
            && RaisedAtUtc == other.RaisedAtUtc;
    }

    public void EnsureCompatibleDuplicate(AlarmEvent other)
    {
        if (!HasSameRaisePayload(other))
        {
            throw new InvalidOperationException("Alarm external ID duplicate has conflicting payload.");
        }
    }
}
