using Microsoft.EntityFrameworkCore.Storage;

namespace Nerv.IIP.Business.Mes.Infrastructure;

public interface IMesSkuAvailabilityScopeCoordinator
{
    Task ExecuteAsync(
        string organizationId,
        string environmentId,
        string skuCode,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken);

    Task<T> ExecuteAsync<T>(
        string organizationId,
        string environmentId,
        string skuCode,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken);
}

public sealed class PostgreSqlMesSkuAvailabilityScopeCoordinator(ApplicationDbContext dbContext)
    : IMesSkuAvailabilityScopeCoordinator
{
    public async Task ExecuteAsync(
        string organizationId,
        string environmentId,
        string skuCode,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(
            organizationId,
            environmentId,
            skuCode,
            async token =>
            {
                await action(token);
                return true;
            },
            cancellationToken);
    }

    public async Task<T> ExecuteAsync<T>(
        string organizationId,
        string environmentId,
        string skuCode,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!dbContext.Database.IsNpgsql())
        {
            var result = await action(cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return result;
        }

        IDbContextTransaction? ownedTransaction = null;
        try
        {
            if (dbContext.Database.CurrentTransaction is null)
            {
                ownedTransaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            }

            var lockKey = $"mes-sku-availability:{organizationId.Trim()}:{environmentId.Trim()}:{skuCode.Trim()}";
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))",
                cancellationToken);
            var result = await action(cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            if (ownedTransaction is not null)
            {
                await ownedTransaction.CommitAsync(cancellationToken);
            }

            return result;
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
