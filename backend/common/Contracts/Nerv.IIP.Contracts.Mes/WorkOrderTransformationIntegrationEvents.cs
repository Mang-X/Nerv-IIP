using System.Collections.Immutable;
using Nerv.IIP.Contracts.IntegrationEvents;

namespace Nerv.IIP.Contracts.Mes;

public sealed record WorkOrderSplitIntegrationEvent(
    string EventId, string EventType, int EventVersion, DateTimeOffset OccurredAtUtc,
    string SourceService, string CorrelationId, string CausationId,
    string OrganizationId, string EnvironmentId, string Actor, string IdempotencyKey,
    WorkOrderTransformationPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record WorkOrderMergedIntegrationEvent(
    string EventId, string EventType, int EventVersion, DateTimeOffset OccurredAtUtc,
    string SourceService, string CorrelationId, string CausationId,
    string OrganizationId, string EnvironmentId, string Actor, string IdempotencyKey,
    WorkOrderTransformationPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record WorkOrderTransformationPayload(
    Guid TransformationId,
    string Reason,
    ImmutableArray<WorkOrderTransformationLinePayload> Lines);

public sealed record WorkOrderTransformationLinePayload(
    string SourceWorkOrderId,
    string TargetWorkOrderId,
    decimal Quantity,
    decimal SourceQuantity,
    decimal TargetQuantity,
    string UomCode,
    string SourceStatus,
    string TargetStatus,
    long SourceVersion,
    long TargetVersion);
