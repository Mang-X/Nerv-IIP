namespace Nerv.IIP.Business.Scheduling.Infrastructure;

public interface IOperationExecutionProjectionMutationLock
{
    Task AcquireAsync(
        string organizationId,
        string environmentId,
        string workOrderId,
        string operationId,
        CancellationToken cancellationToken);
}

public sealed class PostgreSqlOperationExecutionProjectionMutationLock(ApplicationDbContext dbContext)
    : IOperationExecutionProjectionMutationLock
{
    public async Task AcquireAsync(
        string organizationId,
        string environmentId,
        string workOrderId,
        string operationId,
        CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsNpgsql())
        {
            return;
        }

        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "Operation execution projection mutation requires an active unit-of-work transaction.");
        }

        var lockKey = $"scheduling-execution:{organizationId}:{environmentId}:{workOrderId}:{operationId}";
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))",
            cancellationToken);
    }
}
