using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.MasterData.Infrastructure;
using Nerv.IIP.Business.MasterData.Infrastructure.Numbering;

namespace Nerv.IIP.Business.MasterData.Web.Application.Commands.MasterData;

public sealed record MasterDataNumberAllocation(string Code, bool IsIdempotentReplay);

public sealed class MasterDataNumberingService(ApplicationDbContext? dbContext = null)
{
    private const int MaxCounterSaveAttempts = 5;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> CounterScopeLocks = new(StringComparer.Ordinal);

    private readonly ApplicationDbContext? _dbContext = dbContext;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, long> _counters = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, MasterDataIdempotentNumber> _idempotency = new(StringComparer.Ordinal);

    public async Task<MasterDataNumberAllocation> AllocateSkuCodeAsync(
        string organizationId,
        string environmentId,
        string? requestedCode,
        string? idempotencyKey,
        string payloadFingerprint,
        CancellationToken cancellationToken)
    {
        return await AllocateAsync(organizationId, environmentId, "sku", "SKU", requestedCode, idempotencyKey, payloadFingerprint, cancellationToken);
    }

    private async Task<MasterDataNumberAllocation> AllocateAsync(
        string organizationId,
        string environmentId,
        string documentType,
        string prefix,
        string? requestedCode,
        string? idempotencyKey,
        string payloadFingerprint,
        CancellationToken cancellationToken)
    {
        if (_dbContext is null)
        {
            return AllocateInMemory(organizationId, environmentId, documentType, prefix, requestedCode, idempotencyKey, payloadFingerprint);
        }

        var normalizedRequestedCode = Normalize(requestedCode);
        var normalizedIdempotencyKey = Normalize(idempotencyKey);
        var idempotencyRecord = normalizedIdempotencyKey is null
            ? null
            : await FindIdempotencyRecordAsync(organizationId, environmentId, documentType, normalizedIdempotencyKey, cancellationToken);
        if (idempotencyRecord is not null)
        {
            if (!string.Equals(idempotencyRecord.PayloadFingerprint, payloadFingerprint, StringComparison.Ordinal))
            {
                throw new KnownException($"Idempotency key '{normalizedIdempotencyKey}' conflicts with a different {documentType} create payload.");
            }

            return new MasterDataNumberAllocation(idempotencyRecord.Number, true);
        }

        var code = normalizedRequestedCode ?? await NextNumberAsync(organizationId, environmentId, documentType, prefix, cancellationToken);
        if (normalizedIdempotencyKey is not null)
        {
            _dbContext.NumberingIdempotencyKeys.Add(new NumberingIdempotencyKey(
                organizationId,
                environmentId,
                documentType,
                normalizedIdempotencyKey,
                code,
                payloadFingerprint,
                DateTimeOffset.UtcNow));
        }

        return new MasterDataNumberAllocation(code, false);
    }

    private async Task<string> NextNumberAsync(
        string organizationId,
        string environmentId,
        string documentType,
        string prefix,
        CancellationToken cancellationToken)
    {
        var dateSegment = DateTimeOffset.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var scope = Key(organizationId, environmentId, documentType, string.Empty, dateSegment);
        var scopeLock = CounterScopeLocks.GetOrAdd(scope, _ => new SemaphoreSlim(1, 1));
        for (var attempt = 1; attempt <= MaxCounterSaveAttempts; attempt++)
        {
            await scopeLock.WaitAsync(cancellationToken);
            try
            {
                var counter = _dbContext!.NumberingCounters.Local.FirstOrDefault(x =>
                        x.OrganizationId == organizationId &&
                        x.EnvironmentId == environmentId &&
                        x.DocumentType == documentType &&
                        x.SiteCode == string.Empty &&
                        x.DateSegment == dateSegment)
                    ?? await _dbContext.NumberingCounters.SingleOrDefaultAsync(x =>
                        x.OrganizationId == organizationId &&
                        x.EnvironmentId == environmentId &&
                        x.DocumentType == documentType &&
                        x.SiteCode == string.Empty &&
                        x.DateSegment == dateSegment,
                        cancellationToken);

                if (counter is null)
                {
                    counter = new NumberingCounter(organizationId, environmentId, documentType, string.Empty, dateSegment, prefix);
                    _dbContext.NumberingCounters.Add(counter);
                }

                var next = counter.Advance();
                await _dbContext.SaveChangesAsync(cancellationToken);
                return $"{prefix}-{dateSegment}-{next:000000}";
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

        throw new KnownException($"Unable to allocate {documentType} number after {MaxCounterSaveAttempts} attempts.");
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

    private MasterDataNumberAllocation AllocateInMemory(
        string organizationId,
        string environmentId,
        string documentType,
        string prefix,
        string? requestedCode,
        string? idempotencyKey,
        string payloadFingerprint)
    {
        var normalizedRequestedCode = Normalize(requestedCode);
        var normalizedIdempotencyKey = Normalize(idempotencyKey);
        if (normalizedIdempotencyKey is not null
            && _idempotency.TryGetValue(Key(organizationId, environmentId, documentType, normalizedIdempotencyKey), out var existing))
        {
            if (!string.Equals(existing.PayloadFingerprint, payloadFingerprint, StringComparison.Ordinal))
            {
                throw new KnownException($"Idempotency key '{normalizedIdempotencyKey}' conflicts with a different {documentType} create payload.");
            }

            return new MasterDataNumberAllocation(existing.Code, true);
        }

        var code = normalizedRequestedCode ?? NextNumberInMemory(organizationId, environmentId, documentType, prefix);
        if (normalizedIdempotencyKey is not null)
        {
            _idempotency.TryAdd(
                Key(organizationId, environmentId, documentType, normalizedIdempotencyKey),
                new MasterDataIdempotentNumber(code, payloadFingerprint));
        }

        return new MasterDataNumberAllocation(code, false);
    }

    private string NextNumberInMemory(string organizationId, string environmentId, string documentType, string prefix)
    {
        var dateSegment = DateTimeOffset.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var scope = Key(organizationId, environmentId, documentType, dateSegment);
        lock (_lock)
        {
            _counters.TryGetValue(scope, out var current);
            var next = current + 1;
            _counters[scope] = next;
            return $"{prefix}-{dateSegment}-{next:000000}";
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

    private sealed record MasterDataIdempotentNumber(string Code, string PayloadFingerprint);
}
