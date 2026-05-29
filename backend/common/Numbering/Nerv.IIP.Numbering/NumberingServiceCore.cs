using System.Collections.Concurrent;
using System.Globalization;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Numbering;

public sealed record NumberingAllocation(string Number, bool IsIdempotentReplay);

public sealed record NumberingAllocationRequest(
    string OrganizationId,
    string EnvironmentId,
    string DocumentType,
    string Prefix,
    string? RequestedNumber,
    string? IdempotencyKey,
    string PayloadFingerprint,
    string ConflictResourceLabel,
    string SiteCode = "");

public sealed record NumberingCounterScope(
    string OrganizationId,
    string EnvironmentId,
    string DocumentType,
    string SiteCode,
    string DateSegment,
    string Prefix);

public interface INumberingStore
{
    Task<NumberingIdempotencyKey?> FindIdempotencyRecordAsync(
        string organizationId,
        string environmentId,
        string documentType,
        string idempotencyKey,
        CancellationToken cancellationToken);

    void AddIdempotencyRecord(NumberingIdempotencyKey idempotencyKey);

    Task<long> ReserveNextCounterValueAsync(NumberingCounterScope scope, CancellationToken cancellationToken);
}

public sealed class NumberingConcurrencyException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed class NumberingServiceCore(
    INumberingStore? store = null,
    TimeProvider? timeProvider = null,
    NumberingServiceOptions? options = null)
{
    private readonly INumberingStore? _store = store;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly NumberingServiceOptions _options = options ?? NumberingServiceOptions.Default;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, long> _counters = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, NumberingIdempotencyKey> _idempotency = new(StringComparer.Ordinal);

    public async Task<NumberingAllocation> AllocateAsync(NumberingAllocationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedRequestedNumber = Normalize(request.RequestedNumber);
        var normalizedIdempotencyKey = Normalize(request.IdempotencyKey);
        var idempotencyRecord = normalizedIdempotencyKey is null
            ? null
            : await FindIdempotencyRecordAsync(request, normalizedIdempotencyKey, cancellationToken);
        if (idempotencyRecord is not null)
        {
            if (!string.Equals(idempotencyRecord.PayloadFingerprint, request.PayloadFingerprint, StringComparison.Ordinal))
            {
                throw new KnownException($"Idempotency key '{normalizedIdempotencyKey}' conflicts with a different {request.ConflictResourceLabel} create payload.");
            }

            return new NumberingAllocation(idempotencyRecord.Number, true);
        }

        var number = normalizedRequestedNumber ?? await NextNumberAsync(request, cancellationToken);
        if (normalizedIdempotencyKey is not null)
        {
            AddIdempotencyRecord(new NumberingIdempotencyKey(
                request.OrganizationId,
                request.EnvironmentId,
                request.DocumentType,
                normalizedIdempotencyKey,
                number,
                request.PayloadFingerprint,
                _timeProvider.GetUtcNow()));
        }

        return new NumberingAllocation(number, false);
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

    private async Task<string> NextNumberAsync(NumberingAllocationRequest request, CancellationToken cancellationToken)
    {
        var dateSegment = _timeProvider.GetUtcNow().ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var scope = new NumberingCounterScope(
            request.OrganizationId,
            request.EnvironmentId,
            request.DocumentType,
            request.SiteCode,
            dateSegment,
            request.Prefix);
        var next = _store is null
            ? ReserveNextInMemory(scope)
            : await ReserveNextWithRetryAsync(scope, cancellationToken);

        return $"{request.Prefix}-{dateSegment}-{next:000000}";
    }

    private async Task<long> ReserveNextWithRetryAsync(NumberingCounterScope scope, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await _store!.ReserveNextCounterValueAsync(scope, cancellationToken);
            }
            catch (NumberingConcurrencyException) when (attempt < _options.MaxConcurrencyRetries)
            {
                await Task.Delay(_options.RetryBackoff(attempt), cancellationToken);
            }
        }
    }

    private long ReserveNextInMemory(NumberingCounterScope scope)
    {
        var key = Key(scope.OrganizationId, scope.EnvironmentId, scope.DocumentType, scope.SiteCode, scope.DateSegment);
        lock (_lock)
        {
            _counters.TryGetValue(key, out var current);
            var next = current + 1;
            _counters[key] = next;
            return next;
        }
    }

    private Task<NumberingIdempotencyKey?> FindIdempotencyRecordAsync(
        NumberingAllocationRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (_store is not null)
        {
            return _store.FindIdempotencyRecordAsync(
                request.OrganizationId,
                request.EnvironmentId,
                request.DocumentType,
                idempotencyKey,
                cancellationToken);
        }

        _idempotency.TryGetValue(Key(request.OrganizationId, request.EnvironmentId, request.DocumentType, idempotencyKey), out var record);
        return Task.FromResult<NumberingIdempotencyKey?>(record);
    }

    private void AddIdempotencyRecord(NumberingIdempotencyKey idempotencyKey)
    {
        if (_store is not null)
        {
            _store.AddIdempotencyRecord(idempotencyKey);
            return;
        }

        _idempotency.TryAdd(
            Key(idempotencyKey.OrganizationId, idempotencyKey.EnvironmentId, idempotencyKey.DocumentType, idempotencyKey.IdempotencyKey),
            idempotencyKey);
    }

    private static string Key(params string[] parts)
    {
        return string.Join('|', parts.Select(part => part.Trim().ToLowerInvariant()));
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

public sealed record NumberingServiceOptions(int MaxConcurrencyRetries, Func<int, TimeSpan> RetryBackoff)
{
    public static NumberingServiceOptions Default { get; } = new(5, attempt => TimeSpan.FromMilliseconds(attempt * 10));
}
