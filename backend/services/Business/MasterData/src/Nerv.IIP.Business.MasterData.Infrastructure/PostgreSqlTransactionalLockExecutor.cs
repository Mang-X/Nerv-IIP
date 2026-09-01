using NetCorePal.Extensions.Repository;
using NetCorePal.Extensions.Repository.EntityFrameworkCore;

namespace Nerv.IIP.Business.MasterData.Infrastructure;

public sealed class PostgreSqlTransactionalLockExecutor(
    ApplicationDbContext dbContext,
    ITransactionUnitOfWork unitOfWork)
{
    public PostgreSqlTransactionalLockExecutor(ApplicationDbContext dbContext)
        : this(dbContext, dbContext)
    {
    }

    public async Task<T> ExecuteAsync<T>(
        IReadOnlyCollection<string> lockKeys,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lockKeys);
        ArgumentNullException.ThrowIfNull(action);
        if (lockKeys.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("PostgreSQL transaction lock keys cannot be blank.", nameof(lockKeys));
        }

        var canonicalLockKeys = lockKeys
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (canonicalLockKeys.Length == 0)
        {
            throw new ArgumentException("At least one PostgreSQL transaction lock key is required.", nameof(lockKeys));
        }

        if (!dbContext.Database.IsNpgsql())
        {
            return await action(cancellationToken);
        }

        if (unitOfWork.CurrentTransaction is not null)
        {
            await AcquireLocksAsync(canonicalLockKeys, cancellationToken);
            var enlistedResult = await action(cancellationToken);
            await ((IUnitOfWork)unitOfWork).SaveEntitiesAsync(cancellationToken);
            return enlistedResult;
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        unitOfWork.CurrentTransaction = transaction;
        try
        {
            await AcquireLocksAsync(canonicalLockKeys, cancellationToken);
            var result = await action(cancellationToken);
            await ((IUnitOfWork)unitOfWork).SaveEntitiesAsync(cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await unitOfWork.RollbackAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            unitOfWork.CurrentTransaction = null;
        }
    }

    private async Task AcquireLocksAsync(
        IReadOnlyCollection<string> lockKeys,
        CancellationToken cancellationToken)
    {
        foreach (var lockKey in lockKeys)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))",
                cancellationToken);
        }
    }
}
