using Nerv.IIP.Contracts.ConnectorProtocol;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Ops.Domain;
using Nerv.IIP.Ops.Domain.AggregatesModel.OperationTemplateAggregate;
using Nerv.IIP.Ops.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Ops.Infrastructure.Repositories;
using Nerv.IIP.Ops.Web.Application;

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
        return Task.FromResult(store.Create(request.ToDomainInput(), now).ToContract());
    }

    public Task<OperationTaskResponse> GetAsync(string operationTaskId, CancellationToken cancellationToken)
    {
        return Task.FromResult(store.Get(operationTaskId).ToContract());
    }

    public Task<PendingOperationTasksResponse> ClaimPendingAsync(ClaimOperationTasksRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        return Task.FromResult(store.ClaimPending(request.ToDomainInput(), now).ToContract());
    }

    public Task<OperationTaskResponse> AbandonLeaseAsync(string operationTaskId, AbandonOperationTaskLeaseRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        return Task.FromResult(store.AbandonLease(operationTaskId, request.ToDomainInput(), now).ToContract());
    }

    public Task<OperationTaskResponse> HeartbeatLeaseAsync(string operationTaskId, HeartbeatOperationTaskLeaseRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        return Task.FromResult(store.HeartbeatLease(operationTaskId, request.ToDomainInput(), now).ToContract());
    }

    public Task<OperationTaskResponse> RecordResultAsync(OperationResult result, CancellationToken cancellationToken)
    {
        return Task.FromResult(store.RecordResult(result.ToDomainInput()).ToContract());
    }

    public Task<AuditIntentResponse> SubmitAuditIntentAsync(SubmitAuditIntentRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        return Task.FromResult(store.SubmitAuditIntent(request.ToDomainInput(), now).ToContract());
    }

    public Task<OperationTaskResponse> ApproveAsync(string operationTaskId, DecideOperationApprovalRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        return Task.FromResult(store.Approve(operationTaskId, request.ToDomainInput(), now).ToContract());
    }

    public Task<OperationTaskResponse> RejectAsync(string operationTaskId, DecideOperationApprovalRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        return Task.FromResult(store.Reject(operationTaskId, request.ToDomainInput(), now).ToContract());
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
            return existing.ToDetailFact().ToContract();
        }

        var template = await ResolveTemplateAsync(request.OperationCode, cancellationToken);
        var task = OperationTask.Create(await repository.NextTaskIdAsync(cancellationToken), request.ToDomainInput(), template, now);
        var pendingAuditIds = new List<AuditRecordId>();
        foreach (var _ in task.AuditRecords.Where(x => string.IsNullOrWhiteSpace(x.Id.Id)))
        {
            pendingAuditIds.Add(await repository.NextAuditRecordIdAsync(cancellationToken));
        }

        task.AssignPendingAuditIds(pendingAuditIds);
        await AssignAuditChainAsync(task, pendingAuditIds, cancellationToken);
        await repository.AddAsync(task, cancellationToken);
        return task.ToDetailFact().ToContract();
    }

    public async Task<OperationTaskResponse> GetAsync(string operationTaskId, CancellationToken cancellationToken)
    {
        var task = await repository.GetByIdAsync(operationTaskId, cancellationToken)
            ?? throw new OperationTaskNotFoundException(operationTaskId);
        return task.ToDetailFact().ToContract();
    }

    public async Task<PendingOperationTasksResponse> ClaimPendingAsync(ClaimOperationTasksRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var pendingTasks = await repository.GetClaimableAsync(
            request.OrganizationId,
            request.EnvironmentId,
            Math.Clamp(request.Take, 1, 50),
            now,
            cancellationToken);
        var items = new List<OperationTaskDispatchFact>();
        await repository.LockAuditChainAsync(request.OrganizationId, request.EnvironmentId, cancellationToken);
        var chainHead = await repository.GetAuditChainHeadAsync(request.OrganizationId, request.EnvironmentId, cancellationToken);
        foreach (var task in pendingTasks)
        {
            var timeoutAuditId = await repository.NextAuditRecordIdAsync(cancellationToken);
            var auditCountBeforeTimeout = task.AuditRecords.Count;
            task.AbandonExpiredLease(timeoutAuditId, now);
            if (task.AuditRecords.Count > auditCountBeforeTimeout)
            {
                chainHead = AssignAuditChain(task, [timeoutAuditId], chainHead);
            }

            if (!string.Equals(task.Status, "queued", StringComparison.Ordinal))
            {
                continue;
            }

            var claimAuditId = await repository.NextAuditRecordIdAsync(cancellationToken);
            items.Add(task.Claim(
                await repository.NextAttemptIdAsync(cancellationToken),
                claimAuditId,
                Guid.NewGuid().ToString("N"),
                request.ConnectorHostId,
                now,
                TimeSpan.FromSeconds(Math.Clamp(task.DefaultLeaseDurationSeconds, 30, 3600)),
                Math.Clamp(task.DefaultMaxAttempts, 1, 10)));
            chainHead = AssignAuditChain(task, [claimAuditId], chainHead);
        }

        return new PendingOperationTasksResult(items).ToContract();
    }

    public async Task<OperationTaskResponse> AbandonLeaseAsync(string operationTaskId, AbandonOperationTaskLeaseRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var task = await repository.GetByIdAsync(operationTaskId, cancellationToken)
            ?? throw new OperationTaskNotFoundException(operationTaskId);
        var auditId = await repository.NextAuditRecordIdAsync(cancellationToken);
        task.AbandonLease(
            request.LeaseId,
            request.ConnectorHostId,
            request.AbandonReason,
            auditId,
            now);
        await AssignAuditChainAsync(task, [auditId], cancellationToken);
        return task.ToDetailFact().ToContract();
    }

    public async Task<OperationTaskResponse> HeartbeatLeaseAsync(string operationTaskId, HeartbeatOperationTaskLeaseRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var task = await repository.GetByIdAsync(operationTaskId, cancellationToken)
            ?? throw new OperationTaskNotFoundException(operationTaskId);
        var auditId = await repository.NextAuditRecordIdAsync(cancellationToken);
        task.HeartbeatLease(
            request.LeaseId,
            request.ConnectorHostId,
            now,
            TimeSpan.FromSeconds(Math.Clamp(request.LeaseDurationSeconds, 30, 3600)),
            auditId);
        await AssignAuditChainAsync(task, [auditId], cancellationToken);
        return task.ToDetailFact().ToContract();
    }

    public async Task<OperationTaskResponse> RecordResultAsync(OperationResult result, CancellationToken cancellationToken)
    {
        var task = await repository.GetByIdAsync(result.OperationTaskId, cancellationToken)
            ?? throw new OperationTaskNotFoundException(result.OperationTaskId);
        var auditId = await repository.NextAuditRecordIdAsync(cancellationToken);
        task.RecordResult(result.ToDomainInput(), auditId);
        await AssignAuditChainAsync(task, [auditId], cancellationToken);
        return task.ToDetailFact().ToContract();
    }

    public async Task<AuditIntentResponse> SubmitAuditIntentAsync(SubmitAuditIntentRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var task = await repository.GetByIdAsync(request.OperationTaskId, cancellationToken)
            ?? throw new OperationTaskNotFoundException(request.OperationTaskId);
        var auditId = await repository.NextAuditRecordIdAsync(cancellationToken);
        var result = task.SubmitAuditIntent(
            request.ToDomainInput(),
            auditId,
            now);
        await AssignAuditChainAsync(task, [auditId], cancellationToken);
        var audit = task.AuditRecords.Single(x => x.Id == auditId);
        return new AuditIntentResponse(
            audit.Id.Id,
            audit.OperationTaskId.Id,
            audit.SequenceNo,
            audit.PreviousIntegrityHash,
            result.Action,
            result.Actor,
            result.OccurredAtUtc,
            result.CorrelationId,
            audit.IntegrityHash);
    }

    public async Task<OperationTaskResponse> ApproveAsync(string operationTaskId, DecideOperationApprovalRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var task = await repository.GetByIdAsync(operationTaskId, cancellationToken)
            ?? throw new OperationTaskNotFoundException(operationTaskId);
        var auditId = await repository.NextAuditRecordIdAsync(cancellationToken);
        task.Approve(
            request.ToDomainInput(),
            auditId,
            now);
        await AssignAuditChainAsync(task, [auditId], cancellationToken);
        return task.ToDetailFact().ToContract();
    }

    public async Task<OperationTaskResponse> RejectAsync(string operationTaskId, DecideOperationApprovalRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var task = await repository.GetByIdAsync(operationTaskId, cancellationToken)
            ?? throw new OperationTaskNotFoundException(operationTaskId);
        var auditId = await repository.NextAuditRecordIdAsync(cancellationToken);
        task.Reject(
            request.ToDomainInput(),
            auditId,
            now);
        await AssignAuditChainAsync(task, [auditId], cancellationToken);
        return task.ToDetailFact().ToContract();
    }

    private async Task AssignAuditChainAsync(OperationTask task, IReadOnlyCollection<AuditRecordId> auditRecordIds, CancellationToken cancellationToken)
    {
        if (auditRecordIds.Count == 0)
        {
            return;
        }

        await repository.LockAuditChainAsync(task.OrganizationId, task.EnvironmentId, cancellationToken);
        var head = await repository.GetAuditChainHeadAsync(task.OrganizationId, task.EnvironmentId, cancellationToken);
        var sequenceNo = head?.SequenceNo ?? 0;
        var previousHash = head?.IntegrityHash ?? string.Empty;

        foreach (var auditRecordId in auditRecordIds)
        {
            sequenceNo++;
            task.AssignAuditChainStamp(auditRecordId, sequenceNo, previousHash);
            previousHash = task.AuditRecords.Single(x => x.Id == auditRecordId).IntegrityHash;
        }
    }

    private static AuditChainHead? AssignAuditChain(OperationTask task, IReadOnlyCollection<AuditRecordId> auditRecordIds, AuditChainHead? head)
    {
        if (auditRecordIds.Count == 0)
        {
            return head;
        }

        var sequenceNo = head?.SequenceNo ?? 0;
        var previousHash = head?.IntegrityHash ?? string.Empty;

        foreach (var auditRecordId in auditRecordIds)
        {
            sequenceNo++;
            task.AssignAuditChainStamp(auditRecordId, sequenceNo, previousHash);
            previousHash = task.AuditRecords.Single(x => x.Id == auditRecordId).IntegrityHash;
        }

        return new AuditChainHead(sequenceNo, previousHash);
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
