using System.Collections.Concurrent;
using System.Globalization;

namespace Nerv.IIP.Business.Mes.Web.Application.Commands.WorkOrders;

public sealed record MesNumberAllocation(string Number, bool IsIdempotentReplay);

public sealed class MesNumberingService
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, long> _counters = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, MesIdempotentNumber> _idempotency = new(StringComparer.Ordinal);

    public MesNumberAllocation AllocateWorkOrderId(
        string organizationId,
        string environmentId,
        string? requestedWorkOrderId,
        string? idempotencyKey,
        string payloadFingerprint)
    {
        return Allocate(organizationId, environmentId, "work-order", "WO", requestedWorkOrderId, idempotencyKey, payloadFingerprint);
    }

    private MesNumberAllocation Allocate(
        string organizationId,
        string environmentId,
        string documentType,
        string prefix,
        string? requestedNumber,
        string? idempotencyKey,
        string payloadFingerprint)
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

        var number = normalizedRequestedNumber ?? NextNumber(organizationId, environmentId, documentType, prefix);
        if (normalizedIdempotencyKey is not null)
        {
            _idempotency.TryAdd(
                Key(organizationId, environmentId, documentType, normalizedIdempotencyKey),
                new MesIdempotentNumber(number, payloadFingerprint));
        }

        return new MesNumberAllocation(number, false);
    }

    private string NextNumber(string organizationId, string environmentId, string documentType, string prefix)
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
