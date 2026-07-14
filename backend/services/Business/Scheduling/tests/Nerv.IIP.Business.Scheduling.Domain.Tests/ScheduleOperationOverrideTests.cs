using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.ScheduleOperationOverrideAggregate;

namespace Nerv.IIP.Business.Scheduling.Domain.Tests;

public sealed class ScheduleOperationOverrideTests
{
    [Fact]
    public void Replace_rejects_an_older_source_fact()
    {
        var start = new DateTimeOffset(2026, 7, 14, 8, 0, 0, TimeSpan.Zero);
        var fact = ScheduleOperationOverride.Create(
            "org-1", "env-1", "WO-1", "OP-1", 10, "DEV-NEW", "WC-1",
            start, start.AddHours(1), "manual-override", "scheduling-api", null,
            "user:planner", start, start);

        var replaced = fact.TryReplace(
            "DEV-OLD", "WC-1", start.AddHours(-1), start,
            "mes-manual-dispatch", "mes-dispatch", "evt-old", "user:old",
            start.AddMinutes(-1), start.AddMinutes(1));

        Assert.False(replaced);
        Assert.Equal("DEV-NEW", fact.ResourceId);
        Assert.Equal(start, fact.StartUtc);
    }

    [Fact]
    public void Replace_accepts_a_newer_source_fact()
    {
        var start = new DateTimeOffset(2026, 7, 14, 8, 0, 0, TimeSpan.Zero);
        var fact = ScheduleOperationOverride.Create(
            "org-1", "env-1", "WO-1", "OP-1", 10, "DEV-OLD", "WC-1",
            start, start.AddHours(1), "manual-override", "scheduling-api", null,
            "user:planner", start, start);

        var replaced = fact.TryReplace(
            "DEV-NEW", "WC-1", start.AddHours(2), start.AddHours(3),
            "mes-manual-dispatch", "mes-dispatch", "evt-new", "user:dispatcher",
            start.AddMinutes(1), start.AddMinutes(1));

        Assert.True(replaced);
        Assert.Equal("DEV-NEW", fact.ResourceId);
        Assert.Equal(start.AddHours(2), fact.StartUtc);
        Assert.Equal("evt-new", fact.SourceEventId);
    }
}
