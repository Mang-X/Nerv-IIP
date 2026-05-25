namespace Nerv.IIP.Contracts.IntegrationEvents;

// Narrow shared contract for consumer reliability guard; concrete events keep their typed Payload property.
public interface IIntegrationEventEnvelope
{
    string EventId { get; }
    string EventType { get; }
    int EventVersion { get; }
    DateTimeOffset OccurredAtUtc { get; }
    string SourceService { get; }
    string CorrelationId { get; }
    string CausationId { get; }
    string OrganizationId { get; }
    string EnvironmentId { get; }
    string Actor { get; }
    string IdempotencyKey { get; }
    object? PayloadObject { get; }
}
