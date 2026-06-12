using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Nerv.IIP.Coding;

public sealed class CodeDbContextLease(DbContext dbContext, IAsyncDisposable owner) : IAsyncDisposable
{
    public DbContext DbContext { get; } = dbContext;

    public ValueTask DisposeAsync()
    {
        return owner.DisposeAsync();
    }
}

public sealed class EfCoreCodeStore(
    DbContext dbContext,
    Func<CancellationToken, ValueTask<CodeDbContextLease>> counterDbContextLeaseFactory)
    : ICodeStore
{
    private readonly Func<CancellationToken, ValueTask<CodeDbContextLease>> _counterDbContextLeaseFactory = counterDbContextLeaseFactory;
    private readonly DbSet<CodeIdempotencyKey> _idempotencyKeys = dbContext.Set<CodeIdempotencyKey>();

    public static Func<CancellationToken, ValueTask<CodeDbContextLease>> CreateDbContextLeaseFactory<TDbContext>(
        IServiceScopeFactory serviceScopeFactory)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(serviceScopeFactory);

        return async cancellationToken =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var scope = serviceScopeFactory.CreateAsyncScope();
            try
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();
                return new CodeDbContextLease(dbContext, scope);
            }
            catch
            {
                await scope.DisposeAsync();
                throw;
            }
        };
    }

    public async Task<CodeIdempotencyKey?> FindIdempotencyRecordAsync(
        string organizationId,
        string environmentId,
        string ruleKey,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        return _idempotencyKeys.Local.FirstOrDefault(x =>
                x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                x.RuleKey == ruleKey &&
                x.IdempotencyKey == idempotencyKey)
            ?? await _idempotencyKeys.SingleOrDefaultAsync(x =>
                x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                x.RuleKey == ruleKey &&
                x.IdempotencyKey == idempotencyKey,
                cancellationToken);
    }

    public void AddIdempotencyRecord(CodeIdempotencyKey idempotencyKey)
    {
        _idempotencyKeys.Add(idempotencyKey);
    }

    public async Task<long> ReserveNextCounterValueAsync(CodeCounterScope scope, CancellationToken cancellationToken)
    {
        await using var lease = await _counterDbContextLeaseFactory(cancellationToken);
        var counterDbContext = lease.DbContext;
        var counters = counterDbContext.Set<CodeCounter>();
        var counter = counters.Local.FirstOrDefault(x => IsScope(x, scope))
            ?? await counters.SingleOrDefaultAsync(x =>
                x.OrganizationId == scope.OrganizationId &&
                x.EnvironmentId == scope.EnvironmentId &&
                x.RuleKey == scope.RuleKey &&
                x.SiteCode == scope.SiteCode &&
                x.ResetKey == scope.ResetKey,
                cancellationToken);

        if (counter is null)
        {
            counter = new CodeCounter(scope.OrganizationId, scope.EnvironmentId, scope.RuleKey, scope.SiteCode, scope.ResetKey);
            counters.Add(counter);
        }

        var next = counter.AdvanceFrom(scope.Start);
        try
        {
            await counterDbContext.SaveChangesAsync(cancellationToken);
            return next;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            DetachChangedCounters(counterDbContext);
            throw new CodeConcurrencyException("Code counter was updated concurrently.", exception);
        }
        catch (DbUpdateException exception)
        {
            DetachChangedCounters(counterDbContext);
            throw new CodeConcurrencyException("Code counter reservation collided with another writer.", exception);
        }
    }

    private static bool IsScope(CodeCounter counter, CodeCounterScope scope)
    {
        return counter.OrganizationId == scope.OrganizationId &&
            counter.EnvironmentId == scope.EnvironmentId &&
            counter.RuleKey == scope.RuleKey &&
            counter.SiteCode == scope.SiteCode &&
            counter.ResetKey == scope.ResetKey;
    }

    private static void DetachChangedCounters(DbContext dbContext)
    {
        foreach (var entry in dbContext.ChangeTracker.Entries<CodeCounter>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified)
                     .ToArray())
        {
            entry.State = EntityState.Detached;
        }
    }
}
