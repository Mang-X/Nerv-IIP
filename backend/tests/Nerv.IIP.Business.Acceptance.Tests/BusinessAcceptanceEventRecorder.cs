using System.Collections.Concurrent;

namespace Nerv.IIP.Business.Acceptance.Tests;

public sealed class BusinessAcceptanceFixtureEventRecorder
{
    private readonly ConcurrentQueue<BusinessAcceptanceRecordedEvent> _events = new();

    public void Record(string service, string eventType, BusinessAcceptanceCorrelation correlation, object payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(service);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentNullException.ThrowIfNull(correlation);
        ArgumentNullException.ThrowIfNull(payload);

        _events.Enqueue(new BusinessAcceptanceRecordedEvent(
            service,
            eventType,
            correlation.CorrelationId,
            correlation.OrganizationId,
            correlation.EnvironmentId,
            payload));
    }

    public IReadOnlyCollection<BusinessAcceptanceRecordedEvent> ForCorrelation(string correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        return _events
            .Where(x => string.Equals(x.CorrelationId, correlationId, StringComparison.Ordinal))
            .ToArray();
    }

    public void Reset()
    {
        _events.Clear();
    }
}

public sealed record BusinessAcceptanceRecordedEvent(
    string Service,
    string EventType,
    string CorrelationId,
    string OrganizationId,
    string EnvironmentId,
    object Payload);
