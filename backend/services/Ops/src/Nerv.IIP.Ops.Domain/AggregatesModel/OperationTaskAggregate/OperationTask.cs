using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Nerv.IIP.Ops.Domain;
using Nerv.IIP.Ops.Domain.AggregatesModel.OperationTemplateAggregate;
using Nerv.IIP.Ops.Domain.DomainEvents;
using NetCorePal.Extensions.Domain;

namespace Nerv.IIP.Ops.Domain.AggregatesModel.OperationTaskAggregate;

public partial record OperationTaskId : IStringStronglyTypedId;
public partial record OperationAttemptId : IStringStronglyTypedId;
public partial record AuditRecordId : IStringStronglyTypedId;

public sealed class OperationTask : Entity<OperationTaskId>, IAggregateRoot
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly List<OperationAttempt> _attempts = [];
    private readonly List<AuditRecord> _auditRecords = [];

    private OperationTask()
    {
        Id = new OperationTaskId(string.Empty);
    }

    private OperationTask(
        OperationTaskId id,
        string organizationId,
        string environmentId,
        string instanceKey,
        string operationCode,
        string requestedBy,
        DateTimeOffset requestedAtUtc,
        string idempotencyKey,
        string correlationId,
        string parametersJson)
    {
        Id = id;
        OrganizationId = organizationId;
        EnvironmentId = environmentId;
        InstanceKey = instanceKey;
        OperationCode = operationCode;
        Status = "queued";
        RequestedBy = requestedBy;
        RequestedAtUtc = requestedAtUtc;
        IdempotencyKey = idempotencyKey;
        IdempotencyScope = GetIdempotencyScope(organizationId, environmentId, idempotencyKey);
        CorrelationId = correlationId;
        ParametersJson = parametersJson;

        var auditRecord = AddAudit(new AuditRecordId(""), "operation.requested", requestedBy, requestedAtUtc, correlationId);
        this.AddDomainEvent(new OperationTaskCreatedDomainEvent(this));
        AddAuditRecordedDomainEvent(auditRecord);
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string InstanceKey { get; private set; } = string.Empty;
    public string OperationCode { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public string RequestedBy { get; private set; } = string.Empty;
    public DateTimeOffset RequestedAtUtc { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string IdempotencyScope { get; private set; } = string.Empty;
    public string CorrelationId { get; private set; } = string.Empty;
    public string ParametersJson { get; private set; } = "{}";
    public int DefaultMaxAttempts { get; private set; } = 3;
    public int DefaultLeaseDurationSeconds { get; private set; } = 300;
    public bool RequiresApproval { get; private set; }
    public string? ApprovalStatus { get; private set; }
    public string? ApprovalRequestedBy { get; private set; }
    public DateTimeOffset? ApprovalRequestedAtUtc { get; private set; }
    public string? ApprovalDecidedBy { get; private set; }
    public DateTimeOffset? ApprovalDecidedAtUtc { get; private set; }
    public string? ApprovalDecisionReason { get; private set; }
    public Deleted Deleted { get; private set; } = new(false);
    public RowVersion RowVersion { get; private set; } = new(0);
    public IReadOnlyCollection<OperationAttempt> Attempts => _attempts;
    public IReadOnlyCollection<AuditRecord> AuditRecords => _auditRecords;

    public static OperationTask Create(
        OperationTaskId id,
        CreateOperationTaskInput request,
        OperationTemplateSnapshot template,
        DateTimeOffset now)
    {
        if (!string.Equals(request.OperationCode, template.OperationCode, StringComparison.Ordinal))
        {
            throw new InvalidOperationTaskRequestException($"Unsupported operation code: {request.OperationCode}");
        }

        if (!template.Enabled)
        {
            throw new InvalidOperationTaskRequestException($"Cannot create task from disabled operation template: {request.OperationCode}");
        }

        var task = new OperationTask(
            id,
            request.OrganizationId,
            request.EnvironmentId,
            request.InstanceKey,
            request.OperationCode,
            request.RequestedBy,
            now,
            request.IdempotencyKey,
            request.CorrelationId,
            JsonSerializer.Serialize(request.Parameters, JsonOptions))
        {
            DefaultMaxAttempts = Math.Clamp(template.DefaultMaxAttempts, 1, 10),
            DefaultLeaseDurationSeconds = Math.Clamp(template.DefaultLeaseDurationSeconds, 30, 3600),
            RequiresApproval = template.RequiresApproval
        };
        if (task.RequiresApproval)
        {
            task.RequestApproval(now);
        }

        return task;
    }

    public OperationTaskDispatchFact Claim(
        OperationAttemptId attemptId,
        AuditRecordId auditRecordId,
        string leaseId,
        string connectorHostId,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        int maxAttempts)
    {
        if (!CanBeClaimed())
        {
            throw new InvalidOperationResultException("Operation task is not claimable.");
        }

        var attemptNo = _attempts
            .Where(x => x.AttemptNo.HasValue)
            .Select(x => x.AttemptNo!.Value)
            .DefaultIfEmpty(0)
            .Max() + 1;
        if (attemptNo > maxAttempts)
        {
            throw new InvalidOperationResultException("Operation task has exhausted its maximum attempts.");
        }

        var attempt = new OperationAttempt(
            attemptId,
            Id,
            connectorHostId,
            "started",
            now,
            leaseId,
            now,
            now.Add(leaseDuration),
            attemptNo,
            maxAttempts);
        _attempts.Add(attempt);
        Status = "dispatched";
        var auditRecord = AddAudit(auditRecordId, "operation.claimed", connectorHostId, now, CorrelationId);
        this.AddDomainEvent(new OperationTaskDispatchedDomainEvent(this, attempt));
        AddAuditRecordedDomainEvent(auditRecord);

        return new OperationTaskDispatchFact(
            Id.Id,
            attempt.Id.Id,
            OrganizationId,
            EnvironmentId,
            connectorHostId,
            InstanceKey,
            OperationCode,
            CorrelationId,
            Parameters,
            attempt.LeaseId ?? leaseId,
            attempt.LeasedAtUtc ?? now,
            attempt.LeasedUntilUtc ?? now.Add(leaseDuration),
            attempt.AttemptNo ?? attemptNo,
            Math.Max(0, (int)leaseDuration.TotalSeconds),
            attempt.MaxAttempts ?? maxAttempts);
    }

    public void AbandonExpiredLease(AuditRecordId auditRecordId, DateTimeOffset now)
    {
        var attempt = GetActiveAttempt();
        if (attempt is null || attempt.LeasedUntilUtc > now)
        {
            return;
        }

        attempt.Abandon(now, "lease-timeout");
        Status = attempt.AttemptNo >= attempt.MaxAttempts ? "failed" : "queued";
        var auditRecord = AddAudit(auditRecordId, "operation.lease-timeout", attempt.ConnectorHostId, now, CorrelationId);
        AddAuditRecordedDomainEvent(auditRecord);
    }

    public OperationTaskDetailFact AbandonLease(string leaseId, string connectorHostId, string abandonReason, AuditRecordId auditRecordId, DateTimeOffset now)
    {
        var attempt = GetMatchingActiveLease(leaseId, connectorHostId);
        attempt.Abandon(now, abandonReason);
        Status = attempt.AttemptNo >= attempt.MaxAttempts ? "failed" : "queued";
        var auditRecord = AddAudit(auditRecordId, "operation.abandoned", connectorHostId, now, CorrelationId);
        AddAuditRecordedDomainEvent(auditRecord);
        return ToDetailFact();
    }

    public OperationTaskDetailFact HeartbeatLease(string leaseId, string connectorHostId, DateTimeOffset now, TimeSpan leaseDuration, AuditRecordId auditRecordId)
    {
        var attempt = GetMatchingActiveLease(leaseId, connectorHostId);
        if (attempt.LeasedUntilUtc <= now)
        {
            throw new InvalidOperationResultException("Operation task lease has expired.");
        }

        attempt.Heartbeat(now.Add(leaseDuration));
        var auditRecord = AddAudit(auditRecordId, "operation.heartbeat", connectorHostId, now, CorrelationId);
        AddAuditRecordedDomainEvent(auditRecord);
        return ToDetailFact();
    }

    public OperationTaskDetailFact RecordResult(OperationResultInput result, AuditRecordId auditRecordId)
    {
        var attempt = _attempts.SingleOrDefault(x => x.Id.Id == result.AttemptId && x.OperationTaskId == Id)
            ?? throw new InvalidOperationResultException("Operation result does not match an existing attempt.");

        ValidateResultOwnership(attempt, result);
        if (!string.Equals(attempt.Status, "started", StringComparison.Ordinal))
        {
            throw new InvalidOperationResultException("Operation result has already been recorded for this attempt.");
        }

        var completed = string.Equals(result.ExecutionStatus, "succeeded", StringComparison.OrdinalIgnoreCase);
        var status = completed ? "completed" : "failed";
        var auditAction = completed ? "operation.completed" : "operation.failed";

        attempt.Record(status, result.FinishedAtUtc, result.Failure);
        Status = status;
        var auditRecord = AddAudit(auditRecordId, auditAction, result.Context.ConnectorHostId, result.FinishedAtUtc, result.Context.CorrelationId);
        this.AddDomainEvent(new OperationResultRecordedDomainEvent(this, attempt));
        this.AddDomainEvent(completed
            ? new OperationTaskCompletedDomainEvent(this, attempt, result)
            : new OperationTaskFailedDomainEvent(this, attempt, result));
        AddAuditRecordedDomainEvent(auditRecord);
        return ToDetailFact();
    }

    public OperationTaskDetailFact Approve(DecideOperationApprovalInput request, AuditRecordId auditRecordId, DateTimeOffset now)
    {
        ValidateApprovalDecision(request);
        if (!RequiresApproval)
        {
            throw new InvalidOperationTaskRequestException("Operation task does not require approval.");
        }

        if (!string.Equals(Status, "approval-pending", StringComparison.Ordinal)
            || !string.Equals(ApprovalStatus, "pending", StringComparison.Ordinal))
        {
            throw new InvalidOperationTaskRequestException("Operation task approval is not pending.");
        }

        ApprovalStatus = "approved";
        ApprovalDecidedBy = request.Actor;
        ApprovalDecidedAtUtc = now;
        ApprovalDecisionReason = request.DecisionReason;
        Status = "queued";
        var auditRecord = AddAudit(auditRecordId, "operation.approved", request.Actor, now, request.CorrelationId);
        this.AddDomainEvent(new OperationTaskApprovedDomainEvent(this, request.Actor, request.DecisionReason, request.CorrelationId, now));
        AddAuditRecordedDomainEvent(auditRecord);
        return ToDetailFact();
    }

    public OperationTaskDetailFact Reject(DecideOperationApprovalInput request, AuditRecordId auditRecordId, DateTimeOffset now)
    {
        ValidateApprovalDecision(request);
        if (!RequiresApproval)
        {
            throw new InvalidOperationTaskRequestException("Operation task does not require approval.");
        }

        if (!string.Equals(Status, "approval-pending", StringComparison.Ordinal)
            || !string.Equals(ApprovalStatus, "pending", StringComparison.Ordinal))
        {
            throw new InvalidOperationTaskRequestException("Operation task approval is not pending.");
        }

        ApprovalStatus = "rejected";
        ApprovalDecidedBy = request.Actor;
        ApprovalDecidedAtUtc = now;
        ApprovalDecisionReason = request.DecisionReason;
        Status = "rejected";
        var auditRecord = AddAudit(auditRecordId, "operation.rejected", request.Actor, now, request.CorrelationId);
        this.AddDomainEvent(new OperationTaskRejectedDomainEvent(this, request.Actor, request.DecisionReason, request.CorrelationId, now));
        AddAuditRecordedDomainEvent(auditRecord);
        return ToDetailFact();
    }

    public AuditIntentResult SubmitAuditIntent(SubmitAuditIntentInput request, AuditRecordId auditRecordId, DateTimeOffset now)
    {
        AuditIntentValidator.Validate(request);
        if (!string.Equals(OrganizationId, request.OrganizationId, StringComparison.Ordinal)
            || !string.Equals(EnvironmentId, request.EnvironmentId, StringComparison.Ordinal))
        {
            throw new InvalidOperationTaskRequestException("Audit intent context does not match the operation task scope.");
        }

        var auditRecord = AddAudit(auditRecordId, request.Action, request.Actor, now, request.CorrelationId);
        AddAuditRecordedDomainEvent(auditRecord);
        var fact = auditRecord.ToFact();
        return new AuditIntentResult(
            fact.AuditRecordId,
            fact.OperationTaskId,
            fact.SequenceNo,
            fact.PreviousIntegrityHash,
            fact.Action,
            fact.Actor,
            fact.OccurredAtUtc,
            fact.CorrelationId,
            fact.IntegrityHash);
    }

    public OperationTaskFact ToFact()
    {
        return new OperationTaskFact(
            Id.Id,
            OrganizationId,
            EnvironmentId,
            InstanceKey,
            OperationCode,
            Status,
            RequestedBy,
            RequestedAtUtc,
            IdempotencyKey,
            CorrelationId,
            Parameters,
            DefaultMaxAttempts,
            DefaultLeaseDurationSeconds,
            RequiresApproval,
            ToApprovalFact());
    }

    public OperationTaskDetailFact ToDetailFact()
    {
        return new OperationTaskDetailFact(
            ToFact(),
            _attempts.Select(x => x.ToFact()).ToList(),
            _auditRecords.Select(x => x.ToFact()).ToList());
    }

    public static string GetIdempotencyScope(string organizationId, string environmentId, string idempotencyKey)
    {
        return $"{organizationId}\u001f{environmentId}\u001f{idempotencyKey}";
    }

    public void AssignInitialAuditId(AuditRecordId auditRecordId)
    {
        var pendingAudits = _auditRecords.Where(x => x.Id.Id.Length == 0).ToArray();
        if (pendingAudits.Length != 1)
        {
            throw new InvalidOperationTaskRequestException("Initial audit id assignment requires exactly one pending audit record.");
        }

        pendingAudits[0].AssignId(auditRecordId);
    }

    public void AssignPendingAuditIds(IReadOnlyCollection<AuditRecordId> auditRecordIds)
    {
        var pendingAudits = _auditRecords.Where(x => x.Id.Id.Length == 0).ToArray();
        if (pendingAudits.Length != auditRecordIds.Count)
        {
            throw new InvalidOperationTaskRequestException("Pending audit id count does not match operation task audit records.");
        }

        foreach (var (audit, auditRecordId) in pendingAudits.Zip(auditRecordIds))
        {
            var previous = GetPreviousAudit(audit);
            audit.AssignChain(
                auditRecordId,
                previous?.SequenceNo + 1 ?? 1,
                previous?.IntegrityHash ?? string.Empty);
        }
    }

    public void AssignAuditChainStamp(AuditRecordId auditRecordId, long sequenceNo, string previousIntegrityHash)
    {
        var audit = _auditRecords.SingleOrDefault(x => x.Id == auditRecordId)
            ?? throw new InvalidOperationTaskRequestException($"Audit record was not found: {auditRecordId.Id}");
        audit.AssignChain(auditRecordId, sequenceNo, previousIntegrityHash);
    }

    private OperationApprovalFact? ToApprovalFact()
    {
        if (!RequiresApproval && string.IsNullOrWhiteSpace(ApprovalStatus))
        {
            return null;
        }

        return new OperationApprovalFact(
            ApprovalStatus ?? "not-required",
            ApprovalRequestedBy ?? RequestedBy,
            ApprovalRequestedAtUtc ?? RequestedAtUtc,
            ApprovalDecidedBy,
            ApprovalDecidedAtUtc,
            ApprovalDecisionReason);
    }

    private void RequestApproval(DateTimeOffset now)
    {
        Status = "approval-pending";
        ApprovalStatus = "pending";
        ApprovalRequestedBy = RequestedBy;
        ApprovalRequestedAtUtc = now;
        var auditRecord = AddAudit(new AuditRecordId(string.Empty), "operation.approval-requested", RequestedBy, now, CorrelationId);
        this.AddDomainEvent(new OperationApprovalRequestedDomainEvent(this));
        AddAuditRecordedDomainEvent(auditRecord);
    }

    private void ValidateApprovalDecision(DecideOperationApprovalInput request)
    {
        if (!string.Equals(OrganizationId, request.OrganizationId, StringComparison.Ordinal)
            || !string.Equals(EnvironmentId, request.EnvironmentId, StringComparison.Ordinal))
        {
            throw new InvalidOperationTaskRequestException("Approval decision context does not match the operation task scope.");
        }

        if (string.IsNullOrWhiteSpace(request.Actor))
        {
            throw new InvalidOperationTaskRequestException("Approval decision actor is required.");
        }

        if (IsSameActor(RequestedBy, request.Actor))
        {
            throw new InvalidOperationTaskRequestException("Operation requester cannot approve or reject the same operation task.");
        }

        if (string.IsNullOrWhiteSpace(request.CorrelationId))
        {
            throw new InvalidOperationTaskRequestException("Approval decision correlation id is required.");
        }
    }

    private static bool IsSameActor(string left, string right)
    {
        static string Normalize(string value)
        {
            var trimmed = value.Trim();
            return trimmed.StartsWith("user:", StringComparison.OrdinalIgnoreCase)
                ? trimmed["user:".Length..]
                : trimmed;
        }

        return string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
    }

    private bool CanBeClaimed()
    {
        return string.Equals(Status, "queued", StringComparison.Ordinal);
    }

    private IReadOnlyDictionary<string, string> Parameters =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(ParametersJson, JsonOptions) ?? [];

    private void ValidateResultOwnership(OperationAttempt attempt, OperationResultInput result)
    {
        if (!string.Equals(attempt.ConnectorHostId, result.Context.ConnectorHostId, StringComparison.Ordinal)
            || !string.Equals(OrganizationId, result.Context.OrganizationId, StringComparison.Ordinal)
            || !string.Equals(EnvironmentId, result.Context.EnvironmentId, StringComparison.Ordinal)
            || !string.Equals(InstanceKey, result.InstanceKey, StringComparison.Ordinal)
            || !string.Equals(OperationCode, result.OperationCode, StringComparison.Ordinal))
        {
            throw new InvalidOperationResultException("Operation result context does not match the operation task attempt.");
        }
    }

    private OperationAttempt? GetActiveAttempt()
    {
        return _attempts
            .Where(x => string.Equals(x.Status, "started", StringComparison.Ordinal))
            .Where(x => x.HasClaimLease())
            .OrderByDescending(x => x.AttemptNo)
            .FirstOrDefault();
    }

    private OperationAttempt GetMatchingActiveLease(string leaseId, string connectorHostId)
    {
        var attempt = GetActiveAttempt()
            ?? throw new InvalidOperationResultException("Operation task does not have an active lease.");
        if (!string.Equals(attempt.LeaseId, leaseId, StringComparison.Ordinal)
            || !string.Equals(attempt.ConnectorHostId, connectorHostId, StringComparison.Ordinal))
        {
            throw new InvalidOperationResultException("Operation task lease does not match the active attempt.");
        }

        return attempt;
    }

    private AuditRecord AddAudit(AuditRecordId auditRecordId, string action, string actor, DateTimeOffset occurredAtUtc, string correlationId)
    {
        var previous = _auditRecords.OrderBy(x => x.SequenceNo).LastOrDefault();
        var auditRecord = new AuditRecord(
            auditRecordId,
            Id,
            previous?.SequenceNo + 1 ?? 1,
            previous?.IntegrityHash ?? string.Empty,
            action,
            actor,
            occurredAtUtc,
            correlationId);
        _auditRecords.Add(auditRecord);
        return auditRecord;
    }

    private void AddAuditRecordedDomainEvent(AuditRecord auditRecord)
    {
        this.AddDomainEvent(new AuditRecordedDomainEvent(this, auditRecord));
    }

    private AuditRecord? GetPreviousAudit(AuditRecord audit)
    {
        return _auditRecords
            .Where(x => !ReferenceEquals(x, audit))
            .Where(x => x.SequenceNo < audit.SequenceNo || audit.SequenceNo == 0)
            .OrderBy(x => x.SequenceNo)
            .LastOrDefault();
    }
}

public sealed class OperationAttempt : Entity<OperationAttemptId>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private OperationAttempt()
    {
        Id = new OperationAttemptId(string.Empty);
        OperationTaskId = new OperationTaskId(string.Empty);
    }

    internal OperationAttempt(
        OperationAttemptId id,
        OperationTaskId operationTaskId,
        string connectorHostId,
        string status,
        DateTimeOffset startedAtUtc,
        string leaseId,
        DateTimeOffset leasedAtUtc,
        DateTimeOffset leasedUntilUtc,
        int attemptNo,
        int maxAttempts)
    {
        Id = id;
        OperationTaskId = operationTaskId;
        ConnectorHostId = connectorHostId;
        Status = status;
        StartedAtUtc = startedAtUtc;
        LeaseId = leaseId;
        LeasedAtUtc = leasedAtUtc;
        LeasedUntilUtc = leasedUntilUtc;
        AttemptNo = attemptNo;
        MaxAttempts = maxAttempts;
    }

    public OperationTaskId OperationTaskId { get; private set; }
    public string ConnectorHostId { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset? FinishedAtUtc { get; private set; }
    public string? FailureJson { get; private set; }
    public string? LeaseId { get; private set; }
    public DateTimeOffset? LeasedAtUtc { get; private set; }
    public DateTimeOffset? LeasedUntilUtc { get; private set; }
    public int? AttemptNo { get; private set; }
    public int? MaxAttempts { get; private set; }
    public string? AbandonReason { get; private set; }

    internal void Record(string status, DateTimeOffset finishedAtUtc, OperationFailureFact? failure)
    {
        Status = status;
        FinishedAtUtc = finishedAtUtc;
        FailureJson = failure is null ? null : JsonSerializer.Serialize(failure, JsonOptions);
    }

    internal void Abandon(DateTimeOffset abandonedAtUtc, string abandonReason)
    {
        Status = "abandoned";
        FinishedAtUtc = abandonedAtUtc;
        AbandonReason = abandonReason;
    }

    internal void Heartbeat(DateTimeOffset leasedUntilUtc)
    {
        LeasedUntilUtc = leasedUntilUtc;
    }

    internal bool HasClaimLease()
    {
        return !string.IsNullOrWhiteSpace(LeaseId)
            && LeasedAtUtc.HasValue
            && LeasedUntilUtc.HasValue
            && AttemptNo.HasValue
            && MaxAttempts.HasValue;
    }

    internal OperationAttemptFact ToFact()
    {
        return new OperationAttemptFact(
            Id.Id,
            OperationTaskId.Id,
            ConnectorHostId,
            Status,
            StartedAtUtc,
            FinishedAtUtc,
            FailureJson is null ? null : JsonSerializer.Deserialize<OperationFailureFact>(FailureJson, JsonOptions),
            LeaseId ?? string.Empty,
            LeasedAtUtc ?? StartedAtUtc,
            LeasedUntilUtc ?? StartedAtUtc,
            AttemptNo ?? 0,
            MaxAttempts ?? 0,
            AbandonReason);
    }
}

public sealed class AuditRecord : Entity<AuditRecordId>
{
    private AuditRecord()
    {
        Id = new AuditRecordId(string.Empty);
        OperationTaskId = new OperationTaskId(string.Empty);
    }

    internal AuditRecord(
        AuditRecordId id,
        OperationTaskId operationTaskId,
        long sequenceNo,
        string previousIntegrityHash,
        string action,
        string actor,
        DateTimeOffset occurredAtUtc,
        string correlationId)
    {
        Id = id;
        OperationTaskId = operationTaskId;
        SequenceNo = sequenceNo;
        PreviousIntegrityHash = previousIntegrityHash;
        Action = action;
        Actor = actor;
        OccurredAtUtc = occurredAtUtc;
        CorrelationId = correlationId;
        IntegrityHash = ComputeIntegrityHash(id.Id, operationTaskId.Id, sequenceNo, previousIntegrityHash, action, actor, occurredAtUtc, correlationId);
    }

    public OperationTaskId OperationTaskId { get; private set; }
    public long SequenceNo { get; private set; }
    public string PreviousIntegrityHash { get; private set; } = string.Empty;
    public string Action { get; private set; } = string.Empty;
    public string Actor { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public string CorrelationId { get; private set; } = string.Empty;
    public string IntegrityHash { get; private set; } = string.Empty;

    internal void AssignId(AuditRecordId id)
    {
        AssignChain(id, SequenceNo, PreviousIntegrityHash);
    }

    internal void AssignChain(AuditRecordId id, long sequenceNo, string previousIntegrityHash)
    {
        if (sequenceNo < 1)
        {
            throw new InvalidOperationTaskRequestException("Audit record sequence number must be positive.");
        }

        Id = id;
        SequenceNo = sequenceNo;
        PreviousIntegrityHash = previousIntegrityHash;
        IntegrityHash = ComputeIntegrityHash(Id.Id, OperationTaskId.Id, SequenceNo, PreviousIntegrityHash, Action, Actor, OccurredAtUtc, CorrelationId);
    }

    internal AuditRecordFact ToFact()
    {
        return new AuditRecordFact(Id.Id, OperationTaskId.Id, SequenceNo, PreviousIntegrityHash, Action, Actor, OccurredAtUtc, CorrelationId, IntegrityHash);
    }

    public bool HasValidIntegrityHash() =>
        string.Equals(
            IntegrityHash,
            ComputeIntegrityHash(Id.Id, OperationTaskId.Id, SequenceNo, PreviousIntegrityHash, Action, Actor, OccurredAtUtc, CorrelationId),
            StringComparison.Ordinal);

    public static string ComputeIntegrityHash(
        string auditRecordId,
        string operationTaskId,
        long sequenceNo,
        string previousIntegrityHash,
        string action,
        string actor,
        DateTimeOffset occurredAtUtc,
        string correlationId)
    {
        var material = string.Join(
            "\u001f",
            auditRecordId,
            operationTaskId,
            sequenceNo.ToString(System.Globalization.CultureInfo.InvariantCulture),
            previousIntegrityHash,
            action,
            actor,
            occurredAtUtc.ToUniversalTime().ToString("O"),
            correlationId);
        return $"sha256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant()}";
    }
}
