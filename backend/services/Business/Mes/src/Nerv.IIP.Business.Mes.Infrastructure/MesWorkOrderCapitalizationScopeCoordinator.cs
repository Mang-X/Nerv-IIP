using NetCorePal.Extensions.Repository.EntityFrameworkCore;

namespace Nerv.IIP.Business.Mes.Infrastructure;

public interface IMesWorkOrderCapitalizationScopeCoordinator
{
    Task ExecuteAsync(
        string organizationId,
        string environmentId,
        string workOrderId,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken);

    Task<T> ExecuteAsync<T>(
        string organizationId,
        string environmentId,
        string workOrderId,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken);
}

public sealed class PostgreSqlMesWorkOrderCapitalizationScopeCoordinator(
    ApplicationDbContext dbContext,
    ITransactionUnitOfWork unitOfWork) : IMesWorkOrderCapitalizationScopeCoordinator
{
    public async Task ExecuteAsync(
        string organizationId,
        string environmentId,
        string workOrderId,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(
            organizationId,
            environmentId,
            workOrderId,
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
        string workOrderId,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!dbContext.Database.IsNpgsql())
        {
            // BusinessMES runtime persistence is PostgreSQL-only. Provider-light tests still exercise
            // the command/consumer save path without pretending to provide advisory-lock serialization.
            return await action(cancellationToken);
        }

        if (unitOfWork.CurrentTransaction is not null)
        {
            await AcquireLockAsync(organizationId, environmentId, workOrderId, cancellationToken);
            return await action(cancellationToken);
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        unitOfWork.CurrentTransaction = transaction;
        try
        {
            await AcquireLockAsync(organizationId, environmentId, workOrderId, cancellationToken);
            var result = await action(cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await unitOfWork.RollbackAsync(cancellationToken);
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
        string workOrderId,
        CancellationToken cancellationToken)
    {
        var lockKey = $"mes-work-order-capitalization:{organizationId.Trim()}:{environmentId.Trim()}:{workOrderId.Trim()}";
        return dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))",
            cancellationToken);
    }
}
