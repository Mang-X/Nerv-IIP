using NetCorePal.Extensions.Repository;
using NetCorePal.Extensions.Repository.EntityFrameworkCore;

namespace Nerv.IIP.Business.Mes.Infrastructure;

public interface IMesReworkWorkOrderScopeCoordinator
{
    Task ExecuteAsync(
        string organizationId,
        string environmentId,
        string ncrId,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken);
}

public sealed class PostgreSqlMesReworkWorkOrderScopeCoordinator(
    ApplicationDbContext dbContext,
    ITransactionUnitOfWork unitOfWork)
    : IMesReworkWorkOrderScopeCoordinator
{
    public async Task ExecuteAsync(
        string organizationId,
        string environmentId,
        string ncrId,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!dbContext.Database.IsNpgsql())
        {
            await action(cancellationToken);
            await ((IUnitOfWork)unitOfWork).SaveEntitiesAsync(cancellationToken);
            return;
        }

        if (unitOfWork.CurrentTransaction is not null)
        {
            await AcquireLockAsync(organizationId, environmentId, ncrId, cancellationToken);
            await action(cancellationToken);
            await ((IUnitOfWork)unitOfWork).SaveEntitiesAsync(cancellationToken);
            return;
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        unitOfWork.CurrentTransaction = transaction;
        try
        {
            await AcquireLockAsync(organizationId, environmentId, ncrId, cancellationToken);
            await action(cancellationToken);
            await ((IUnitOfWork)unitOfWork).SaveEntitiesAsync(cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
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
        string ncrId,
        CancellationToken cancellationToken)
    {
        var lockKey = $"mes-rework-work-order:{organizationId.Trim()}:{environmentId.Trim()}:{ncrId.Trim()}";
        return dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))",
            cancellationToken);
    }
}
