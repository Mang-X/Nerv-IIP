using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.ScheduleOperationOverrideAggregate;

namespace Nerv.IIP.Business.Scheduling.Domain.Tests;

public sealed class ScheduleOperationOverrideTests
{
    [Fact]
    public void Mes_clear_rejects_stale_dispatch_and_allows_newer_redispatch()
    {
        var fact = ScheduleOperationOverride.Create(
            "org-1", "env-1", "WO-1", "OP-1", 10, "DEV-1", "WC-1",
            At(8), At(9), "mes-manual-dispatch", "mes-dispatch", "evt-create",
            "user:planner", At(1), At(1));

        Assert.True(fact.TryClearMesDispatch(2, "evt-clear", "user:planner",
            At(2), "device-cleared", At(2)));
        Assert.False(fact.IsActive);
        Assert.Equal(2, fact.SourceRevision);
        Assert.Equal("device-cleared", fact.ClearedReasonCode);
        Assert.Equal(At(2), fact.ClearedAtUtc);
        Assert.Equal("DEV-1", fact.ResourceId);
        Assert.Equal(At(8), fact.StartUtc);
        Assert.False(fact.TryApplyMesDispatch("DEV-OLD", "WC-1", At(8), At(9),
            "evt-old", "user:planner", 1, At(1), At(3)));
        Assert.True(fact.TryApplyMesDispatch("DEV-NEW", "WC-1", At(10), At(11),
            "evt-new", "user:planner", 3, At(2), At(4)));
        Assert.True(fact.IsActive);
        Assert.Equal(3, fact.SourceRevision);
        Assert.Null(fact.ClearedReasonCode);
        Assert.Null(fact.ClearedAtUtc);
    }

    [Fact]
    public void Mes_clear_does_not_deactivate_a_scheduling_api_override()
    {
        var fact = ScheduleOperationOverride.Create(
            "org-1", "env-1", "WO-1", "OP-1", 10, "DEV-MANUAL", "WC-1",
            At(8), At(9), "manual-override", "scheduling-api", null,
            "user:planner", At(1), At(1));

        var cleared = fact.TryClearMesDispatch(
            2, "evt-clear", "user:planner", At(2), "device-cleared", At(2));

        Assert.False(cleared);
        Assert.True(fact.IsActive);
        Assert.Equal("DEV-MANUAL", fact.ResourceId);
        Assert.Null(fact.SourceRevision);
    }

    [Fact]
    public void Equal_source_timestamps_converge_by_mes_revision()
    {
        var fact = ScheduleOperationOverride.CreateClearedMesDispatch(
            "org-1", "env-1", "WO-1", "OP-1", 10, "DEV-1", "WC-1",
            At(8), At(9), "evt-clear", "user:planner", 2, At(2),
            "device-cleared", At(2));

        Assert.False(fact.IsActive);
        Assert.False(fact.TryApplyMesDispatch("DEV-OLD", "WC-1", At(8), At(9),
            "evt-old", "user:planner", 1, At(2), At(3)));
        Assert.True(fact.TryApplyMesDispatch("DEV-NEW", "WC-1", At(10), At(11),
            "evt-new", "user:planner", 3, At(2), At(4)));
        Assert.True(fact.IsActive);
        Assert.Equal("DEV-NEW", fact.ResourceId);
        Assert.Equal(3, fact.SourceRevision);
    }

    [Fact]
    public void Legacy_revision_zero_cannot_replace_a_positive_mes_revision_even_with_a_later_timestamp()
    {
        var fact = ScheduleOperationOverride.CreateClearedMesDispatch(
            "org-1", "env-1", "WO-1", "OP-1", 10, "DEV-1", "WC-1",
            At(8), At(9), "evt-clear", "user:planner", 2, At(2),
            "device-cleared", At(2));

        var applied = fact.TryApplyMesDispatch(
            "DEV-LEGACY", "WC-1", At(10), At(11), "evt-legacy", "user:legacy",
            0, At(7), At(7));

        Assert.False(applied);
        Assert.False(fact.IsActive);
        Assert.Equal(2, fact.SourceRevision);
        Assert.Equal("DEV-1", fact.ResourceId);
        Assert.Equal("evt-clear", fact.SourceEventId);
    }

    [Fact]
    public void Replace_manually_reactivates_and_resets_mes_revocation_metadata()
    {
        var fact = ScheduleOperationOverride.CreateClearedMesDispatch(
            "org-1", "env-1", "WO-1", "OP-1", 10, "DEV-1", "WC-1",
            At(8), At(9), "evt-clear", "user:planner", 2, At(2),
            "operation-cancelled", At(2));

        fact.ReplaceManually("DEV-MANUAL", "WC-2", At(12), At(13), "user:scheduler", At(3));

        Assert.True(fact.IsActive);
        Assert.Equal("scheduling-api", fact.SourceType);
        Assert.Null(fact.SourceRevision);
        Assert.Null(fact.ClearedReasonCode);
        Assert.Null(fact.ClearedAtUtc);
    }

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

    private static DateTimeOffset At(int hour) =>
        new(2026, 7, 14, hour, 0, 0, TimeSpan.Zero);
}
