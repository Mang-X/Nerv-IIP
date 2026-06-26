using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Ops.Domain;
using Nerv.IIP.Ops.Domain.AggregatesModel.OperationTaskAggregate;
using System.Security.Cryptography;
using System.Text;
using NetCorePal.Extensions.Repository;
using NetCorePal.Extensions.Repository.EntityFrameworkCore;

namespace Nerv.IIP.Ops.Infrastructure.Repositories;

public interface IOperationTaskRepository : IRepository<OperationTask, OperationTaskId>
{
    Task<OperationTask?> GetByIdAsync(string operationTaskId, CancellationToken cancellationToken = default);
    Task<OperationTask?> GetByIdempotencyScopeAsync(string idempotencyScope, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OperationTask>> GetClaimableAsync(string organizationId, string environmentId, int take, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<OperationTaskId> NextTaskIdAsync(CancellationToken cancellationToken = default);
    Task<OperationAttemptId> NextAttemptIdAsync(CancellationToken cancellationToken = default);
    Task<AuditRecordId> NextAuditRecordIdAsync(CancellationToken cancellationToken = default);
    Task LockAuditChainAsync(string organizationId, string environmentId, CancellationToken cancellationToken = default);
    Task<AuditChainHead?> GetAuditChainHeadAsync(string organizationId, string environmentId, CancellationToken cancellationToken = default);
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

    public async Task<IReadOnlyList<OperationTask>> GetClaimableAsync(string organizationId, string environmentId, int take, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var cappedTake = Math.Clamp(take, 1, 50);
        return await DbContext.OperationTasks
            .FromSqlInterpolated($"""
                SELECT t.*
                FROM ops.operation_tasks AS t
                WHERE t."OrganizationId" = {organizationId}
                  AND t."EnvironmentId" = {environmentId}
                  AND (
                    t."Status" = 'queued'
                    OR (
                      t."Status" = 'dispatched'
                      AND EXISTS (
                        SELECT 1
                        FROM ops.operation_attempts AS a
                        WHERE a."OperationTaskId" = t."Id"
                          AND a."Status" = 'started'
                          AND a."LeaseId" IS NOT NULL
                          AND a."LeasedAtUtc" IS NOT NULL
                          AND a."LeasedUntilUtc" IS NOT NULL
                          AND a."AttemptNo" IS NOT NULL
                          AND a."MaxAttempts" IS NOT NULL
                          AND a."LeasedUntilUtc" <= {now}
                      )
                    )
                  )
                ORDER BY t."RequestedAtUtc", t."Id"
                FOR UPDATE SKIP LOCKED
                LIMIT {cappedTake}
                """)
            .Include(x => x.Attempts)
            .Include(x => x.AuditRecords)
            .ToListAsync(cancellationToken);
    }

    public Task<OperationTaskId> NextTaskIdAsync(CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        return Task.FromResult(new OperationTaskId($"op-{Guid.CreateVersion7():N}"));
    }

    public Task<OperationAttemptId> NextAttemptIdAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new OperationAttemptId($"attempt-{Guid.CreateVersion7():N}"));
    }

    public Task<AuditRecordId> NextAuditRecordIdAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new AuditRecordId($"audit-{Guid.CreateVersion7():N}"));
    }

    public async Task LockAuditChainAsync(string organizationId, string environmentId, CancellationToken cancellationToken = default)
    {
        if (!DbContext.Database.IsRelational())
        {
            return;
        }

        var lockKey = ComputeAuditChainLockKey(organizationId, environmentId);
        await DbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})",
            cancellationToken);
    }

    public async Task<AuditChainHead?> GetAuditChainHeadAsync(string organizationId, string environmentId, CancellationToken cancellationToken = default)
    {
        return await DbContext.AuditRecords
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .OrderByDescending(x => x.SequenceNo)
            .ThenByDescending(x => x.OccurredAtUtc)
            .ThenByDescending(x => x.Id)
            .Select(x => new AuditChainHead(x.SequenceNo, x.IntegrityHash))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static long ComputeAuditChainLockKey(string organizationId, string environmentId)
    {
        var material = $"ops-audit-chain\u001f{organizationId}\u001f{environmentId}";
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(material), hash);
        return BitConverter.ToInt64(hash);
    }
}
