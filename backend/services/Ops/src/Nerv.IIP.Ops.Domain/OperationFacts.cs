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
    bool RequiresApproval);

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
    string CorrelationId);

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
                x.CorrelationId))
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
            attemptSummaries.LastOrDefault()?.AttemptId,
            attemptSummaries,
            auditSummaries);
    }

    private static int GetLeaseDurationSeconds(DateTimeOffset leasedAtUtc, DateTimeOffset leasedUntilUtc)
    {
        return Math.Max(0, (int)(leasedUntilUtc - leasedAtUtc).TotalSeconds);
    }
}
