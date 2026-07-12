namespace Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.AlarmShelveIdempotencyAggregate;

public partial record AlarmShelveIdempotencyId : IGuidStronglyTypedId;

/// <summary>
/// Persistent per-(alarm, idempotencyKey) shelve idempotency record with a payload fingerprint —
/// mirrors <c>Nerv.IIP.Coding.CodeIdempotencyKey</c>. A unique index on
/// (organization, environment, alarmEventId, idempotencyKey) makes the shelve operation durably
/// idempotent independent of the alarm's window/status:
///  - same key + same fingerprint  → replay (the caller returns the prior result, no re-apply),
///    so a delayed duplicate re-applies nothing even after A→B→delayed-A or window expiry;
///  - same key + different fingerprint → the key was reused with a conflicting payload → reject;
///  - concurrent duplicates are collapsed by the unique index (single apply).
/// </summary>
public sealed class AlarmShelveIdempotency : Entity<AlarmShelveIdempotencyId>, IAggregateRoot
{
    private AlarmShelveIdempotency()
    {
    }

    public AlarmShelveIdempotency(
        string organizationId,
        string environmentId,
        string alarmEventId,
        string idempotencyKey,
        string payloadFingerprint)
    {
        Id = new AlarmShelveIdempotencyId(Guid.CreateVersion7());
        OrganizationId = IndustrialTelemetryText.Required(organizationId, nameof(organizationId));
        EnvironmentId = IndustrialTelemetryText.Required(environmentId, nameof(environmentId));
        AlarmEventId = IndustrialTelemetryText.Required(alarmEventId, nameof(alarmEventId));
        IdempotencyKey = IndustrialTelemetryText.Required(idempotencyKey, nameof(idempotencyKey));
        PayloadFingerprint = IndustrialTelemetryText.Required(payloadFingerprint, nameof(payloadFingerprint));
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string AlarmEventId { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string PayloadFingerprint { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
}
