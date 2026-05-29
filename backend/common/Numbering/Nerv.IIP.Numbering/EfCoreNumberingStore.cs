using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Nerv.IIP.Numbering;

public sealed class NumberingDbContextLease(DbContext dbContext, IAsyncDisposable owner) : IAsyncDisposable
{
    public DbContext DbContext { get; } = dbContext;

    public ValueTask DisposeAsync()
    {
        return owner.DisposeAsync();
    }
}

public sealed class EfCoreNumberingStore(DbContext dbContext, Func<CancellationToken, ValueTask<NumberingDbContextLease>> counterDbContextLeaseFactory)
    : INumberingStore
{
    private readonly DbContext _dbContext = dbContext;
    private readonly Func<CancellationToken, ValueTask<NumberingDbContextLease>> _counterDbContextLeaseFactory = counterDbContextLeaseFactory;
    private readonly DbSet<NumberingIdempotencyKey> _idempotencyKeys = dbContext.Set<NumberingIdempotencyKey>();

    public static Func<CancellationToken, ValueTask<NumberingDbContextLease>> CreateDbContextLeaseFactory<TDbContext>(
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
                return new NumberingDbContextLease(dbContext, scope);
            }
            catch
            {
                await scope.DisposeAsync();
                throw;
            }
        };
    }

    public async Task<NumberingIdempotencyKey?> FindIdempotencyRecordAsync(
        string organizationId,
        string environmentId,
        string documentType,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        return _idempotencyKeys.Local.FirstOrDefault(x =>
                x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                x.DocumentType == documentType &&
                x.IdempotencyKey == idempotencyKey)
            ?? await _idempotencyKeys.SingleOrDefaultAsync(x =>
                x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                x.DocumentType == documentType &&
                x.IdempotencyKey == idempotencyKey,
                cancellationToken);
    }

    public void AddIdempotencyRecord(NumberingIdempotencyKey idempotencyKey)
    {
        _idempotencyKeys.Add(idempotencyKey);
    }

    public async Task<long> ReserveNextCounterValueAsync(NumberingCounterScope scope, CancellationToken cancellationToken)
    {
        await using var lease = await _counterDbContextLeaseFactory(cancellationToken);
        var counterDbContext = lease.DbContext;
        var counters = counterDbContext.Set<NumberingCounter>();
        var counter = counters.Local.FirstOrDefault(x => IsScope(x, scope))
            ?? await counters.SingleOrDefaultAsync(x =>
                x.OrganizationId == scope.OrganizationId &&
                x.EnvironmentId == scope.EnvironmentId &&
                x.DocumentType == scope.DocumentType &&
                x.SiteCode == scope.SiteCode &&
                x.DateSegment == scope.DateSegment,
                cancellationToken);

        if (counter is null)
        {
            counter = new NumberingCounter(
                scope.OrganizationId,
                scope.EnvironmentId,
                scope.DocumentType,
                scope.SiteCode,
                scope.DateSegment,
                scope.Prefix);
            counters.Add(counter);
        }

        var next = counter.Advance();
        try
        {
            await counterDbContext.SaveChangesAsync(cancellationToken);
            return next;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            DetachChangedCounters(counterDbContext);
            throw new NumberingConcurrencyException("Numbering counter was updated concurrently.", exception);
        }
        catch (DbUpdateException exception)
        {
            DetachChangedCounters(counterDbContext);
            throw new NumberingConcurrencyException("Numbering counter reservation collided with another writer.", exception);
        }
    }

    private static bool IsScope(NumberingCounter counter, NumberingCounterScope scope)
    {
        return counter.OrganizationId == scope.OrganizationId &&
            counter.EnvironmentId == scope.EnvironmentId &&
            counter.DocumentType == scope.DocumentType &&
            counter.SiteCode == scope.SiteCode &&
            counter.DateSegment == scope.DateSegment;
    }

    private static void DetachChangedCounters(DbContext dbContext)
    {
        foreach (var entry in dbContext.ChangeTracker.Entries<NumberingCounter>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified)
                     .ToArray())
        {
            entry.State = EntityState.Detached;
        }
    }
}
