using Microsoft.EntityFrameworkCore;

namespace Nerv.IIP.Numbering;

public sealed class EfCoreNumberingStore(DbContext dbContext, DbSet<NumberingCounter> counters, DbSet<NumberingIdempotencyKey> idempotencyKeys)
    : INumberingStore
{
    private readonly DbContext _dbContext = dbContext;
    private readonly DbSet<NumberingCounter> _counters = counters;
    private readonly DbSet<NumberingIdempotencyKey> _idempotencyKeys = idempotencyKeys;

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
        var counter = _counters.Local.FirstOrDefault(x => IsScope(x, scope))
            ?? await _counters.SingleOrDefaultAsync(x =>
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
            _counters.Add(counter);
        }

        var next = counter.Advance();
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return next;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            DetachChangedCounters();
            throw new NumberingConcurrencyException("Numbering counter was updated concurrently.", exception);
        }
        catch (DbUpdateException exception)
        {
            DetachChangedCounters();
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

    private void DetachChangedCounters()
    {
        foreach (var entry in _dbContext.ChangeTracker.Entries<NumberingCounter>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified)
                     .ToArray())
        {
            entry.State = EntityState.Detached;
        }
    }
}
