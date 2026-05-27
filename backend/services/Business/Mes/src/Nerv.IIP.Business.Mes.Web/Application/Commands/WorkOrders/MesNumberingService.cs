using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Infrastructure.Numbering;

namespace Nerv.IIP.Business.Mes.Web.Application.Commands.WorkOrders;

public sealed record MesNumberAllocation(string Number, bool IsIdempotentReplay);

public sealed class MesNumberingService(ApplicationDbContext? dbContext = null)
{
    private readonly ApplicationDbContext? _dbContext = dbContext;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, long> _counters = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, MesIdempotentNumber> _idempotency = new(StringComparer.Ordinal);

    public async Task<MesNumberAllocation> AllocateWorkOrderIdAsync(
        string organizationId,
        string environmentId,
        string? requestedWorkOrderId,
        string? idempotencyKey,
        string payloadFingerprint,
        CancellationToken cancellationToken)
    {
        return await AllocateAsync(organizationId, environmentId, "work-order", "WO", requestedWorkOrderId, idempotencyKey, payloadFingerprint, cancellationToken);
    }

    public async Task<MesNumberAllocation> AllocateAsync(
        string organizationId,
        string environmentId,
        string documentType,
        string prefix,
        string? requestedNumber,
        string? idempotencyKey,
        string payloadFingerprint,
        CancellationToken cancellationToken)
    {
        if (_dbContext is null)
        {
            return AllocateInMemory(organizationId, environmentId, documentType, prefix, requestedNumber, idempotencyKey, payloadFingerprint);
        }

        var normalizedRequestedNumber = Normalize(requestedNumber);
        var normalizedIdempotencyKey = Normalize(idempotencyKey);
        var idempotencyRecord = normalizedIdempotencyKey is null
            ? null
            : await FindIdempotencyRecordAsync(organizationId, environmentId, documentType, normalizedIdempotencyKey, cancellationToken);
        if (idempotencyRecord is not null)
        {
            if (!string.Equals(idempotencyRecord.PayloadFingerprint, payloadFingerprint, StringComparison.Ordinal))
            {
                throw new KnownException($"Idempotency key '{normalizedIdempotencyKey}' conflicts with a different MES create payload.");
            }

            return new MesNumberAllocation(idempotencyRecord.Number, true);
        }

        var number = normalizedRequestedNumber ?? await NextNumberAsync(organizationId, environmentId, documentType, prefix, cancellationToken);
        if (normalizedIdempotencyKey is not null)
        {
            _dbContext.NumberingIdempotencyKeys.Add(new NumberingIdempotencyKey(
                organizationId,
                environmentId,
                documentType,
                normalizedIdempotencyKey,
                number,
                payloadFingerprint,
                DateTimeOffset.UtcNow));
        }

        return new MesNumberAllocation(number, false);
    }

    public static string Fingerprint(params object?[] parts)
    {
        return string.Join('|', parts.Select(part => part switch
        {
            null => string.Empty,
            IEnumerable<string> values => string.Join(',', values.Order(StringComparer.Ordinal)),
            _ => Convert.ToString(part, CultureInfo.InvariantCulture) ?? string.Empty
        }));
    }

    private async Task<string> NextNumberAsync(string organizationId, string environmentId, string documentType, string prefix, CancellationToken cancellationToken)
    {
        var dateSegment = DateTimeOffset.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var counter = _dbContext!.NumberingCounters.Local.FirstOrDefault(x =>
                x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.DocumentType == documentType && x.SiteCode == string.Empty && x.DateSegment == dateSegment)
            ?? await _dbContext.NumberingCounters.SingleOrDefaultAsync(x =>
                x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.DocumentType == documentType && x.SiteCode == string.Empty && x.DateSegment == dateSegment,
                cancellationToken);

        if (counter is null)
        {
            counter = new NumberingCounter(organizationId, environmentId, documentType, string.Empty, dateSegment, prefix);
            _dbContext.NumberingCounters.Add(counter);
        }

        var next = counter.Advance();
        return $"{prefix}-{dateSegment}-{next:000000}";
    }

    private async Task<NumberingIdempotencyKey?> FindIdempotencyRecordAsync(string organizationId, string environmentId, string documentType, string idempotencyKey, CancellationToken cancellationToken)
    {
        return _dbContext!.NumberingIdempotencyKeys.Local.FirstOrDefault(x =>
                x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.DocumentType == documentType && x.IdempotencyKey == idempotencyKey)
            ?? await _dbContext.NumberingIdempotencyKeys.SingleOrDefaultAsync(x =>
                x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.DocumentType == documentType && x.IdempotencyKey == idempotencyKey,
                cancellationToken);
    }

    private MesNumberAllocation AllocateInMemory(string organizationId, string environmentId, string documentType, string prefix, string? requestedNumber, string? idempotencyKey, string payloadFingerprint)
    {
        var normalizedRequestedNumber = Normalize(requestedNumber);
        var normalizedIdempotencyKey = Normalize(idempotencyKey);
        if (normalizedIdempotencyKey is not null
            && _idempotency.TryGetValue(Key(organizationId, environmentId, documentType, normalizedIdempotencyKey), out var existing))
        {
            if (!string.Equals(existing.PayloadFingerprint, payloadFingerprint, StringComparison.Ordinal))
            {
                throw new KnownException($"Idempotency key '{normalizedIdempotencyKey}' conflicts with a different MES create payload.");
            }

            return new MesNumberAllocation(existing.Number, true);
        }

        var number = normalizedRequestedNumber ?? NextNumberInMemory(organizationId, environmentId, documentType, prefix);
        if (normalizedIdempotencyKey is not null)
        {
            _idempotency.TryAdd(Key(organizationId, environmentId, documentType, normalizedIdempotencyKey), new MesIdempotentNumber(number, payloadFingerprint));
        }

        return new MesNumberAllocation(number, false);
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

    private sealed record MesIdempotentNumber(string Number, string PayloadFingerprint);
}
