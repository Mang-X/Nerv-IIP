using Nerv.IIP.Contracts.ConnectorProtocol;
using Nerv.IIP.Contracts.Ops;

namespace Nerv.IIP.Ops.Domain;

public abstract class OpsStateException(string message) : Exception(message);
public sealed class OperationTaskNotFoundException(string operationTaskId)
    : OpsStateException($"Operation task was not found: {operationTaskId}");
public sealed class InvalidOperationResultException(string message) : OpsStateException(message);
public sealed class InvalidOperationTaskRequestException(string message) : OpsStateException(message);

public sealed class InMemoryOpsStateStore
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

    public PendingOperationTasksResponse DispatchPending(
        string organizationId,
        string environmentId,
        string connectorHostId,
        int take,
        DateTimeOffset now)
    {
        lock (_gate)
        {
            var cappedTake = Math.Clamp(take, 1, 50);
            var pendingTasks = _tasks
                .Where(x => x.OrganizationId == organizationId
                    && x.EnvironmentId == environmentId
                    && string.Equals(x.Status, "queued", StringComparison.Ordinal))
                .OrderBy(x => x.RequestedAtUtc)
                .ThenBy(x => x.OperationTaskId)
                .Take(cappedTake)
                .ToList();

            var items = new List<OperationTaskDispatchItem>();
            foreach (var task in pendingTasks)
            {
                var attemptId = $"attempt-{_attempts.Count + 1:000000}";
                _attempts.Add(new OperationAttemptFact(
                    attemptId,
                    task.OperationTaskId,
                    connectorHostId,
                    "started",
                    now,
                    null,
                    null));

                ReplaceTask(task with { Status = "dispatched" });
                AddAudit(task.OperationTaskId, "operation.dispatched", connectorHostId, now, task.CorrelationId);

                items.Add(new OperationTaskDispatchItem(
                    task.OperationTaskId,
                    attemptId,
                    task.OrganizationId,
                    task.EnvironmentId,
                    connectorHostId,
                    task.InstanceKey,
                    task.OperationCode,
                    task.CorrelationId,
                    task.Parameters));
            }

            return new PendingOperationTasksResponse(items);
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
