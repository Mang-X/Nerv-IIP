using Nerv.IIP.Contracts.ConnectorProtocol;
using Nerv.IIP.Ops.Domain.AggregatesModel.OperationTemplateAggregate;

namespace Nerv.IIP.Ops.Domain;

public abstract class OpsStateException(string message) : Exception(message);
public sealed class OperationTaskNotFoundException(string operationTaskId)
    : OpsStateException($"Operation task was not found: {operationTaskId}");
public sealed class OperationTemplateNotFoundException(string operationCode)
    : OpsStateException($"Operation template was not found: {operationCode}");
public sealed class InvalidOperationResultException(string message) : OpsStateException(message);
public sealed class InvalidOperationTaskRequestException(string message) : OpsStateException(message);

public interface IOpsStateStore
{
    OperationTaskDetailFact Create(CreateOperationTaskInput request, DateTimeOffset now);
    OperationTaskDetailFact Get(string operationTaskId);
    OperationTaskListResult ListTasks(string organizationId, string environmentId, int? page, int? pageSize);
    AuditRecordListResult ListAuditRecords(string organizationId, string environmentId, string? operationTaskId);
    AuditIntegrityValidationResult ValidateAuditIntegrity(string organizationId, string environmentId);
    AuditIntentResult SubmitAuditIntent(SubmitAuditIntentInput request, DateTimeOffset now);
    OperationTemplateFact CreateTemplate(CreateOperationTemplateInput request, DateTimeOffset now);
    OperationTemplateListResult ListTemplates();
    OperationTemplateFact GetTemplate(string operationCode);
    PendingOperationTasksResult ClaimPending(ClaimOperationTasksInput request, DateTimeOffset now);
    OperationTaskDetailFact AbandonLease(string operationTaskId, AbandonOperationTaskLeaseInput request, DateTimeOffset now);
    OperationTaskDetailFact HeartbeatLease(string operationTaskId, HeartbeatOperationTaskLeaseInput request, DateTimeOffset now);
    OperationTaskDetailFact RecordResult(OperationResultInput result);
    OperationTaskDetailFact Approve(string operationTaskId, DecideOperationApprovalInput request, DateTimeOffset now);
    OperationTaskDetailFact Reject(string operationTaskId, DecideOperationApprovalInput request, DateTimeOffset now);
}

public sealed class InMemoryOpsStateStore : IOpsStateStore
{
    private const int MaxAuditRecordsResponseSize = 500;
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _idempotency = new(StringComparer.Ordinal);
    private readonly Dictionary<string, OperationTemplateSnapshot> _templates = new(StringComparer.Ordinal)
    {
        ["lifecycle.restart"] = new OperationTemplateSnapshot("lifecycle.restart", true, 3, 300, false)
    };
    private readonly Dictionary<string, OperationTemplateFact> _templateResponses = new(StringComparer.Ordinal)
    {
        ["lifecycle.restart"] = BuiltInTemplateResponse()
    };
    private readonly List<OperationTaskFact> _tasks = [];
    private readonly List<OperationAttemptFact> _attempts = [];
    private readonly List<AuditRecordFact> _auditRecords = [];

    public OperationTaskDetailFact Create(CreateOperationTaskInput request, DateTimeOffset now)
    {
        lock (_gate)
        {
            var idempotencyScope = GetIdempotencyScope(request.OrganizationId, request.EnvironmentId, request.IdempotencyKey);
            if (_idempotency.TryGetValue(idempotencyScope, out var existingTaskId))
            {
                return GetUnlocked(existingTaskId);
            }

            if (!_templates.TryGetValue(request.OperationCode, out var template))
            {
                throw new InvalidOperationTaskRequestException($"Unsupported operation code: {request.OperationCode}");
            }

            if (!template.Enabled)
            {
                throw new InvalidOperationTaskRequestException($"Cannot create task from disabled operation template: {request.OperationCode}");
            }

            var taskId = $"op-{_tasks.Count + 1:000000}";
            var task = new OperationTaskFact(
                taskId,
                request.OrganizationId,
                request.EnvironmentId,
                request.InstanceKey,
                request.OperationCode,
                template.RequiresApproval ? "approval-pending" : "queued",
                request.RequestedBy,
                now,
                request.IdempotencyKey,
                request.CorrelationId,
                new Dictionary<string, string>(request.Parameters, StringComparer.Ordinal),
                template.DefaultMaxAttempts,
                template.DefaultLeaseDurationSeconds,
                template.RequiresApproval,
                template.RequiresApproval
                    ? new OperationApprovalFact("pending", request.RequestedBy, now, null, null, null)
                    : null);

            _tasks.Add(task);
            _idempotency[idempotencyScope] = taskId;
            AddAudit(taskId, "operation.requested", request.RequestedBy, now, request.CorrelationId);
            if (template.RequiresApproval)
            {
                AddAudit(taskId, "operation.approval-requested", request.RequestedBy, now, request.CorrelationId);
            }

            return GetUnlocked(taskId);
        }
    }

    public OperationTaskDetailFact Get(string operationTaskId)
    {
        lock (_gate)
        {
            return GetUnlocked(operationTaskId);
        }
    }

    public OperationTaskListResult ListTasks(string organizationId, string environmentId, int? page, int? pageSize)
    {
        lock (_gate)
        {
            var resolvedPage = page is > 0 ? page.Value : 1;
            var resolvedPageSize = pageSize is > 0 ? Math.Min(pageSize.Value, 200) : 20;
            var query = _tasks
                .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
                .OrderByDescending(x => x.RequestedAtUtc)
                .ThenByDescending(x => x.OperationTaskId, StringComparer.Ordinal)
                .ToArray();
            var items = query
                .Skip((resolvedPage - 1) * resolvedPageSize)
                .Take(resolvedPageSize)
                .Select(x => new OperationTaskListItemFact(
                    x.OperationTaskId,
                    x.OrganizationId,
                    x.EnvironmentId,
                    x.InstanceKey,
                    x.OperationCode,
                    x.Status,
                    x.RequestedBy,
                    x.RequestedAtUtc,
                    _attempts
                        .Where(a => a.OperationTaskId == x.OperationTaskId)
                        .OrderByDescending(a => a.StartedAtUtc)
                        .Select(a => a.AttemptId)
                        .FirstOrDefault()))
                .ToArray();

            return new OperationTaskListResult(resolvedPage, resolvedPageSize, query.Length, items);
        }
    }

    public AuditRecordListResult ListAuditRecords(string organizationId, string environmentId, string? operationTaskId)
    {
        lock (_gate)
        {
            var taskIds = _tasks
                .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
                .Where(x => string.IsNullOrWhiteSpace(operationTaskId) || x.OperationTaskId == operationTaskId)
                .Select(x => x.OperationTaskId)
                .ToHashSet(StringComparer.Ordinal);
            var items = _auditRecords
                .Where(x => taskIds.Contains(x.OperationTaskId))
                .OrderByDescending(x => x.OccurredAtUtc)
                .ThenByDescending(x => x.AuditRecordId, StringComparer.Ordinal)
                .Take(MaxAuditRecordsResponseSize)
                .ToArray();

            return new AuditRecordListResult(items);
        }
    }

    public AuditIntegrityValidationResult ValidateAuditIntegrity(string organizationId, string environmentId)
    {
        lock (_gate)
        {
            return AuditIntegrityValidator.Validate(ListAuditRecords(organizationId, environmentId, null).Items);
        }
    }

    public AuditIntentResult SubmitAuditIntent(SubmitAuditIntentInput request, DateTimeOffset now)
    {
        lock (_gate)
        {
            AuditIntentValidator.Validate(request);
            var task = FindTask(request.OperationTaskId);
            ValidateAuditIntentScope(task, request);
            var audit = AddAudit(request.OperationTaskId, request.Action, request.Actor, now, request.CorrelationId);
            return new AuditIntentResult(
                audit.AuditRecordId,
                audit.OperationTaskId,
                audit.SequenceNo,
                audit.PreviousIntegrityHash,
                audit.Action,
                audit.Actor,
                audit.OccurredAtUtc,
                audit.CorrelationId,
                audit.IntegrityHash);
        }
    }

    public OperationTemplateFact CreateTemplate(CreateOperationTemplateInput request, DateTimeOffset now)
    {
        lock (_gate)
        {
            var template = OperationTemplate.Create(
                new OperationTemplateId($"opt-{Guid.CreateVersion7():N}"),
                request.OperationCode,
                request.DisplayName,
                request.ParameterSchemaJson,
                request.RiskLevel,
                request.DefaultMaxAttempts,
                request.DefaultLeaseDurationSeconds,
                request.RequiresApproval,
                now);
            if (_templateResponses.ContainsKey(template.OperationCode))
            {
                throw new InvalidOperationTaskRequestException($"Operation template already exists: {template.OperationCode}");
            }

            var response = new OperationTemplateFact(
                template.Id.Id,
                template.OperationCode,
                template.DisplayName,
                template.ParameterSchemaJson,
                template.RiskLevel,
                template.DefaultMaxAttempts,
                template.DefaultLeaseDurationSeconds,
                template.RequiresApproval,
                template.Enabled,
                template.CreatedAtUtc,
                template.UpdatedAtUtc);
            _templateResponses.Add(response.OperationCode, response);
            _templates.Add(response.OperationCode, template.ToSnapshot());
            return response;
        }
    }

    public OperationTemplateListResult ListTemplates()
    {
        lock (_gate)
        {
            return new OperationTemplateListResult(
                _templateResponses.Values.OrderBy(x => x.OperationCode, StringComparer.Ordinal).ToArray());
        }
    }

    public OperationTemplateFact GetTemplate(string operationCode)
    {
        lock (_gate)
        {
            return _templateResponses.TryGetValue(operationCode, out var template)
                ? template
                : throw new OperationTemplateNotFoundException(operationCode);
        }
    }

    public PendingOperationTasksResult ClaimPending(ClaimOperationTasksInput request, DateTimeOffset now)
    {
        lock (_gate)
        {
            var cappedTake = Math.Clamp(request.Take, 1, 50);
            RequeueExpiredLeasesUnlocked(request.OrganizationId, request.EnvironmentId, now);
            var pendingTasks = _tasks
                .Where(x => x.OrganizationId == request.OrganizationId
                    && x.EnvironmentId == request.EnvironmentId
                    && string.Equals(x.Status, "queued", StringComparison.Ordinal))
                .OrderBy(x => x.RequestedAtUtc)
                .ThenBy(x => x.OperationTaskId)
                .Take(cappedTake)
                .ToList();

            var items = new List<OperationTaskDispatchFact>();
            foreach (var task in pendingTasks)
            {
                var attemptId = $"attempt-{_attempts.Count + 1:000000}";
                var attemptNo = _attempts.Count(x => x.OperationTaskId == task.OperationTaskId) + 1;
                var maxAttempts = Math.Clamp(task.DefaultMaxAttempts, 1, 10);
                if (attemptNo > maxAttempts)
                {
                    ReplaceTask(task with { Status = "failed" });
                    continue;
                }

                var leaseDuration = TimeSpan.FromSeconds(Math.Clamp(task.DefaultLeaseDurationSeconds, 30, 3600));
                var leasedUntilUtc = now.Add(leaseDuration);
                var leaseId = Guid.NewGuid().ToString("N");
                _attempts.Add(new OperationAttemptFact(
                    attemptId,
                    task.OperationTaskId,
                    request.ConnectorHostId,
                    "started",
                    now,
                    null,
                    null,
                    leaseId,
                    now,
                    leasedUntilUtc,
                    attemptNo,
                    maxAttempts,
                    null));

                ReplaceTask(task with { Status = "dispatched" });
                AddAudit(task.OperationTaskId, "operation.claimed", request.ConnectorHostId, now, task.CorrelationId);

                items.Add(new OperationTaskDispatchFact(
                    task.OperationTaskId,
                    attemptId,
                    task.OrganizationId,
                    task.EnvironmentId,
                    request.ConnectorHostId,
                    task.InstanceKey,
                    task.OperationCode,
                    task.CorrelationId,
                    task.Parameters,
                    leaseId,
                    now,
                    leasedUntilUtc,
                    attemptNo,
                    Math.Max(0, (int)leaseDuration.TotalSeconds),
                    maxAttempts));
            }

            return new PendingOperationTasksResult(items);
        }
    }

    public OperationTaskDetailFact AbandonLease(string operationTaskId, AbandonOperationTaskLeaseInput request, DateTimeOffset now)
    {
        lock (_gate)
        {
            var task = FindTask(operationTaskId);
            var attempt = GetMatchingActiveLease(task, request.LeaseId, request.ConnectorHostId);
            ReplaceAttempt(attempt with
            {
                Status = "abandoned",
                FinishedAtUtc = now,
                AbandonReason = request.AbandonReason
            });
            ReplaceTask(task with { Status = attempt.AttemptNo >= attempt.MaxAttempts ? "failed" : "queued" });
            AddAudit(task.OperationTaskId, "operation.abandoned", request.ConnectorHostId, now, task.CorrelationId);
            return GetUnlocked(operationTaskId);
        }
    }

    public OperationTaskDetailFact HeartbeatLease(string operationTaskId, HeartbeatOperationTaskLeaseInput request, DateTimeOffset now)
    {
        lock (_gate)
        {
            var task = FindTask(operationTaskId);
            var attempt = GetMatchingActiveLease(task, request.LeaseId, request.ConnectorHostId);
            if (attempt.LeasedUntilUtc <= now)
            {
                throw new InvalidOperationResultException("Operation task lease has expired.");
            }

            ReplaceAttempt(attempt with
            {
                LeasedUntilUtc = now.Add(TimeSpan.FromSeconds(Math.Clamp(request.LeaseDurationSeconds, 30, 3600)))
            });
            AddAudit(task.OperationTaskId, "operation.heartbeat", request.ConnectorHostId, now, task.CorrelationId);
            return GetUnlocked(operationTaskId);
        }
    }

    public OperationTaskDetailFact RecordResult(OperationResultInput result)
    {
        lock (_gate)
        {
            var task = FindTask(result.OperationTaskId);
            var attempt = _attempts.SingleOrDefault(x => x.OperationTaskId == result.OperationTaskId && x.AttemptId == result.AttemptId)
                ?? throw new InvalidOperationResultException("Operation result does not match an existing attempt.");

            ValidateResultOwnership(task, attempt, result);
            if (!string.Equals(attempt.Status, "started", StringComparison.Ordinal))
            {
                throw new InvalidOperationResultException("Operation result has already been recorded for this attempt.");
            }

            var completed = string.Equals(result.ExecutionStatus, "succeeded", StringComparison.OrdinalIgnoreCase);
            var status = completed ? "completed" : "failed";
            var auditAction = completed ? "operation.completed" : "operation.failed";

            ReplaceAttempt(attempt with
            {
                Status = status,
                FinishedAtUtc = result.FinishedAtUtc,
                Failure = result.Failure
            });
            ReplaceTask(task with { Status = status });
            AddAudit(task.OperationTaskId, auditAction, result.Context.ConnectorHostId, result.FinishedAtUtc, result.Context.CorrelationId);

            return GetUnlocked(task.OperationTaskId);
        }
    }

    public OperationTaskDetailFact Approve(string operationTaskId, DecideOperationApprovalInput request, DateTimeOffset now)
    {
        lock (_gate)
        {
            var task = FindTask(operationTaskId);
            ValidateApprovalDecision(task, request);
            var approval = task.Approval ?? throw new InvalidOperationTaskRequestException("Operation task does not require approval.");
            if (!string.Equals(task.Status, "approval-pending", StringComparison.Ordinal)
                || !string.Equals(approval.Status, "pending", StringComparison.Ordinal))
            {
                throw new InvalidOperationTaskRequestException("Operation task approval is not pending.");
            }

            ReplaceTask(task with
            {
                Status = "queued",
                Approval = approval with
                {
                    Status = "approved",
                    DecidedBy = request.Actor,
                    DecidedAtUtc = now,
                    DecisionReason = request.DecisionReason
                }
            });
            AddAudit(task.OperationTaskId, "operation.approved", request.Actor, now, request.CorrelationId);
            return GetUnlocked(task.OperationTaskId);
        }
    }

    public OperationTaskDetailFact Reject(string operationTaskId, DecideOperationApprovalInput request, DateTimeOffset now)
    {
        lock (_gate)
        {
            var task = FindTask(operationTaskId);
            ValidateApprovalDecision(task, request);
            var approval = task.Approval ?? throw new InvalidOperationTaskRequestException("Operation task does not require approval.");
            if (!string.Equals(task.Status, "approval-pending", StringComparison.Ordinal)
                || !string.Equals(approval.Status, "pending", StringComparison.Ordinal))
            {
                throw new InvalidOperationTaskRequestException("Operation task approval is not pending.");
            }

            ReplaceTask(task with
            {
                Status = "rejected",
                Approval = approval with
                {
                    Status = "rejected",
                    DecidedBy = request.Actor,
                    DecidedAtUtc = now,
                    DecisionReason = request.DecisionReason
                }
            });
            AddAudit(task.OperationTaskId, "operation.rejected", request.Actor, now, request.CorrelationId);
            return GetUnlocked(task.OperationTaskId);
        }
    }

    private OperationTaskDetailFact GetUnlocked(string operationTaskId)
    {
        var task = FindTask(operationTaskId);
        return new OperationTaskDetailFact(
            task,
            _attempts.Where(x => x.OperationTaskId == task.OperationTaskId).ToList(),
            _auditRecords.Where(x => x.OperationTaskId == task.OperationTaskId).ToList());
    }

    private OperationTaskFact FindTask(string operationTaskId)
    {
        return _tasks.SingleOrDefault(x => x.OperationTaskId == operationTaskId)
            ?? throw new OperationTaskNotFoundException(operationTaskId);
    }

    private static void ValidateResultOwnership(OperationTaskFact task, OperationAttemptFact attempt, OperationResultInput result)
    {
        if (!string.Equals(attempt.ConnectorHostId, result.Context.ConnectorHostId, StringComparison.Ordinal)
            || !string.Equals(task.OrganizationId, result.Context.OrganizationId, StringComparison.Ordinal)
            || !string.Equals(task.EnvironmentId, result.Context.EnvironmentId, StringComparison.Ordinal)
            || !string.Equals(task.InstanceKey, result.InstanceKey, StringComparison.Ordinal)
            || !string.Equals(task.OperationCode, result.OperationCode, StringComparison.Ordinal))
        {
            throw new InvalidOperationResultException("Operation result context does not match the operation task attempt.");
        }
    }

    private static void ValidateAuditIntentScope(OperationTaskFact task, SubmitAuditIntentInput request)
    {
        if (!string.Equals(task.OrganizationId, request.OrganizationId, StringComparison.Ordinal)
            || !string.Equals(task.EnvironmentId, request.EnvironmentId, StringComparison.Ordinal))
        {
            throw new InvalidOperationTaskRequestException("Audit intent context does not match the operation task scope.");
        }
    }

    private static void ValidateApprovalDecision(OperationTaskFact task, DecideOperationApprovalInput request)
    {
        if (!string.Equals(task.OrganizationId, request.OrganizationId, StringComparison.Ordinal)
            || !string.Equals(task.EnvironmentId, request.EnvironmentId, StringComparison.Ordinal))
        {
            throw new InvalidOperationTaskRequestException("Approval decision context does not match the operation task scope.");
        }

        if (string.IsNullOrWhiteSpace(request.Actor))
        {
            throw new InvalidOperationTaskRequestException("Approval decision actor is required.");
        }

        if (IsSameActor(task.RequestedBy, request.Actor))
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

    private void RequeueExpiredLeasesUnlocked(string organizationId, string environmentId, DateTimeOffset now)
    {
        var expiredTasks = _tasks
            .Where(x => x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && string.Equals(x.Status, "dispatched", StringComparison.Ordinal))
            .ToList();

        foreach (var task in expiredTasks)
        {
            var attempt = _attempts
                .Where(x => x.OperationTaskId == task.OperationTaskId
                    && string.Equals(x.Status, "started", StringComparison.Ordinal))
                .OrderByDescending(x => x.AttemptNo)
                .FirstOrDefault();
            if (attempt is null || attempt.LeasedUntilUtc > now)
            {
                continue;
            }

            ReplaceAttempt(attempt with
            {
                Status = "abandoned",
                FinishedAtUtc = now,
                AbandonReason = "lease-timeout"
            });
            ReplaceTask(task with { Status = attempt.AttemptNo >= attempt.MaxAttempts ? "failed" : "queued" });
            AddAudit(task.OperationTaskId, "operation.lease-timeout", attempt.ConnectorHostId, now, task.CorrelationId);
        }
    }

    private OperationAttemptFact GetMatchingActiveLease(OperationTaskFact task, string leaseId, string connectorHostId)
    {
        var attempt = _attempts
            .Where(x => x.OperationTaskId == task.OperationTaskId
                && string.Equals(x.Status, "started", StringComparison.Ordinal))
            .OrderByDescending(x => x.AttemptNo)
            .FirstOrDefault()
            ?? throw new InvalidOperationResultException("Operation task does not have an active lease.");
        if (!string.Equals(attempt.LeaseId, leaseId, StringComparison.Ordinal)
            || !string.Equals(attempt.ConnectorHostId, connectorHostId, StringComparison.Ordinal))
        {
            throw new InvalidOperationResultException("Operation task lease does not match the active attempt.");
        }

        return attempt;
    }

    private static string GetIdempotencyScope(string organizationId, string environmentId, string idempotencyKey)
    {
        return $"{organizationId}\u001f{environmentId}\u001f{idempotencyKey}";
    }

    private AuditRecordFact AddAudit(string operationTaskId, string action, string actor, DateTimeOffset occurredAtUtc, string correlationId)
    {
        var auditRecordId = $"audit-{_auditRecords.Count + 1:000000}";
        var task = FindTask(operationTaskId);
        var head = _auditRecords
            .Where(x =>
            {
                var auditTask = FindTask(x.OperationTaskId);
                return auditTask.OrganizationId == task.OrganizationId
                    && auditTask.EnvironmentId == task.EnvironmentId;
            })
            .OrderByDescending(x => x.SequenceNo)
            .ThenByDescending(x => x.OccurredAtUtc)
            .FirstOrDefault();
        var sequenceNo = (head?.SequenceNo ?? 0) + 1;
        var previousIntegrityHash = head?.IntegrityHash ?? string.Empty;
        var audit = new AuditRecordFact(
            auditRecordId,
            operationTaskId,
            sequenceNo,
            previousIntegrityHash,
            action,
            actor,
            occurredAtUtc,
            correlationId,
            AggregatesModel.OperationTaskAggregate.AuditRecord.ComputeIntegrityHash(
                auditRecordId,
                operationTaskId,
                sequenceNo,
                previousIntegrityHash,
                action,
                actor,
                occurredAtUtc,
                correlationId));
        _auditRecords.Add(audit);
        return audit;
    }

    private void ReplaceTask(OperationTaskFact task)
    {
        _tasks[_tasks.FindIndex(x => x.OperationTaskId == task.OperationTaskId)] = task;
    }

    private void ReplaceAttempt(OperationAttemptFact attempt)
    {
        _attempts[_attempts.FindIndex(x => x.AttemptId == attempt.AttemptId)] = attempt;
    }

    private static OperationTemplateFact BuiltInTemplateResponse()
    {
        var now = DateTimeOffset.Parse("2026-05-21T00:00:00Z");
        return new OperationTemplateFact(
            "opt-lifecycle-restart",
            "lifecycle.restart",
            "Lifecycle restart",
            "{}",
            "low",
            3,
            300,
            RequiresApproval: false,
            Enabled: true,
            now,
            now);
    }
}
