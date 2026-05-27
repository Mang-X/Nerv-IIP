using System.Collections.Concurrent;
using System.Globalization;

namespace Nerv.IIP.Business.MasterData.Web.Application.Commands.MasterData;

public sealed record MasterDataNumberAllocation(string Code, bool IsIdempotentReplay);

public sealed class MasterDataNumberingService
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, long> _counters = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, MasterDataIdempotentNumber> _idempotency = new(StringComparer.Ordinal);

    public MasterDataNumberAllocation AllocateSkuCode(
        string organizationId,
        string environmentId,
        string? requestedCode,
        string? idempotencyKey,
        string payloadFingerprint)
    {
        return Allocate(organizationId, environmentId, "sku", "SKU", requestedCode, idempotencyKey, payloadFingerprint);
    }

    private MasterDataNumberAllocation Allocate(
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
                throw new KnownException($"Idempotency key '{normalizedIdempotencyKey}' conflicts with a different SKU create payload.");
            }

            return new MasterDataNumberAllocation(existing.Code, true);
        }

        var code = normalizedRequestedCode ?? NextNumber(organizationId, environmentId, documentType, prefix);
        if (normalizedIdempotencyKey is not null)
        {
            _idempotency.TryAdd(
                Key(organizationId, environmentId, documentType, normalizedIdempotencyKey),
                new MasterDataIdempotentNumber(code, payloadFingerprint));
        }

        return new MasterDataNumberAllocation(code, false);
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

    private sealed record MasterDataIdempotentNumber(string Code, string PayloadFingerprint);
}
