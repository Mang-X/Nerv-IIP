namespace Nerv.IIP.Business.Inventory.Infrastructure;

/// <summary>
/// Immutable event-bound audit fact for a movement that is waiting on unit-cost authority.
/// The CAP delivery remains unacknowledged after this fact is saved.
/// </summary>
public sealed class InventoryAuthorityResolutionPendingAudit
{
    public const string PendingStatus = "Pending";

    private InventoryAuthorityResolutionPendingAudit()
    {
    }

    public InventoryAuthorityResolutionPendingAudit(
        string eventId,
        string idempotencyKey,
        string reasonCode,
        DateTimeOffset observedAtUtc)
    {
        EventId = Required(eventId, nameof(eventId));
        IdempotencyKey = Required(idempotencyKey, nameof(idempotencyKey));
        ReasonCode = Required(reasonCode, nameof(reasonCode));
        if (observedAtUtc == default)
        {
            throw new ArgumentOutOfRangeException(nameof(observedAtUtc));
        }

        Id = Guid.CreateVersion7();
        Status = PendingStatus;
        ObservedAtUtc = observedAtUtc.ToUniversalTime();
    }

    public Guid Id { get; private set; }

    public string EventId { get; private set; } = null!;

    public string IdempotencyKey { get; private set; } = null!;

    public string ReasonCode { get; private set; } = null!;

    public string Status { get; private set; } = null!;

    public DateTimeOffset ObservedAtUtc { get; private set; }

    public void EnsureMatches(
        string idempotencyKey,
        string reasonCode,
        string status)
    {
        if (string.Equals(IdempotencyKey, idempotencyKey, StringComparison.Ordinal)
            && string.Equals(ReasonCode, reasonCode, StringComparison.Ordinal)
            && string.Equals(Status, status, StringComparison.Ordinal))
        {
            return;
        }

        throw new InventoryAuthorityResolutionPendingAuditConflictException(
            EventId,
            IdempotencyKey,
            ReasonCode,
            Status,
            idempotencyKey,
            reasonCode,
            status);
    }

    private static string Required(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value;
    }
}

/// <summary>
/// Indicates that an event id is already bound to different pending identity data.
/// This exception must escape the consumer so the delivery remains unacknowledged.
/// </summary>
public sealed class InventoryAuthorityResolutionPendingAuditConflictException(
    string eventId,
    string storedIdempotencyKey,
    string storedReasonCode,
    string storedStatus,
    string receivedIdempotencyKey,
    string receivedReasonCode,
    string receivedStatus)
    : InvalidOperationException(
        $"Inventory authority pending audit conflict for event '{eventId}'. "
        + $"Stored identity is ({storedIdempotencyKey}, {storedReasonCode}, {storedStatus}); "
        + $"received ({receivedIdempotencyKey}, {receivedReasonCode}, {receivedStatus}).")
{
    public string EventId { get; } = eventId;

    public string StoredIdempotencyKey { get; } = storedIdempotencyKey;

    public string StoredReasonCode { get; } = storedReasonCode;

    public string StoredStatus { get; } = storedStatus;

    public string ReceivedIdempotencyKey { get; } = receivedIdempotencyKey;

    public string ReceivedReasonCode { get; } = receivedReasonCode;

    public string ReceivedStatus { get; } = receivedStatus;
}
