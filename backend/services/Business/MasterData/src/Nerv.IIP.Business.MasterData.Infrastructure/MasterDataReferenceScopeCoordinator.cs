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
    PostgreSqlTransactionalLockExecutor lockExecutor)
    : IMasterDataReferenceScopeCoordinator
{
    public PostgreSqlMasterDataReferenceScopeCoordinator(ApplicationDbContext dbContext)
        : this(new PostgreSqlTransactionalLockExecutor(dbContext))
    {
    }

    public async Task<T> ExecuteAsync<T>(
        string organizationId,
        string environmentId,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        var lockKey = $"masterdata-reference:{organizationId.Trim()}:{environmentId.Trim()}";
        return await lockExecutor.ExecuteAsync(
            [lockKey],
            action,
            cancellationToken);
    }
}
