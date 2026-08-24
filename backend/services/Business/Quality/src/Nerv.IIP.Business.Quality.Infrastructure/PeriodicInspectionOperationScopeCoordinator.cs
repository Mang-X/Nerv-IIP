using Microsoft.EntityFrameworkCore.Storage;

namespace Nerv.IIP.Business.Quality.Infrastructure;

public interface IPeriodicInspectionOperationScopeCoordinator
{
    Task ExecuteAsync(
        string organizationId,
        string environmentId,
        string workOrderId,
        IReadOnlyCollection<string> operationIds,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken);
}

public sealed class PeriodicInspectionOperationScopeCoordinator(ApplicationDbContext dbContext)
    : IPeriodicInspectionOperationScopeCoordinator
{
    public async Task ExecuteAsync(
        string organizationId,
        string environmentId,
        string workOrderId,
        IReadOnlyCollection<string> operationIds,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        var keys = operationIds
            .Select(operationId => $"quality-periodic-inspection:{organizationId.Trim()}:{environmentId.Trim()}:{workOrderId.Trim()}:{operationId.Trim()}")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (keys.Length == 0)
        {
            throw new ArgumentException("At least one operation id is required.", nameof(operationIds));
        }

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

            foreach (var key in keys)
            {
                await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))",
                    cancellationToken);
            }

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
