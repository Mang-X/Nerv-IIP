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

public sealed record PagedOperationTaskListResponse(
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<OperationTaskListItem> Items);

public sealed record OperationTaskListItem(
    string OperationTaskId,
    string OrganizationId,
    string EnvironmentId,
    string InstanceKey,
    string OperationCode,
    string Status,
    string RequestedBy,
    DateTimeOffset RequestedAtUtc,
    string? CurrentAttemptId);

public sealed record OperationAttemptSummary(
    string AttemptId,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    string? FailureCode,
    string LeaseId,
    DateTimeOffset LeasedAtUtc,
    DateTimeOffset LeasedUntilUtc,
    int AttemptNo,
    int LeaseDurationSeconds,
    int MaxAttempts,
    string? AbandonReason);

public sealed record AuditRecordSummary(
    string AuditRecordId,
    string OperationTaskId,
    string Action,
    string Actor,
    DateTimeOffset OccurredAtUtc,
    string CorrelationId);

public sealed record AuditRecordListResponse(IReadOnlyList<AuditRecordSummary> Items);

public sealed record SubmitAuditIntentRequest(
    string OrganizationId,
    string EnvironmentId,
    string OperationTaskId,
    string Action,
    string Actor,
    string CorrelationId);

public sealed record AuditIntentResponse(
    string AuditRecordId,
    string OperationTaskId,
    string Action,
    string Actor,
    DateTimeOffset OccurredAtUtc,
    string CorrelationId);

public sealed record CreateOperationTemplateRequest(
    string OperationCode,
    string DisplayName,
    string ParameterSchemaJson,
    string RiskLevel,
    int DefaultMaxAttempts,
    int DefaultLeaseDurationSeconds,
    bool RequiresApproval);

public sealed record OperationTemplateResponse(
    string OperationTemplateId,
    string OperationCode,
    string DisplayName,
    string ParameterSchemaJson,
    string RiskLevel,
    int DefaultMaxAttempts,
    int DefaultLeaseDurationSeconds,
    bool RequiresApproval,
    bool Enabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record OperationTemplateListResponse(IReadOnlyList<OperationTemplateResponse> Items);

public sealed record PendingOperationTasksResponse(IReadOnlyList<OperationTaskDispatchItem> Items);

public sealed record ClaimOperationTasksRequest(
    string OrganizationId,
    string EnvironmentId,
    string ConnectorHostId,
    int Take,
    /// <summary>
    /// Ignored by Ops; claimed tasks use the lease duration captured from their operation template.
    /// </summary>
    int LeaseDurationSeconds = 300,
    /// <summary>
    /// Ignored by Ops; claimed tasks use max attempts captured from their operation template.
    /// </summary>
    int MaxAttempts = 3);

public sealed record AbandonOperationTaskLeaseRequest(
    string OrganizationId,
    string EnvironmentId,
    string ConnectorHostId,
    string LeaseId,
    string AbandonReason);

public sealed record HeartbeatOperationTaskLeaseRequest(
    string OrganizationId,
    string EnvironmentId,
    string ConnectorHostId,
    string LeaseId,
    int LeaseDurationSeconds = 300);

public sealed record OperationTaskDispatchItem(
    string OperationTaskId,
    string AttemptId,
    string OrganizationId,
    string EnvironmentId,
    string ConnectorHostId,
    string InstanceKey,
    string OperationCode,
    string CorrelationId,
    IReadOnlyDictionary<string, string> Parameters,
    string LeaseId,
    DateTimeOffset LeasedAtUtc,
    DateTimeOffset LeasedUntilUtc,
    int AttemptNo,
    int LeaseDurationSeconds,
    int MaxAttempts);
