using System.Collections.Concurrent;

namespace Nerv.IIP.Web;

public sealed class DownstreamHealthState(TimeProvider timeProvider)
{
    private static readonly TimeSpan DegradedWindow = TimeSpan.FromSeconds(30);
    private readonly ConcurrentDictionary<string, DownstreamHealthEntry> _entries = new(StringComparer.Ordinal);

    public void RecordSuccess(string downstream)
    {
        _entries.AddOrUpdate(
            downstream,
            static key => DownstreamHealthEntry.Available(key),
            static (_, existing) => existing with { Status = "available", Reason = null, DegradedUntilUtc = null });
    }

    public void RecordFailure(string downstream, string reason)
    {
        var now = timeProvider.GetUtcNow();
        _entries[downstream] = new DownstreamHealthEntry(
            downstream,
            "degraded",
            reason,
            now,
            now.Add(DegradedWindow));
    }

    public IReadOnlyCollection<DownstreamHealthEntry> Snapshot()
    {
        var now = timeProvider.GetUtcNow();
        return _entries.Values
            .Select(entry => entry.DegradedUntilUtc is not null && entry.DegradedUntilUtc <= now
                ? entry with { Status = "available", Reason = null, DegradedUntilUtc = null }
                : entry)
            .OrderBy(entry => entry.Downstream, StringComparer.Ordinal)
            .ToArray();
    }
}

public sealed record DownstreamHealthEntry(
    string Downstream,
    string Status,
    string? Reason,
    DateTimeOffset? LastFailureAtUtc,
    DateTimeOffset? DegradedUntilUtc)
{
    public static DownstreamHealthEntry Available(string downstream) =>
        new(downstream, "available", null, null, null);
}
