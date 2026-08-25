using NetCorePal.Extensions.Repository;
using NetCorePal.Extensions.Repository.EntityFrameworkCore;

namespace Nerv.IIP.Business.MasterData.Infrastructure;

public interface IToolingAuditOperationCoordinator
{
    Task<T> ExecuteAsync<T>(
        string organizationId,
        string environmentId,
        string operationId,
        string? toolingCode,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken);
}

public sealed class PostgreSqlToolingAuditOperationCoordinator(
    ApplicationDbContext dbContext,
    ITransactionUnitOfWork unitOfWork)
    : IToolingAuditOperationCoordinator
{
    public PostgreSqlToolingAuditOperationCoordinator(ApplicationDbContext dbContext)
        : this(dbContext, dbContext)
    {
    }

    public async Task<T> ExecuteAsync<T>(
        string organizationId,
        string environmentId,
        string operationId,
        string? toolingCode,
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
            await AcquireLocksAsync(organizationId, environmentId, operationId, toolingCode, cancellationToken);
            var enlistedResult = await action(cancellationToken);
            await ((IUnitOfWork)unitOfWork).SaveEntitiesAsync(cancellationToken);
            return enlistedResult;
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        unitOfWork.CurrentTransaction = transaction;
        try
        {
            await AcquireLocksAsync(organizationId, environmentId, operationId, toolingCode, cancellationToken);
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
        string organizationId,
        string environmentId,
        string operationId,
        string? toolingCode,
        CancellationToken cancellationToken)
    {
        var scope = $"{organizationId.Trim()}:{environmentId.Trim()}";
        var operationLock = $"masterdata-tooling-operation:{scope}:{operationId.Trim()}";
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({operationLock}, 0))",
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(toolingCode))
        {
            var targetLock = $"masterdata-tooling-target:{scope}:{toolingCode.Trim().ToUpperInvariant()}";
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({targetLock}, 0))",
                cancellationToken);
        }
    }
}
