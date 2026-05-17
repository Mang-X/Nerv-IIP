using System.Text.Json;
using Nerv.IIP.Contracts.ConnectorProtocol;
using Nerv.IIP.Contracts.Ops;
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

        AddAudit(new AuditRecordId(""), "operation.requested", requestedBy, requestedAtUtc, correlationId);
        this.AddDomainEvent(new OperationTaskCreatedDomainEvent(this));
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
    public Deleted Deleted { get; private set; } = new(false);
    public RowVersion RowVersion { get; private set; } = new(0);
    public IReadOnlyCollection<OperationAttempt> Attempts => _attempts;
    public IReadOnlyCollection<AuditRecord> AuditRecords => _auditRecords;

    public static OperationTask Create(OperationTaskId id, CreateOperationTaskRequest request, DateTimeOffset now)
    {
        if (!string.Equals(request.OperationCode, "lifecycle.restart", StringComparison.Ordinal))
        {
            throw new InvalidOperationTaskRequestException($"Unsupported operation code: {request.OperationCode}");
        }

        return new OperationTask(
            id,
            request.OrganizationId,
            request.EnvironmentId,
            request.InstanceKey,
            request.OperationCode,
            request.RequestedBy,
            now,
            request.IdempotencyKey,
            request.CorrelationId,
            JsonSerializer.Serialize(request.Parameters, JsonOptions));
    }

    public OperationTaskDispatchItem Dispatch(OperationAttemptId attemptId, AuditRecordId auditRecordId, string connectorHostId, DateTimeOffset now)
    {
        if (!string.Equals(Status, "queued", StringComparison.Ordinal))
        {
            throw new InvalidOperationResultException("Operation task is not pending dispatch.");
        }

        var attempt = new OperationAttempt(attemptId, Id, connectorHostId, "started", now);
        _attempts.Add(attempt);
        Status = "dispatched";
        AddAudit(auditRecordId, "operation.dispatched", connectorHostId, now, CorrelationId);
        this.AddDomainEvent(new OperationTaskDispatchedDomainEvent(this, attempt));

        return new OperationTaskDispatchItem(
            Id.Id,
            attempt.Id.Id,
            OrganizationId,
            EnvironmentId,
            connectorHostId,
            InstanceKey,
            OperationCode,
            CorrelationId,
            Parameters);
    }

    public OperationTaskResponse RecordResult(OperationResult result, AuditRecordId auditRecordId)
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
        AddAudit(auditRecordId, auditAction, result.Context.ConnectorHostId, result.FinishedAtUtc, result.Context.CorrelationId);
        this.AddDomainEvent(new OperationResultRecordedDomainEvent(this, attempt));
        return ToResponse();
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
            Parameters);
    }

    public OperationTaskResponse ToResponse()
    {
        return OperationTaskMapper.ToResponse(
            ToFact(),
            _attempts.Select(x => x.ToFact()),
            _auditRecords.Select(x => x.ToFact()));
    }

    public static string GetIdempotencyScope(string organizationId, string environmentId, string idempotencyKey)
    {
        return $"{organizationId}\u001f{environmentId}\u001f{idempotencyKey}";
    }

    public void AssignInitialAuditId(AuditRecordId auditRecordId)
    {
        var audit = _auditRecords.Single(x => x.Id.Id.Length == 0);
        audit.AssignId(auditRecordId);
    }

    private IReadOnlyDictionary<string, string> Parameters =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(ParametersJson, JsonOptions) ?? [];

    private void ValidateResultOwnership(OperationAttempt attempt, OperationResult result)
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

    private void AddAudit(AuditRecordId auditRecordId, string action, string actor, DateTimeOffset occurredAtUtc, string correlationId)
    {
        _auditRecords.Add(new AuditRecord(auditRecordId, Id, action, actor, occurredAtUtc, correlationId));
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

    internal OperationAttempt(OperationAttemptId id, OperationTaskId operationTaskId, string connectorHostId, string status, DateTimeOffset startedAtUtc)
    {
        Id = id;
        OperationTaskId = operationTaskId;
        ConnectorHostId = connectorHostId;
        Status = status;
        StartedAtUtc = startedAtUtc;
    }

    public OperationTaskId OperationTaskId { get; private set; }
    public string ConnectorHostId { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset? FinishedAtUtc { get; private set; }
    public string? FailureJson { get; private set; }

    internal void Record(string status, DateTimeOffset finishedAtUtc, FailureReason? failure)
    {
        Status = status;
        FinishedAtUtc = finishedAtUtc;
        FailureJson = failure is null ? null : JsonSerializer.Serialize(failure, JsonOptions);
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
            FailureJson is null ? null : JsonSerializer.Deserialize<FailureReason>(FailureJson, JsonOptions));
    }
}

public sealed class AuditRecord : Entity<AuditRecordId>
{
    private AuditRecord()
    {
        Id = new AuditRecordId(string.Empty);
        OperationTaskId = new OperationTaskId(string.Empty);
    }

    internal AuditRecord(AuditRecordId id, OperationTaskId operationTaskId, string action, string actor, DateTimeOffset occurredAtUtc, string correlationId)
    {
        Id = id;
        OperationTaskId = operationTaskId;
        Action = action;
        Actor = actor;
        OccurredAtUtc = occurredAtUtc;
        CorrelationId = correlationId;
    }

    public OperationTaskId OperationTaskId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string Actor { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public string CorrelationId { get; private set; } = string.Empty;

    internal void AssignId(AuditRecordId id)
    {
        Id = id;
    }

    internal AuditRecordFact ToFact()
    {
        return new AuditRecordFact(Id.Id, OperationTaskId.Id, Action, Actor, OccurredAtUtc, CorrelationId);
    }
}
