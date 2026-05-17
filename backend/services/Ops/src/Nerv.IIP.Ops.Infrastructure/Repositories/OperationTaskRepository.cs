using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Ops.Domain.AggregatesModel.OperationTaskAggregate;
using NetCorePal.Extensions.Repository;
using NetCorePal.Extensions.Repository.EntityFrameworkCore;

namespace Nerv.IIP.Ops.Infrastructure.Repositories;

public interface IOperationTaskRepository : IRepository<OperationTask, OperationTaskId>
{
    Task<OperationTask?> GetByIdAsync(string operationTaskId, CancellationToken cancellationToken = default);
    Task<OperationTask?> GetByIdempotencyScopeAsync(string idempotencyScope, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OperationTask>> GetPendingAsync(string organizationId, string environmentId, int take, CancellationToken cancellationToken = default);
    Task<OperationTaskId> NextTaskIdAsync(CancellationToken cancellationToken = default);
    Task<AuditRecordId> NextAuditRecordIdAsync(CancellationToken cancellationToken = default);
    Task<int> CountAttemptsAsync(CancellationToken cancellationToken = default);
    Task<int> CountAuditRecordsAsync(CancellationToken cancellationToken = default);
}

public sealed class OperationTaskRepository(ApplicationDbContext context)
    : RepositoryBase<OperationTask, OperationTaskId, ApplicationDbContext>(context), IOperationTaskRepository
{
    public async Task<OperationTask?> GetByIdAsync(string operationTaskId, CancellationToken cancellationToken = default)
    {
        var id = new OperationTaskId(operationTaskId);
        return await DbContext.OperationTasks
            .Include(x => x.Attempts)
            .Include(x => x.AuditRecords)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<OperationTask?> GetByIdempotencyScopeAsync(string idempotencyScope, CancellationToken cancellationToken = default)
    {
        return await DbContext.OperationTasks
            .Include(x => x.Attempts)
            .Include(x => x.AuditRecords)
            .SingleOrDefaultAsync(x => x.IdempotencyScope == idempotencyScope, cancellationToken);
    }

    public async Task<IReadOnlyList<OperationTask>> GetPendingAsync(string organizationId, string environmentId, int take, CancellationToken cancellationToken = default)
    {
        return await DbContext.OperationTasks
            .Include(x => x.Attempts)
            .Include(x => x.AuditRecords)
            .Where(x => x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.Status == "queued")
            .OrderBy(x => x.RequestedAtUtc)
            .ThenBy(x => x.Id)
            .Take(Math.Clamp(take, 1, 50))
            .ToListAsync(cancellationToken);
    }

    public async Task<OperationTaskId> NextTaskIdAsync(CancellationToken cancellationToken = default)
    {
        var count = await DbContext.OperationTasks.CountAsync(cancellationToken);
        return new OperationTaskId($"op-{count + 1:000000}");
    }

    public async Task<AuditRecordId> NextAuditRecordIdAsync(CancellationToken cancellationToken = default)
    {
        var count = await DbContext.AuditRecords.CountAsync(cancellationToken);
        return new AuditRecordId($"audit-{count + 1:000000}");
    }

    public async Task<int> CountAttemptsAsync(CancellationToken cancellationToken = default)
    {
        return await DbContext.OperationAttempts.CountAsync(cancellationToken);
    }

    public async Task<int> CountAuditRecordsAsync(CancellationToken cancellationToken = default)
    {
        return await DbContext.AuditRecords.CountAsync(cancellationToken);
    }
}
