using Nerv.IIP.Contracts.ConnectorProtocol;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Ops.Domain;
using Nerv.IIP.Ops.Domain.AggregatesModel.OperationTemplateAggregate;
using Nerv.IIP.Ops.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Ops.Infrastructure;
using Nerv.IIP.Ops.Infrastructure.Repositories;
using Nerv.IIP.Ops.Web.Application;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Messaging.CAP;
using System.Diagnostics;

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

public sealed record OperationLeaseReaperResult(int RequeuedCount, int FailedCount);

public interface IOperationLeaseReaper
{
    Task<OperationLeaseReaperResult> ReapExpiredLeasesAsync(
        string organizationId,
        string environmentId,
        DateTimeOffset now,
        int take,
        CancellationToken cancellationToken);
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
    IOperationTemplateRepository operationTemplateRepository,
    ApplicationDbContext dbContext) : IOperationTaskApplicationService
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
        const string duplicateRecoverySavepoint = "ops_operation_task_create_before_save";
        // Use EF's native current transaction here. The CAP unit-of-work wrapper does not expose savepoints,
        // while EF's relational transaction does and can recover from a duplicate unique-conflict inside an outer transaction.
        var transaction = dbContext.Database.CurrentTransaction;
        if (transaction is not null)
        {
            Debug.Assert(transaction.SupportsSavepoints, "Ops duplicate recovery requires a transaction that supports savepoints.");
            await transaction.CreateSavepointAsync(duplicateRecoverySavepoint, cancellationToken);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.ReleaseSavepointAsync(duplicateRecoverySavepoint, cancellationToken);
            }
        }
        catch (DbUpdateException exception)
        {
            if (!IsDuplicateTaskConflict(exception))
            {
                throw;
            }

            if (transaction is not null)
            {
                await transaction.RollbackToSavepointAsync(duplicateRecoverySavepoint, cancellationToken);
            }

            dbContext.ChangeTracker.Clear();
            var duplicate = await repository.GetByIdempotencyScopeAsync(idempotencyScope, cancellationToken)
                ?? RethrowDuplicateConflict(exception);
            return duplicate.ToDetailFact().ToContract();
        }

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

        var builtIn = BuiltInOperationTemplates.Find(operationCode);
        if (builtIn is not null)
        {
            return builtIn;
        }

        throw new InvalidOperationTaskRequestException($"Unsupported operation code: {operationCode}");
    }

    private bool IsDuplicateTaskConflict(DbUpdateException exception)
    {
        return dbContext.ChangeTracker.Entries<OperationTask>().Any(x => x.State == EntityState.Added)
            && ProcessedIntegrationEventInbox.IsUniqueConflict(exception, dbContext, constraintOrIndexName: null);
    }

    private static OperationTask RethrowDuplicateConflict(DbUpdateException exception)
    {
        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();
        throw new InvalidOperationException("Unreachable duplicate conflict rethrow path.");
    }
}

public sealed class EfOperationLeaseReaper(
    IOperationTaskRepository repository,
    ApplicationDbContext dbContext) : IOperationLeaseReaper
{
    public async Task<OperationLeaseReaperResult> ReapExpiredLeasesAsync(
        string organizationId,
        string environmentId,
        DateTimeOffset now,
        int take,
        CancellationToken cancellationToken)
    {
        var expiredTasks = await repository.GetExpiredLeasesAsync(
            organizationId,
            environmentId,
            Math.Clamp(take, 1, 100),
            now,
            cancellationToken);
        if (expiredTasks.Count == 0)
        {
            return new OperationLeaseReaperResult(0, 0);
        }

        await repository.LockAuditChainAsync(organizationId, environmentId, cancellationToken);
        var chainHead = await repository.GetAuditChainHeadAsync(organizationId, environmentId, cancellationToken);
        var requeued = 0;
        var failed = 0;
        foreach (var task in expiredTasks)
        {
            var beforeStatus = task.Status;
            var auditId = await repository.NextAuditRecordIdAsync(cancellationToken);
            var auditCount = task.AuditRecords.Count;
            task.AbandonExpiredLease(auditId, now);
            if (task.AuditRecords.Count == auditCount)
            {
                continue;
            }

            chainHead = AssignAuditChainStamp(task, [auditId], chainHead);
            if (string.Equals(task.Status, "queued", StringComparison.Ordinal))
            {
                requeued++;
            }
            else if (!string.Equals(task.Status, beforeStatus, StringComparison.Ordinal))
            {
                failed++;
            }
        }

        // Background reaping is not mediated by CommandUnitOfWorkBehavior, so this save intentionally
        // persists state only. Domain event publication for lease-timeout transitions is a future contract decision.
        await dbContext.SaveChangesAsync(cancellationToken);
        return new OperationLeaseReaperResult(requeued, failed);
    }

    private static AuditChainHead? AssignAuditChainStamp(OperationTask task, IReadOnlyCollection<AuditRecordId> auditRecordIds, AuditChainHead? head)
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
}

internal static class BuiltInOperationTemplates
{
    public static readonly IReadOnlyList<OperationTemplateResponse> Responses =
        BuiltInOperationTemplateCatalog.Definitions.Select(ToResponse).ToArray();

    public static OperationTemplateSnapshot? Find(string operationCode)
    {
        return BuiltInOperationTemplateCatalog.Find(operationCode)?.ToSnapshot();
    }

    private static OperationTemplateResponse ToResponse(BuiltInOperationTemplateDefinition definition)
    {
        return new OperationTemplateResponse(
            definition.OperationTemplateId,
            definition.OperationCode,
            definition.DisplayName,
            definition.ParameterSchemaJson,
            definition.RiskLevel,
            definition.DefaultMaxAttempts,
            definition.DefaultLeaseDurationSeconds,
            definition.RequiresApproval,
            definition.Enabled,
            BuiltInOperationTemplateCatalog.MetadataTimestampUtc,
            BuiltInOperationTemplateCatalog.MetadataTimestampUtc);
    }
}
