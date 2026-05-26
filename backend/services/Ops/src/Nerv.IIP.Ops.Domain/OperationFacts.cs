using Nerv.IIP.Contracts.ConnectorProtocol;
using Nerv.IIP.Contracts.Ops;

namespace Nerv.IIP.Ops.Domain;

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

public sealed record OperationAttemptFact(
    string AttemptId,
    string OperationTaskId,
    string ConnectorHostId,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    FailureReason? Failure,
    string LeaseId,
    DateTimeOffset LeasedAtUtc,
    DateTimeOffset LeasedUntilUtc,
    int AttemptNo,
    int MaxAttempts,
    string? AbandonReason);

public sealed record AuditRecordFact(
    string AuditRecordId,
    string OperationTaskId,
    string Action,
    string Actor,
    DateTimeOffset OccurredAtUtc,
    string CorrelationId,
    string IntegrityHash);

public static class AuditRecordMapper
{
    public static AuditIntentResponse ToIntentResponse(AuditRecordFact auditRecord)
    {
        return new AuditIntentResponse(
            auditRecord.AuditRecordId,
            auditRecord.OperationTaskId,
            auditRecord.Action,
            auditRecord.Actor,
            auditRecord.OccurredAtUtc,
            auditRecord.CorrelationId,
            auditRecord.IntegrityHash);
    }
}

public static class AuditIntentValidator
{
    private const int MaxAuditFieldLength = 128;

    public static void Validate(SubmitAuditIntentRequest request)
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

public static class OperationTaskMapper
{
    public static OperationTaskResponse ToResponse(
        OperationTaskFact task,
        IEnumerable<OperationAttemptFact> attempts,
        IEnumerable<AuditRecordFact> auditRecords)
    {
        var attemptSummaries = attempts
            .Select(x => new OperationAttemptSummary(
                x.AttemptId,
                x.Status,
                x.StartedAtUtc,
                x.FinishedAtUtc,
                x.Failure?.Code,
                x.LeaseId,
                x.LeasedAtUtc,
                x.LeasedUntilUtc,
                x.AttemptNo,
                GetLeaseDurationSeconds(x.LeasedAtUtc, x.LeasedUntilUtc),
                x.MaxAttempts,
                x.AbandonReason))
            .ToList();

        var auditSummaries = auditRecords
            .Select(x => new AuditRecordSummary(
                x.AuditRecordId,
                x.OperationTaskId,
                x.Action,
                x.Actor,
                x.OccurredAtUtc,
                x.CorrelationId,
                x.IntegrityHash))
            .ToList();

        return new OperationTaskResponse(
            task.OperationTaskId,
            task.OrganizationId,
            task.EnvironmentId,
            task.InstanceKey,
            task.OperationCode,
            task.Status,
            task.RequestedBy,
            task.RequestedAtUtc,
            task.Approval is null
                ? null
                : new OperationApprovalSummary(
                    task.Approval.Status,
                    task.Approval.RequestedBy,
                    task.Approval.RequestedAtUtc,
                    task.Approval.DecidedBy,
                    task.Approval.DecidedAtUtc,
                    task.Approval.DecisionReason),
            attemptSummaries.LastOrDefault()?.AttemptId,
            attemptSummaries,
            auditSummaries);
    }

    private static int GetLeaseDurationSeconds(DateTimeOffset leasedAtUtc, DateTimeOffset leasedUntilUtc)
    {
        return Math.Max(0, (int)(leasedUntilUtc - leasedAtUtc).TotalSeconds);
    }
}
