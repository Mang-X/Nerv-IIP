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
    OperationTaskCompletedPayload Payload);

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
    OperationTaskFailedPayload Payload);

public sealed record OperationTaskFailedPayload(
    string OperationTaskId,
    string AttemptId,
    string InstanceKey,
    string OperationCode,
    DateTimeOffset FinishedAtUtc,
    string? FailureCode);
