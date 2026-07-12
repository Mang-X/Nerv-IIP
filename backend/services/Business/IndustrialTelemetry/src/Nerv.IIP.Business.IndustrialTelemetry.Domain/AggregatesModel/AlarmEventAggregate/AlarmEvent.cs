using Nerv.IIP.Business.IndustrialTelemetry.Domain.DomainEvents;

namespace Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.AlarmEventAggregate;

public partial record AlarmEventId : IGuidStronglyTypedId, IComparable<AlarmEventId>
{
    // Orderable by the underlying Guid so the id is a truly-unique pagination tie-breaker: PostgreSQL
    // orders the mapped uuid column; the InMemory test provider (which cannot sort a non-IComparable
    // value object) uses this comparison.
    public int CompareTo(AlarmEventId? other) => Id.CompareTo(other?.Id ?? Guid.Empty);
}

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
    public DateTimeOffset? AcknowledgedAtUtc { get; private set; }
    public string? AcknowledgedBy { get; private set; }
    public DateTimeOffset? ShelvedAtUtc { get; private set; }
    public DateTimeOffset? ShelvedUntilUtc { get; private set; }
    public string? ShelvedBy { get; private set; }
    public string? ShelveReason { get; private set; }
    public DateTimeOffset? EscalatedAtUtc { get; private set; }
    public string? EscalationReason { get; private set; }
    public string? EscalationRecipientRefsText { get; private set; }
    public IReadOnlyCollection<string> EscalationRecipientRefs =>
        SplitRecipientRefs(EscalationRecipientRefsText);

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

    public void Acknowledge(DateTimeOffset acknowledgedAtUtc, string acknowledgedBy)
    {
        EnsureActive("cleared alarms cannot be acknowledged.");
        if (acknowledgedAtUtc < RaisedAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(acknowledgedAtUtc), "Alarm cannot be acknowledged before it was raised.");
        }

        var normalizedAcknowledgedBy = IndustrialTelemetryText.Required(acknowledgedBy, nameof(acknowledgedBy));
        if (AcknowledgedAtUtc is not null)
        {
            return;
        }

        AcknowledgedAtUtc = acknowledgedAtUtc;
        AcknowledgedBy = normalizedAcknowledgedBy;
        if (Status != "shelved")
        {
            Status = "acknowledged";
        }

        this.AddDomainEvent(new AlarmAcknowledgedDomainEvent(this));
    }

    public void Shelve(DateTimeOffset shelvedAtUtc, DateTimeOffset shelvedUntilUtc, string shelvedBy, string? shelveReason = null)
    {
        EnsureActive("cleared alarms cannot be shelved.");
        if (shelvedAtUtc < RaisedAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(shelvedAtUtc), "Alarm cannot be shelved before it was raised.");
        }

        if (shelvedUntilUtc <= shelvedAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(shelvedUntilUtc), "Alarm shelving must have a future expiry.");
        }

        if (IsShelvedAt(shelvedAtUtc))
        {
            return;
        }

        ShelvedAtUtc = shelvedAtUtc;
        ShelvedUntilUtc = shelvedUntilUtc;
        ShelvedBy = IndustrialTelemetryText.Required(shelvedBy, nameof(shelvedBy));
        ShelveReason = IndustrialTelemetryText.Optional(shelveReason);
        Status = "shelved";
        this.AddDomainEvent(new AlarmShelvedDomainEvent(this));
    }

    public bool ExpireShelving(DateTimeOffset asOfUtc)
    {
        if (Status != "shelved" || ShelvedUntilUtc is null || ShelvedUntilUtc > asOfUtc)
        {
            return false;
        }

        return Unshelve(asOfUtc);
    }

    public bool Unshelve(DateTimeOffset unshelvedAtUtc)
    {
        if (Status != "shelved")
        {
            return false;
        }

        if (ShelvedAtUtc is not null && unshelvedAtUtc < ShelvedAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(unshelvedAtUtc), "Alarm cannot be unshelved before it was shelved.");
        }

        Status = AcknowledgedAtUtc is null ? "raised" : "acknowledged";
        this.AddDomainEvent(new AlarmUnshelvedDomainEvent(this));
        return true;
    }

    public bool IsShelvedAt(DateTimeOffset asOfUtc)
    {
        return Status == "shelved"
            && ShelvedAtUtc is not null
            && ShelvedUntilUtc is not null
            && ShelvedAtUtc <= asOfUtc
            && asOfUtc < ShelvedUntilUtc;
    }

    public bool ShouldEscalateAt(DateTimeOffset asOfUtc, TimeSpan unacknowledgedTimeout, IReadOnlyCollection<string> severityEscalationLevels)
    {
        if (Status == "cleared" || EscalatedAtUtc is not null || IsShelvedAt(asOfUtc))
        {
            return false;
        }

        var severityMatches = severityEscalationLevels.Any(level =>
            string.Equals(level.Trim(), Severity, StringComparison.OrdinalIgnoreCase)
            || string.Equals(level.Trim(), Priority, StringComparison.OrdinalIgnoreCase));
        if (severityMatches)
        {
            return true;
        }

        return AcknowledgedAtUtc is null
            && unacknowledgedTimeout > TimeSpan.Zero
            && asOfUtc >= RaisedAtUtc.Add(unacknowledgedTimeout);
    }

    public void Escalate(DateTimeOffset escalatedAtUtc, string escalationReason, IReadOnlyCollection<string> recipientRefs)
    {
        EnsureActive("cleared alarms cannot be escalated.");
        if (IsShelvedAt(escalatedAtUtc))
        {
            throw new InvalidOperationException("shelved alarms cannot be escalated.");
        }

        ExpireShelving(escalatedAtUtc);
        if (EscalatedAtUtc is not null)
        {
            return;
        }

        EscalatedAtUtc = escalatedAtUtc;
        EscalationReason = IndustrialTelemetryText.Required(escalationReason, nameof(escalationReason));
        EscalationRecipientRefsText = JoinRecipientRefs(recipientRefs);
        this.AddDomainEvent(new AlarmEscalatedDomainEvent(this));
    }

    private void EnsureActive(string message)
    {
        if (Status == "cleared")
        {
            throw new InvalidOperationException(message);
        }
    }

    private static string JoinRecipientRefs(IReadOnlyCollection<string> recipientRefs)
    {
        var normalized = recipientRefs
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (normalized.Length == 0)
        {
            throw new ArgumentException("At least one escalation recipient is required.", nameof(recipientRefs));
        }

        return string.Join(";", normalized);
    }

    private static IReadOnlyCollection<string> SplitRecipientRefs(string? value)
    {
        return value?
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? [];
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
