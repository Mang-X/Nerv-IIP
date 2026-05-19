using Nerv.IIP.Contracts.ConnectorProtocol;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Ops.Domain;
using Nerv.IIP.Ops.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Ops.Infrastructure.Repositories;

namespace Nerv.IIP.Ops.Web.Application.Commands;

public interface IOperationTaskApplicationService
{
    Task<OperationTaskResponse> CreateAsync(CreateOperationTaskRequest request, DateTimeOffset now, CancellationToken cancellationToken);
    Task<OperationTaskResponse> GetAsync(string operationTaskId, CancellationToken cancellationToken);
    Task<PendingOperationTasksResponse> ClaimPendingAsync(ClaimOperationTasksRequest request, DateTimeOffset now, CancellationToken cancellationToken);
    Task<OperationTaskResponse> AbandonLeaseAsync(string operationTaskId, AbandonOperationTaskLeaseRequest request, DateTimeOffset now, CancellationToken cancellationToken);
    Task<OperationTaskResponse> HeartbeatLeaseAsync(string operationTaskId, HeartbeatOperationTaskLeaseRequest request, DateTimeOffset now, CancellationToken cancellationToken);
    Task<OperationTaskResponse> RecordResultAsync(OperationResult result, CancellationToken cancellationToken);
}

public sealed class InMemoryOperationTaskApplicationService(IOpsStateStore store) : IOperationTaskApplicationService
{
    public Task<OperationTaskResponse> CreateAsync(CreateOperationTaskRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        return Task.FromResult(store.Create(request, now));
    }

    public Task<OperationTaskResponse> GetAsync(string operationTaskId, CancellationToken cancellationToken)
    {
        return Task.FromResult(store.Get(operationTaskId));
    }

    public Task<PendingOperationTasksResponse> ClaimPendingAsync(ClaimOperationTasksRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        return Task.FromResult(store.ClaimPending(request, now));
    }

    public Task<OperationTaskResponse> AbandonLeaseAsync(string operationTaskId, AbandonOperationTaskLeaseRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        return Task.FromResult(store.AbandonLease(operationTaskId, request, now));
    }

    public Task<OperationTaskResponse> HeartbeatLeaseAsync(string operationTaskId, HeartbeatOperationTaskLeaseRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        return Task.FromResult(store.HeartbeatLease(operationTaskId, request, now));
    }

    public Task<OperationTaskResponse> RecordResultAsync(OperationResult result, CancellationToken cancellationToken)
    {
        return Task.FromResult(store.RecordResult(result));
    }
}

public sealed class EfOperationTaskApplicationService(IOperationTaskRepository repository) : IOperationTaskApplicationService
{
    public async Task<OperationTaskResponse> CreateAsync(CreateOperationTaskRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var idempotencyScope = OperationTask.GetIdempotencyScope(request.OrganizationId, request.EnvironmentId, request.IdempotencyKey);
        var existing = await repository.GetByIdempotencyScopeAsync(idempotencyScope, cancellationToken);
        if (existing is not null)
        {
            return existing.ToResponse();
        }

        var task = OperationTask.Create(await repository.NextTaskIdAsync(cancellationToken), request, now);
        task.AssignInitialAuditId(await repository.NextAuditRecordIdAsync(cancellationToken));
        await repository.AddAsync(task, cancellationToken);
        return task.ToResponse();
    }

    public async Task<OperationTaskResponse> GetAsync(string operationTaskId, CancellationToken cancellationToken)
    {
        var task = await repository.GetByIdAsync(operationTaskId, cancellationToken)
            ?? throw new OperationTaskNotFoundException(operationTaskId);
        return task.ToResponse();
    }

    public async Task<PendingOperationTasksResponse> ClaimPendingAsync(ClaimOperationTasksRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var pendingTasks = await repository.GetClaimableAsync(
            request.OrganizationId,
            request.EnvironmentId,
            Math.Clamp(request.Take, 1, 50),
            now,
            cancellationToken);
        var items = new List<OperationTaskDispatchItem>();
        var leaseDuration = TimeSpan.FromSeconds(Math.Clamp(request.LeaseDurationSeconds, 30, 3600));
        var maxAttempts = Math.Clamp(request.MaxAttempts, 1, 10);

        foreach (var task in pendingTasks)
        {
            task.AbandonExpiredLease(await repository.NextAuditRecordIdAsync(cancellationToken), now);
            if (!string.Equals(task.Status, "queued", StringComparison.Ordinal))
            {
                continue;
            }

            items.Add(task.Claim(
                await repository.NextAttemptIdAsync(cancellationToken),
                await repository.NextAuditRecordIdAsync(cancellationToken),
                Guid.NewGuid().ToString("N"),
                request.ConnectorHostId,
                now,
                leaseDuration,
                maxAttempts));
        }

        return new PendingOperationTasksResponse(items);
    }

    public async Task<OperationTaskResponse> AbandonLeaseAsync(string operationTaskId, AbandonOperationTaskLeaseRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var task = await repository.GetByIdAsync(operationTaskId, cancellationToken)
            ?? throw new OperationTaskNotFoundException(operationTaskId);
        return task.AbandonLease(
            request.LeaseId,
            request.ConnectorHostId,
            request.AbandonReason,
            await repository.NextAuditRecordIdAsync(cancellationToken),
            now);
    }

    public async Task<OperationTaskResponse> HeartbeatLeaseAsync(string operationTaskId, HeartbeatOperationTaskLeaseRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var task = await repository.GetByIdAsync(operationTaskId, cancellationToken)
            ?? throw new OperationTaskNotFoundException(operationTaskId);
        return task.HeartbeatLease(
            request.LeaseId,
            request.ConnectorHostId,
            now,
            TimeSpan.FromSeconds(Math.Clamp(request.LeaseDurationSeconds, 30, 3600)),
            await repository.NextAuditRecordIdAsync(cancellationToken));
    }

    public async Task<OperationTaskResponse> RecordResultAsync(OperationResult result, CancellationToken cancellationToken)
    {
        var task = await repository.GetByIdAsync(result.OperationTaskId, cancellationToken)
            ?? throw new OperationTaskNotFoundException(result.OperationTaskId);
        var auditId = await repository.NextAuditRecordIdAsync(cancellationToken);
        return task.RecordResult(result, auditId);
    }
}
