using System.Collections.Concurrent;

namespace Nerv.IIP.PlatformGateway.Web.Application.Resilience;

public sealed class GatewayDownstreamHealthState
{
    private static readonly TimeSpan DegradedWindow = TimeSpan.FromSeconds(30);
    private readonly ConcurrentDictionary<string, GatewayDownstreamHealthEntry> _entries = new(StringComparer.Ordinal);

    public void RecordSuccess(string downstream)
    {
        _entries.AddOrUpdate(
            downstream,
            static key => GatewayDownstreamHealthEntry.Available(key),
            static (_, existing) => existing with { Status = "available", Reason = null, DegradedUntilUtc = null });
    }

    public void RecordFailure(string downstream, string reason)
    {
        var now = DateTimeOffset.UtcNow;
        _entries[downstream] = new GatewayDownstreamHealthEntry(
            downstream,
            "degraded",
            reason,
            now,
            now.Add(DegradedWindow));
    }

    public IReadOnlyCollection<GatewayDownstreamHealthEntry> Snapshot()
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

public sealed record GatewayDownstreamHealthEntry(
    string Downstream,
    string Status,
    string? Reason,
    DateTimeOffset? LastFailureAtUtc,
    DateTimeOffset? DegradedUntilUtc)
{
    public static GatewayDownstreamHealthEntry Available(string downstream) =>
        new(downstream, "available", null, null, null);
}
