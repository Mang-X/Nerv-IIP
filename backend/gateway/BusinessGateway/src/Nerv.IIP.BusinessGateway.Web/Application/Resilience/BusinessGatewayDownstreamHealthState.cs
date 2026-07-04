using System.Collections.Concurrent;

namespace Nerv.IIP.BusinessGateway.Web.Application.Resilience;

public sealed class BusinessGatewayDownstreamHealthState
{
    private static readonly TimeSpan DegradedWindow = TimeSpan.FromSeconds(30);
    private readonly ConcurrentDictionary<string, BusinessGatewayDownstreamHealthEntry> _entries = new(StringComparer.Ordinal);

    public void RecordSuccess(string downstream)
    {
        _entries.AddOrUpdate(
            downstream,
            static key => BusinessGatewayDownstreamHealthEntry.Available(key),
            static (_, existing) => existing with { Status = "available", Reason = null, DegradedUntilUtc = null });
    }

    public void RecordFailure(string downstream, string reason)
    {
        var now = DateTimeOffset.UtcNow;
        _entries[downstream] = new BusinessGatewayDownstreamHealthEntry(
            downstream,
            "degraded",
            reason,
            now,
            now.Add(DegradedWindow));
    }

    public IReadOnlyCollection<BusinessGatewayDownstreamHealthEntry> Snapshot()
    {
        var now = DateTimeOffset.UtcNow;
        return _entries.Values
            .Select(entry => entry.DegradedUntilUtc is not null && entry.DegradedUntilUtc <= now
                ? entry with { Status = "available", Reason = null, DegradedUntilUtc = null }
                : entry)
            .OrderBy(entry => entry.Downstream, StringComparer.Ordinal)
            .ToArray();
    }
}

public sealed record BusinessGatewayDownstreamHealthEntry(
    string Downstream,
    string Status,
    string? Reason,
    DateTimeOffset? LastFailureAtUtc,
    DateTimeOffset? DegradedUntilUtc)
{
    public static BusinessGatewayDownstreamHealthEntry Available(string downstream) =>
        new(downstream, "available", null, null, null);
}
