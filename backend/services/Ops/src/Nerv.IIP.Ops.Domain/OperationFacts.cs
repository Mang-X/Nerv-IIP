using Nerv.IIP.Contracts.ConnectorProtocol;

namespace Nerv.IIP.Ops.Domain;

public sealed record CreateOperationTaskInput(
    string OrganizationId,
    string EnvironmentId,
    string InstanceKey,
    string OperationCode,
    string IdempotencyKey,
    string RequestedBy,
    string CorrelationId,
    IReadOnlyDictionary<string, string> Parameters);

public sealed record ClaimOperationTasksInput(
    string OrganizationId,
    string EnvironmentId,
    string ConnectorHostId,
    int Take);

public sealed record AbandonOperationTaskLeaseInput(
    string OrganizationId,
    string EnvironmentId,
    string ConnectorHostId,
    string LeaseId,
    string AbandonReason);

public sealed record HeartbeatOperationTaskLeaseInput(
    string OrganizationId,
    string EnvironmentId,
    string ConnectorHostId,
    string LeaseId,
    int LeaseDurationSeconds);

public sealed record DecideOperationApprovalInput(
    string OrganizationId,
    string EnvironmentId,
    string Actor,
    string DecisionReason,
    string CorrelationId);

public sealed record SubmitAuditIntentInput(
    string OrganizationId,
    string EnvironmentId,
    string OperationTaskId,
    string Action,
    string Actor,
    string CorrelationId);

public sealed record OperationResultInput(
    ConnectorRequestContext Context,
    string OperationTaskId,
    string AttemptId,
    string InstanceKey,
    string OperationCode,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset FinishedAtUtc,
    string ExecutionStatus,
    OperationFailureFact? Failure,
    IReadOnlyDictionary<string, string> Output);

public sealed record OperationFailureFact(
    string Code,
    string Message,
    string Category,
    bool Retryable,
    IReadOnlyDictionary<string, string> Detail);

public sealed record OperationTaskFact(
    string OperationTaskId,
    string OrganizationId,
    string EnvironmentId,
    string InstanceKey,
    string OperationCode,
    string Status,
    string RequestedBy,
    DateTimeOffset RequestedAtUtc,
    string IdempotencyKey,
    string CorrelationId,
    IReadOnlyDictionary<string, string> Parameters,
    int DefaultMaxAttempts,
    int DefaultLeaseDurationSeconds,
    bool RequiresApproval,
    OperationApprovalFact? Approval);

public sealed record OperationApprovalFact(
    string Status,
    string RequestedBy,
    DateTimeOffset RequestedAtUtc,
    string? DecidedBy,
    DateTimeOffset? DecidedAtUtc,
    string? DecisionReason);

public sealed record OperationTaskDetailFact(
    OperationTaskFact Task,
    IReadOnlyList<OperationAttemptFact> Attempts,
    IReadOnlyList<AuditRecordFact> AuditRecords)
{
    public string OperationTaskId => Task.OperationTaskId;
    public string OrganizationId => Task.OrganizationId;
    public string EnvironmentId => Task.EnvironmentId;
    public string InstanceKey => Task.InstanceKey;
    public string OperationCode => Task.OperationCode;
    public string Status => Task.Status;
    public string RequestedBy => Task.RequestedBy;
    public DateTimeOffset RequestedAtUtc => Task.RequestedAtUtc;
    public OperationApprovalFact? Approval => Task.Approval;
    public string? CurrentAttemptId => Attempts.OrderByDescending(x => x.StartedAtUtc).FirstOrDefault()?.AttemptId;
}

public sealed record OperationAttemptFact(
    string AttemptId,
    string OperationTaskId,
    string ConnectorHostId,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    OperationFailureFact? Failure,
    string LeaseId,
    DateTimeOffset LeasedAtUtc,
    DateTimeOffset LeasedUntilUtc,
    int AttemptNo,
    int MaxAttempts,
    string? AbandonReason)
{
    public string? FailureCode => Failure?.Code;
}

public sealed record AuditRecordFact(
    string AuditRecordId,
    string OperationTaskId,
    long SequenceNo,
    string PreviousIntegrityHash,
    string Action,
    string Actor,
    DateTimeOffset OccurredAtUtc,
    string CorrelationId,
    string IntegrityHash);

public sealed record OperationTaskListResult(int Page, int PageSize, int TotalCount, IReadOnlyList<OperationTaskListItemFact> Items);

public sealed record OperationTaskListItemFact(
    string OperationTaskId,
    string OrganizationId,
    string EnvironmentId,
    string InstanceKey,
    string OperationCode,
    string Status,
    string RequestedBy,
    DateTimeOffset RequestedAtUtc,
    string? CurrentAttemptId);

public sealed record AuditRecordListResult(IReadOnlyList<AuditRecordFact> Items);

public sealed record AuditIntegrityValidationResult(
    bool IsValid,
    int CheckedRecords,
    string? FirstInvalidAuditRecordId,
    long? FirstInvalidSequenceNo,
    string? FailureCode,
    string? FailureMessage)
{
    public static AuditIntegrityValidationResult Valid(int checkedRecords) =>
        new(true, checkedRecords, null, null, null, null);

    public static AuditIntegrityValidationResult Invalid(
        int checkedRecords,
        AuditRecordFact audit,
        string failureCode,
        string failureMessage) =>
        new(false, checkedRecords, audit.AuditRecordId, audit.SequenceNo, failureCode, failureMessage);
}

public sealed record AuditChainHead(long SequenceNo, string IntegrityHash);

public sealed record AuditIntentResult(
    string AuditRecordId,
    string OperationTaskId,
    long SequenceNo,
    string PreviousIntegrityHash,
    string Action,
    string Actor,
    DateTimeOffset OccurredAtUtc,
    string CorrelationId,
    string IntegrityHash);

public sealed record CreateOperationTemplateInput(
    string OperationCode,
    string DisplayName,
    string ParameterSchemaJson,
    string RiskLevel,
    int DefaultMaxAttempts,
    int DefaultLeaseDurationSeconds,
    bool RequiresApproval);

public sealed record OperationTemplateFact(
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

public sealed record OperationTemplateListResult(IReadOnlyList<OperationTemplateFact> Items);

public sealed record PendingOperationTasksResult(IReadOnlyList<OperationTaskDispatchFact> Items);

public sealed record OperationTaskDispatchFact(
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

public static class AuditIntentValidator
{
    private const int MaxAuditFieldLength = 128;

    public static void Validate(SubmitAuditIntentInput request)
    {
        ValidateRequired(request.OrganizationId, nameof(request.OrganizationId));
        ValidateRequired(request.EnvironmentId, nameof(request.EnvironmentId));
        ValidateRequired(request.OperationTaskId, nameof(request.OperationTaskId));
        ValidateAuditField(request.Action, nameof(request.Action));
        ValidateAuditField(request.Actor, nameof(request.Actor));
        ValidateAuditField(request.CorrelationId, nameof(request.CorrelationId));
    }

    private static void ValidateAuditField(string value, string fieldName)
    {
        ValidateRequired(value, fieldName);
        if (value.Length > MaxAuditFieldLength)
        {
            throw new InvalidOperationTaskRequestException($"Audit intent {fieldName} must be at most {MaxAuditFieldLength} characters.");
        }
    }

    private static void ValidateRequired(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationTaskRequestException($"Audit intent {fieldName} is required.");
        }
    }
}

public static class AuditIntegrityValidator
{
    public static AuditIntegrityValidationResult Validate(IReadOnlyList<AuditRecordFact> records)
    {
        var ordered = records.OrderBy(x => x.SequenceNo).ThenBy(x => x.AuditRecordId, StringComparer.Ordinal).ToArray();
        var expectedSequenceNo = 1L;
        var previousHash = string.Empty;

        for (var index = 0; index < ordered.Length; index++)
        {
            var audit = ordered[index];
            var checkedRecords = index + 1;
            if (audit.SequenceNo != expectedSequenceNo)
            {
                return AuditIntegrityValidationResult.Invalid(
                    checkedRecords,
                    audit,
                    "sequence-gap",
                    $"Expected audit sequence {expectedSequenceNo}, found {audit.SequenceNo}.");
            }

            if (!string.Equals(audit.PreviousIntegrityHash, previousHash, StringComparison.Ordinal))
            {
                return AuditIntegrityValidationResult.Invalid(
                    checkedRecords,
                    audit,
                    "previous-hash-mismatch",
                    "Audit record previous hash does not match the prior record hash.");
            }

            var expectedHash = AggregatesModel.OperationTaskAggregate.AuditRecord.ComputeIntegrityHash(
                audit.AuditRecordId,
                audit.OperationTaskId,
                audit.SequenceNo,
                audit.PreviousIntegrityHash,
                audit.Action,
                audit.Actor,
                audit.OccurredAtUtc,
                audit.CorrelationId);
            if (!string.Equals(audit.IntegrityHash, expectedHash, StringComparison.Ordinal))
            {
                return AuditIntegrityValidationResult.Invalid(
                    checkedRecords,
                    audit,
                    "hash-mismatch",
                    "Audit record integrity hash does not match its immutable fields.");
            }

            expectedSequenceNo++;
            previousHash = audit.IntegrityHash;
        }

        return AuditIntegrityValidationResult.Valid(ordered.Length);
    }
}
