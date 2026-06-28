using Nerv.IIP.Contracts.IntegrationEvents;

namespace Nerv.IIP.Contracts.Mes;

public static class MesIntegrationEventTypes
{
    public const string WorkOrderReleased = "mes.WorkOrderReleased";
}

public static class MesIntegrationEventVersions
{
    public const int V1 = 1;
}

public static class MesIntegrationEventSources
{
    public const string BusinessMes = "business-mes";
}

public sealed record WorkOrderReleasedIntegrationEvent(
    string EventId,
    string EventType,
    int EventVersion,
    DateTimeOffset OccurredAtUtc,
    string SourceService,
    string CorrelationId,
    string CausationId,
    string OrganizationId,
    string EnvironmentId,
    string Actor,
    string IdempotencyKey,
    WorkOrderReleasedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record WorkOrderReleasedPayload(
    string WorkOrderId,
    string SkuCode,
    decimal PlannedQuantity,
    DateTimeOffset ReleasedAtUtc,
    IReadOnlyCollection<ReleasedOperationPayload> Operations);

public sealed record ReleasedOperationPayload(
    string OperationId,
    int OperationSequence,
    string WorkCenterId);
