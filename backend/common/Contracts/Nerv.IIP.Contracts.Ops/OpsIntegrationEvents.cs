using Nerv.IIP.Contracts.IntegrationEvents;

namespace Nerv.IIP.Contracts.Ops;

public sealed record OperationTaskCompletedIntegrationEvent(
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
    OperationTaskCompletedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record OperationTaskCompletedPayload(
    string OperationTaskId,
    string AttemptId,
    string InstanceKey,
    string OperationCode,
    DateTimeOffset FinishedAtUtc);

public sealed record OperationTaskFailedIntegrationEvent(
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
    OperationTaskFailedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record OperationTaskFailedPayload(
    string OperationTaskId,
    string AttemptId,
    string InstanceKey,
    string OperationCode,
    DateTimeOffset FinishedAtUtc,
    string? FailureCode);

public sealed record OperationTaskRequestedIntegrationEvent(
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
    OperationTaskRequestedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record OperationTaskRequestedPayload(
    string OperationTaskId,
    string InstanceKey,
    string OperationCode,
    string RequestedBy,
    DateTimeOffset RequestedAtUtc);

public sealed record OperationTaskClaimedIntegrationEvent(
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
    OperationTaskClaimedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record OperationTaskClaimedPayload(
    string OperationTaskId,
    string AttemptId,
    string InstanceKey,
    string OperationCode,
    string LeaseId,
    DateTimeOffset LeasedAtUtc,
    DateTimeOffset LeasedUntilUtc,
    int AttemptNo,
    int MaxAttempts);

public sealed record AuditRecordedIntegrationEvent(
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
    AuditRecordedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record AuditRecordedPayload(
    string AuditRecordId,
    string OperationTaskId,
    string Action,
    string Actor,
    DateTimeOffset OccurredAtUtc);
