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
    Task<PendingOperationTasksResponse> DispatchPendingAsync(string organizationId, string environmentId, string connectorHostId, int take, DateTimeOffset now, CancellationToken cancellationToken);
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

    public Task<PendingOperationTasksResponse> DispatchPendingAsync(string organizationId, string environmentId, string connectorHostId, int take, DateTimeOffset now, CancellationToken cancellationToken)
    {
        return Task.FromResult(store.DispatchPending(organizationId, environmentId, connectorHostId, take, now));
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

    public async Task<PendingOperationTasksResponse> DispatchPendingAsync(string organizationId, string environmentId, string connectorHostId, int take, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var pendingTasks = await repository.GetPendingAsync(organizationId, environmentId, take, cancellationToken);
        var attemptCount = await repository.CountAttemptsAsync(cancellationToken);
        var auditCount = await repository.CountAuditRecordsAsync(cancellationToken);
        var items = new List<OperationTaskDispatchItem>();

        foreach (var task in pendingTasks)
        {
            attemptCount++;
            auditCount++;
            items.Add(task.Dispatch(
                new OperationAttemptId($"attempt-{attemptCount:000000}"),
                new AuditRecordId($"audit-{auditCount:000000}"),
                connectorHostId,
                now));
        }

        return new PendingOperationTasksResponse(items);
    }

    public async Task<OperationTaskResponse> RecordResultAsync(OperationResult result, CancellationToken cancellationToken)
    {
        var task = await repository.GetByIdAsync(result.OperationTaskId, cancellationToken)
            ?? throw new OperationTaskNotFoundException(result.OperationTaskId);
        var auditId = await repository.NextAuditRecordIdAsync(cancellationToken);
        return task.RecordResult(result, auditId);
    }
}
