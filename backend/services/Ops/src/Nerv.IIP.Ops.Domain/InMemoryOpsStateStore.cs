using Nerv.IIP.Contracts.ConnectorProtocol;
using Nerv.IIP.Contracts.Ops;

namespace Nerv.IIP.Ops.Domain;

public abstract class OpsStateException(string message) : Exception(message);
public sealed class OperationTaskNotFoundException(string operationTaskId)
    : OpsStateException($"Operation task was not found: {operationTaskId}");
public sealed class InvalidOperationResultException(string message) : OpsStateException(message);
public sealed class InvalidOperationTaskRequestException(string message) : OpsStateException(message);

public interface IOpsStateStore
{
    OperationTaskResponse Create(CreateOperationTaskRequest request, DateTimeOffset now);
    OperationTaskResponse Get(string operationTaskId);
    PendingOperationTasksResponse ClaimPending(ClaimOperationTasksRequest request, DateTimeOffset now);
    OperationTaskResponse AbandonLease(string operationTaskId, AbandonOperationTaskLeaseRequest request, DateTimeOffset now);
    OperationTaskResponse HeartbeatLease(string operationTaskId, HeartbeatOperationTaskLeaseRequest request, DateTimeOffset now);
    OperationTaskResponse RecordResult(OperationResult result);
}

public sealed class InMemoryOpsStateStore : IOpsStateStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _idempotency = new(StringComparer.Ordinal);
    private readonly List<OperationTaskFact> _tasks = [];
    private readonly List<OperationAttemptFact> _attempts = [];
    private readonly List<AuditRecordFact> _auditRecords = [];

    public OperationTaskResponse Create(CreateOperationTaskRequest request, DateTimeOffset now)
    {
        lock (_gate)
        {
            var idempotencyScope = GetIdempotencyScope(request.OrganizationId, request.EnvironmentId, request.IdempotencyKey);
            if (_idempotency.TryGetValue(idempotencyScope, out var existingTaskId))
            {
                return GetUnlocked(existingTaskId);
            }

            if (!string.Equals(request.OperationCode, "lifecycle.restart", StringComparison.Ordinal))
            {
                throw new InvalidOperationTaskRequestException($"Unsupported operation code: {request.OperationCode}");
            }

            var taskId = $"op-{_tasks.Count + 1:000000}";
            var task = new OperationTaskFact(
                taskId,
                request.OrganizationId,
                request.EnvironmentId,
                request.InstanceKey,
                request.OperationCode,
                "queued",
                request.RequestedBy,
                now,
                request.IdempotencyKey,
                request.CorrelationId,
                new Dictionary<string, string>(request.Parameters, StringComparer.Ordinal));

            _tasks.Add(task);
            _idempotency[idempotencyScope] = taskId;
            AddAudit(taskId, "operation.requested", request.RequestedBy, now, request.CorrelationId);
            return GetUnlocked(taskId);
        }
    }

    public OperationTaskResponse Get(string operationTaskId)
    {
        lock (_gate)
        {
            return GetUnlocked(operationTaskId);
        }
    }

    public PendingOperationTasksResponse ClaimPending(ClaimOperationTasksRequest request, DateTimeOffset now)
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

            var items = new List<OperationTaskDispatchItem>();
            foreach (var task in pendingTasks)
            {
                var attemptId = $"attempt-{_attempts.Count + 1:000000}";
                var attemptNo = _attempts.Count(x => x.OperationTaskId == task.OperationTaskId) + 1;
                var maxAttempts = Math.Clamp(request.MaxAttempts, 1, 10);
                if (attemptNo > maxAttempts)
                {
                    ReplaceTask(task with { Status = "failed" });
                    continue;
                }

                var leaseDuration = TimeSpan.FromSeconds(Math.Clamp(request.LeaseDurationSeconds, 30, 3600));
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

                items.Add(new OperationTaskDispatchItem(
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
                    maxAttempts));
            }

            return new PendingOperationTasksResponse(items);
        }
    }

    public OperationTaskResponse AbandonLease(string operationTaskId, AbandonOperationTaskLeaseRequest request, DateTimeOffset now)
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

    public OperationTaskResponse HeartbeatLease(string operationTaskId, HeartbeatOperationTaskLeaseRequest request, DateTimeOffset now)
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

    public OperationTaskResponse RecordResult(OperationResult result)
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

    private OperationTaskResponse GetUnlocked(string operationTaskId)
    {
        var task = FindTask(operationTaskId);
        return OperationTaskMapper.ToResponse(
            task,
            _attempts.Where(x => x.OperationTaskId == task.OperationTaskId),
            _auditRecords.Where(x => x.OperationTaskId == task.OperationTaskId));
    }

    private OperationTaskFact FindTask(string operationTaskId)
    {
        return _tasks.SingleOrDefault(x => x.OperationTaskId == operationTaskId)
            ?? throw new OperationTaskNotFoundException(operationTaskId);
    }

    private static void ValidateResultOwnership(OperationTaskFact task, OperationAttemptFact attempt, OperationResult result)
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

    private void AddAudit(string operationTaskId, string action, string actor, DateTimeOffset occurredAtUtc, string correlationId)
    {
        _auditRecords.Add(new AuditRecordFact(
            $"audit-{_auditRecords.Count + 1:000000}",
            operationTaskId,
            action,
            actor,
            occurredAtUtc,
            correlationId));
    }

    private void ReplaceTask(OperationTaskFact task)
    {
        _tasks[_tasks.FindIndex(x => x.OperationTaskId == task.OperationTaskId)] = task;
    }

    private void ReplaceAttempt(OperationAttemptFact attempt)
    {
        _attempts[_attempts.FindIndex(x => x.AttemptId == attempt.AttemptId)] = attempt;
    }
}
