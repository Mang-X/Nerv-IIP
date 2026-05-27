using System.Collections.Concurrent;
using System.Globalization;

namespace Nerv.IIP.Business.DemandPlanning.Web.Application.Commands;

public sealed record DemandPlanningNumberAllocation(string Number, bool IsIdempotentReplay);

public sealed class DemandPlanningNumberingService
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, long> _counters = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DemandPlanningIdempotentNumber> _idempotency = new(StringComparer.Ordinal);

    public DemandPlanningNumberAllocation AllocateDemandReference(
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
