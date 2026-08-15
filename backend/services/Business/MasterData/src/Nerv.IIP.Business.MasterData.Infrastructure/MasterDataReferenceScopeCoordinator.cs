using NetCorePal.Extensions.Repository;
using NetCorePal.Extensions.Repository.EntityFrameworkCore;

namespace Nerv.IIP.Business.MasterData.Infrastructure;

public interface IMasterDataReferenceScopeCoordinator
{
    Task<T> ExecuteAsync<T>(
        string organizationId,
        string environmentId,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken);
}

public sealed class PostgreSqlMasterDataReferenceScopeCoordinator(
    ApplicationDbContext dbContext,
    ITransactionUnitOfWork unitOfWork)
    : IMasterDataReferenceScopeCoordinator
{
    public PostgreSqlMasterDataReferenceScopeCoordinator(ApplicationDbContext dbContext)
        : this(dbContext, dbContext)
    {
    }

    public async Task<T> ExecuteAsync<T>(
        string organizationId,
        string environmentId,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!dbContext.Database.IsNpgsql())
        {
            return await action(cancellationToken);
        }

        if (unitOfWork.CurrentTransaction is not null)
        {
            await AcquireLockAsync(organizationId, environmentId, cancellationToken);
            var enlistedResult = await action(cancellationToken);
            await ((IUnitOfWork)unitOfWork).SaveEntitiesAsync(cancellationToken);
            return enlistedResult;
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        unitOfWork.CurrentTransaction = transaction;
        try
        {
            await AcquireLockAsync(organizationId, environmentId, cancellationToken);
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

    private Task AcquireLockAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken)
    {
        var lockKey = $"masterdata-reference:{organizationId.Trim()}:{environmentId.Trim()}";
        return dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))",
            cancellationToken);
    }
}
