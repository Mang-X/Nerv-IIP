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
    PostgreSqlTransactionalLockExecutor lockExecutor)
    : IToolingAuditOperationCoordinator
{
    public PostgreSqlToolingAuditOperationCoordinator(ApplicationDbContext dbContext)
        : this(new PostgreSqlTransactionalLockExecutor(dbContext))
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
        var scope = $"{organizationId.Trim()}:{environmentId.Trim()}";
        List<string> lockKeys = [$"masterdata-tooling-operation:{scope}:{operationId.Trim()}"];
        if (!string.IsNullOrWhiteSpace(toolingCode))
        {
            lockKeys.Add($"masterdata-tooling-target:{scope}:{toolingCode.Trim().ToUpperInvariant()}");
        }

        return await lockExecutor.ExecuteAsync(lockKeys, action, cancellationToken);
    }
}
