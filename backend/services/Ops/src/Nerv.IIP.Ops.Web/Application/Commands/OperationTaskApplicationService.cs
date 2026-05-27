using Nerv.IIP.Contracts.ConnectorProtocol;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Ops.Domain;
using Nerv.IIP.Ops.Domain.AggregatesModel.OperationTemplateAggregate;
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
    Task<AuditIntentResponse> SubmitAuditIntentAsync(SubmitAuditIntentRequest request, DateTimeOffset now, CancellationToken cancellationToken);
    Task<OperationTaskResponse> ApproveAsync(string operationTaskId, DecideOperationApprovalRequest request, DateTimeOffset now, CancellationToken cancellationToken);
    Task<OperationTaskResponse> RejectAsync(string operationTaskId, DecideOperationApprovalRequest request, DateTimeOffset now, CancellationToken cancellationToken);
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

    public Task<AuditIntentResponse> SubmitAuditIntentAsync(SubmitAuditIntentRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        return Task.FromResult(store.SubmitAuditIntent(request, now));
    }

    public Task<OperationTaskResponse> ApproveAsync(string operationTaskId, DecideOperationApprovalRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        return Task.FromResult(store.Approve(operationTaskId, request, now));
    }

    public Task<OperationTaskResponse> RejectAsync(string operationTaskId, DecideOperationApprovalRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        return Task.FromResult(store.Reject(operationTaskId, request, now));
    }
}

public sealed class EfOperationTaskApplicationService(
    IOperationTaskRepository repository,
    IOperationTemplateRepository operationTemplateRepository) : IOperationTaskApplicationService
{
    public async Task<OperationTaskResponse> CreateAsync(CreateOperationTaskRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var idempotencyScope = OperationTask.GetIdempotencyScope(request.OrganizationId, request.EnvironmentId, request.IdempotencyKey);
        var existing = await repository.GetByIdempotencyScopeAsync(idempotencyScope, cancellationToken);
        if (existing is not null)
        {
            return existing.ToResponse();
        }

        var template = await ResolveTemplateAsync(request.OperationCode, cancellationToken);
        var task = OperationTask.Create(await repository.NextTaskIdAsync(cancellationToken), request, template, now);
        var pendingAuditIds = new List<AuditRecordId>();
        foreach (var _ in task.AuditRecords.Where(x => string.IsNullOrWhiteSpace(x.Id.Id)))
        {
            pendingAuditIds.Add(await repository.NextAuditRecordIdAsync(cancellationToken));
        }

        task.AssignPendingAuditIds(pendingAuditIds);
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
                TimeSpan.FromSeconds(Math.Clamp(task.DefaultLeaseDurationSeconds, 30, 3600)),
                Math.Clamp(task.DefaultMaxAttempts, 1, 10)));
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

    public async Task<AuditIntentResponse> SubmitAuditIntentAsync(SubmitAuditIntentRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var task = await repository.GetByIdAsync(request.OperationTaskId, cancellationToken)
            ?? throw new OperationTaskNotFoundException(request.OperationTaskId);
        return task.SubmitAuditIntent(
            request,
            await repository.NextAuditRecordIdAsync(cancellationToken),
            now);
    }

    public async Task<OperationTaskResponse> ApproveAsync(string operationTaskId, DecideOperationApprovalRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var task = await repository.GetByIdAsync(operationTaskId, cancellationToken)
            ?? throw new OperationTaskNotFoundException(operationTaskId);
        return task.Approve(
            request,
            await repository.NextAuditRecordIdAsync(cancellationToken),
            now);
    }

    public async Task<OperationTaskResponse> RejectAsync(string operationTaskId, DecideOperationApprovalRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var task = await repository.GetByIdAsync(operationTaskId, cancellationToken)
            ?? throw new OperationTaskNotFoundException(operationTaskId);
        return task.Reject(
            request,
            await repository.NextAuditRecordIdAsync(cancellationToken),
            now);
    }

    private async Task<OperationTemplateSnapshot> ResolveTemplateAsync(string operationCode, CancellationToken cancellationToken)
    {
        var template = await operationTemplateRepository.GetByOperationCodeAsync(operationCode, cancellationToken);
        if (template is not null)
        {
            return template.ToSnapshot();
        }

        if (string.Equals(operationCode, BuiltInOperationTemplates.LifecycleRestart.OperationCode, StringComparison.Ordinal))
        {
            return BuiltInOperationTemplates.LifecycleRestart;
        }

        throw new InvalidOperationTaskRequestException($"Unsupported operation code: {operationCode}");
    }
}

internal static class BuiltInOperationTemplates
{
    public static readonly OperationTemplateSnapshot LifecycleRestart = new(
        "lifecycle.restart",
        Enabled: true,
        DefaultMaxAttempts: 3,
        DefaultLeaseDurationSeconds: 300,
        RequiresApproval: false);
}
