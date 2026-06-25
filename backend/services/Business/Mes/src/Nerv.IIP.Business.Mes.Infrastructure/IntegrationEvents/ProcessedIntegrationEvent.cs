using NetCorePal.Extensions.Domain;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Mes.Infrastructure.IntegrationEvents;

public partial record ProcessedIntegrationEventId : IGuidStronglyTypedId;

public sealed class ProcessedIntegrationEvent : Entity<ProcessedIntegrationEventId>
{
    private ProcessedIntegrationEvent()
    {
    }

    public ProcessedIntegrationEvent(
        string consumerName,
        string eventId,
        string eventType,
        int eventVersion,
        string sourceService,
        string idempotencyKey,
        DateTimeOffset processedAtUtc)
    {
        ConsumerName = consumerName;
        EventId = eventId;
        EventType = eventType;
        EventVersion = eventVersion;
        SourceService = sourceService;
        IdempotencyKey = idempotencyKey;
        ProcessedAtUtc = processedAtUtc;
    }

    public string ConsumerName { get; private set; } = string.Empty;
    public string EventId { get; private set; } = string.Empty;
    public string EventType { get; private set; } = string.Empty;
    public int EventVersion { get; private set; }
    public string SourceService { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public DateTimeOffset ProcessedAtUtc { get; private set; }
}
