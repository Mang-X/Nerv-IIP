using Microsoft.EntityFrameworkCore.Storage;

namespace Nerv.IIP.Business.Scheduling.Infrastructure;

public interface IScheduleReleaseScopeLock
{
    Task<IAsyncDisposable> AcquireAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken);
}

public sealed class PostgreSqlScheduleReleaseScopeLock(ApplicationDbContext dbContext)
    : IScheduleReleaseScopeLock
{
    public async Task<IAsyncDisposable> AcquireAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsNpgsql())
        {
            return NoopAsyncDisposable.Instance;
        }

        IDbContextTransaction? transaction = dbContext.Database.CurrentTransaction;
        if (transaction is null)
        {
            throw new InvalidOperationException(
                "Schedule release scope lock requires an active unit-of-work transaction.");
        }

        var lockKey = $"scheduling-release:{organizationId.Trim()}:{environmentId.Trim()}";
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))",
            cancellationToken);
        return NoopAsyncDisposable.Instance;
    }

    private sealed class NoopAsyncDisposable : IAsyncDisposable
    {
        public static NoopAsyncDisposable Instance { get; } = new();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
