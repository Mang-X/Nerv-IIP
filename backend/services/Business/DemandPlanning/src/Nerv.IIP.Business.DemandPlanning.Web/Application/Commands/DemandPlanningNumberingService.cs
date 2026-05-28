using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.DemandPlanning.Infrastructure;
using Nerv.IIP.Business.DemandPlanning.Infrastructure.Numbering;

namespace Nerv.IIP.Business.DemandPlanning.Web.Application.Commands;

public sealed record DemandPlanningNumberAllocation(string Number, bool IsIdempotentReplay);

public sealed class DemandPlanningNumberingService(ApplicationDbContext? dbContext = null)
{
    private const int MaxCounterSaveAttempts = 5;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> CounterScopeLocks = new(StringComparer.Ordinal);

    private readonly ApplicationDbContext? _dbContext = dbContext;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, long> _counters = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DemandPlanningIdempotentNumber> _idempotency = new(StringComparer.Ordinal);

    public async Task<DemandPlanningNumberAllocation> AllocateDemandReferenceAsync(
        string organizationId,
        string environmentId,
        string? requestedReference,
        string? idempotencyKey,
        string payloadFingerprint,
        CancellationToken cancellationToken)
    {
        if (_dbContext is null)
        {
            return AllocateDemandReferenceInMemory(organizationId, environmentId, requestedReference, idempotencyKey, payloadFingerprint);
        }

        var normalizedReference = Normalize(requestedReference);
        var normalizedIdempotencyKey = Normalize(idempotencyKey);
        var idempotencyRecord = normalizedIdempotencyKey is null
            ? null
            : await FindIdempotencyRecordAsync(organizationId, environmentId, "demand", normalizedIdempotencyKey, cancellationToken);
        if (idempotencyRecord is not null)
        {
            if (!string.Equals(idempotencyRecord.PayloadFingerprint, payloadFingerprint, StringComparison.Ordinal))
            {
                throw new KnownException($"Idempotency key '{normalizedIdempotencyKey}' conflicts with a different demand create payload.");
            }

            return new DemandPlanningNumberAllocation(idempotencyRecord.Number, true);
        }

        var number = normalizedReference ?? await NextNumberAsync(organizationId, environmentId, cancellationToken);
        if (normalizedIdempotencyKey is not null)
        {
            _dbContext.NumberingIdempotencyKeys.Add(new NumberingIdempotencyKey(
                organizationId,
                environmentId,
                "demand",
                normalizedIdempotencyKey,
                number,
                payloadFingerprint,
                DateTimeOffset.UtcNow));
        }

        return new DemandPlanningNumberAllocation(number, false);
    }

    private async Task<string> NextNumberAsync(string organizationId, string environmentId, CancellationToken cancellationToken)
    {
        var dateSegment = DateTimeOffset.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var scope = Key(organizationId, environmentId, "demand", string.Empty, dateSegment);
        var scopeLock = CounterScopeLocks.GetOrAdd(scope, _ => new SemaphoreSlim(1, 1));
        for (var attempt = 1; attempt <= MaxCounterSaveAttempts; attempt++)
        {
            await scopeLock.WaitAsync(cancellationToken);
            try
            {
                var counter = _dbContext!.NumberingCounters.Local.FirstOrDefault(x =>
                        x.OrganizationId == organizationId &&
                        x.EnvironmentId == environmentId &&
                        x.DocumentType == "demand" &&
                        x.SiteCode == string.Empty &&
                        x.DateSegment == dateSegment)
                    ?? await _dbContext.NumberingCounters.SingleOrDefaultAsync(x =>
                        x.OrganizationId == organizationId &&
                        x.EnvironmentId == environmentId &&
                        x.DocumentType == "demand" &&
                        x.SiteCode == string.Empty &&
                        x.DateSegment == dateSegment,
                        cancellationToken);

                if (counter is null)
                {
                    counter = new NumberingCounter(organizationId, environmentId, "demand", string.Empty, dateSegment, "DEMAND");
                    _dbContext.NumberingCounters.Add(counter);
                }

                var next = counter.Advance();
                await _dbContext.SaveChangesAsync(cancellationToken);
                return $"DEMAND-{dateSegment}-{next:000000}";
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxCounterSaveAttempts)
            {
                DetachCounterEntries();
            }
            catch (DbUpdateException) when (attempt < MaxCounterSaveAttempts)
            {
                DetachCounterEntries();
            }
            finally
            {
                scopeLock.Release();
            }
        }

        throw new KnownException($"Unable to allocate demand number after {MaxCounterSaveAttempts} attempts.");
    }

    private void DetachCounterEntries()
    {
        foreach (var entry in _dbContext!.ChangeTracker.Entries<NumberingCounter>().ToArray())
        {
            entry.State = EntityState.Detached;
        }
    }

    private async Task<NumberingIdempotencyKey?> FindIdempotencyRecordAsync(
        string organizationId,
        string environmentId,
        string documentType,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        return _dbContext!.NumberingIdempotencyKeys.Local.FirstOrDefault(x =>
                x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                x.DocumentType == documentType &&
                x.IdempotencyKey == idempotencyKey)
            ?? await _dbContext.NumberingIdempotencyKeys.SingleOrDefaultAsync(x =>
                x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                x.DocumentType == documentType &&
                x.IdempotencyKey == idempotencyKey,
                cancellationToken);
    }

    private DemandPlanningNumberAllocation AllocateDemandReferenceInMemory(
        string organizationId,
        string environmentId,
        string? requestedReference,
        string? idempotencyKey,
        string payloadFingerprint)
    {
        var normalizedReference = Normalize(requestedReference);
        var normalizedIdempotencyKey = Normalize(idempotencyKey);
        if (normalizedIdempotencyKey is not null
            && _idempotency.TryGetValue(Key(organizationId, environmentId, "demand", normalizedIdempotencyKey), out var existing))
        {
            if (!string.Equals(existing.PayloadFingerprint, payloadFingerprint, StringComparison.Ordinal))
            {
                throw new KnownException($"Idempotency key '{normalizedIdempotencyKey}' conflicts with a different demand create payload.");
            }

            return new DemandPlanningNumberAllocation(existing.Number, true);
        }

        var number = normalizedReference ?? NextNumber(organizationId, environmentId);
        if (normalizedIdempotencyKey is not null)
        {
            _idempotency.TryAdd(
                Key(organizationId, environmentId, "demand", normalizedIdempotencyKey),
                new DemandPlanningIdempotentNumber(number, payloadFingerprint));
        }

        return new DemandPlanningNumberAllocation(number, false);
    }

    private string NextNumber(string organizationId, string environmentId)
    {
        var dateSegment = DateTimeOffset.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var scope = Key(organizationId, environmentId, "demand", dateSegment);
        lock (_lock)
        {
            _counters.TryGetValue(scope, out var current);
            var next = current + 1;
            _counters[scope] = next;
            return $"DEMAND-{dateSegment}-{next:000000}";
        }
    }

    private static string Key(params string[] parts)
    {
        return string.Join('|', parts.Select(part => part.Trim().ToLowerInvariant()));
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private sealed record DemandPlanningIdempotentNumber(string Number, string PayloadFingerprint);
}
