namespace Nerv.IIP.Contracts.Ops;

public sealed record CreateOperationTaskRequest(
    string OrganizationId,
    string EnvironmentId,
    string InstanceKey,
    string OperationCode,
    string IdempotencyKey,
    string RequestedBy,
    string Reason,
    string CorrelationId,
    IReadOnlyDictionary<string, string> Parameters);

public sealed record OperationTaskResponse(
    string OperationTaskId,
    string OrganizationId,
    string EnvironmentId,
    string InstanceKey,
    string OperationCode,
    string Status,
    string RequestedBy,
    DateTimeOffset RequestedAtUtc,
    string? CurrentAttemptId,
    IReadOnlyList<OperationAttemptSummary> Attempts,
    IReadOnlyList<AuditRecordSummary> AuditRecords);

public sealed record OperationAttemptSummary(
    string AttemptId,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    string? FailureCode);

public sealed record AuditRecordSummary(
    string AuditRecordId,
    string OperationTaskId,
    string Action,
    string Actor,
    DateTimeOffset OccurredAtUtc,
    string CorrelationId);

public sealed record PendingOperationTasksResponse(IReadOnlyList<OperationTaskDispatchItem> Items);

public sealed record OperationTaskDispatchItem(
    string OperationTaskId,
    string AttemptId,
    string OrganizationId,
    string EnvironmentId,
    string ConnectorHostId,
    string InstanceKey,
    string OperationCode,
    string CorrelationId,
    IReadOnlyDictionary<string, string> Parameters);
