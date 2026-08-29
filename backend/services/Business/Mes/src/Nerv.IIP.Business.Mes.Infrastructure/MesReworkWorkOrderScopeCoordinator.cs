using Microsoft.EntityFrameworkCore.Storage;

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

public sealed class PostgreSqlMesReworkWorkOrderScopeCoordinator(ApplicationDbContext dbContext)
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
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        IDbContextTransaction? ownedTransaction = null;
        try
        {
            if (dbContext.Database.CurrentTransaction is null)
            {
                ownedTransaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            }

            var lockKey = $"mes-rework-work-order:{organizationId.Trim()}:{environmentId.Trim()}:{ncrId.Trim()}";
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))",
                cancellationToken);
            await action(cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            if (ownedTransaction is not null)
            {
                await ownedTransaction.CommitAsync(cancellationToken);
            }
        }
        finally
        {
            if (ownedTransaction is not null)
            {
                await ownedTransaction.DisposeAsync();
            }
        }
    }
}
