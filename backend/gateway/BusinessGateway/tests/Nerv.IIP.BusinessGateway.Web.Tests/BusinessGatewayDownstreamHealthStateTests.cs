using Microsoft.Extensions.Time.Testing;
using Nerv.IIP.BusinessGateway.Web.Application.Resilience;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessGatewayDownstreamHealthStateTests
{
    private static readonly DateTimeOffset FailureAtUtc = DateTimeOffset.Parse("2026-08-30T00:00:00Z");

    [Fact]
    public void Failure_is_degraded_before_thirty_seconds_and_available_at_the_boundary()
    {
        var clock = new FakeTimeProvider(FailureAtUtc);
        var state = new BusinessGatewayDownstreamHealthState(clock);

        state.RecordFailure("Notification", "notification unavailable");

        clock.Advance(TimeSpan.FromSeconds(30) - TimeSpan.FromTicks(1));
        var beforeBoundary = Assert.Single(state.Snapshot());
        Assert.Equal("degraded", beforeBoundary.Status);
        Assert.Equal("notification unavailable", beforeBoundary.Reason);
        Assert.Equal(FailureAtUtc, beforeBoundary.LastFailureAtUtc);
        Assert.Equal(FailureAtUtc.AddSeconds(30), beforeBoundary.DegradedUntilUtc);

        clock.Advance(TimeSpan.FromTicks(1));
        var atBoundary = Assert.Single(state.Snapshot());
        Assert.Equal("available", atBoundary.Status);
        Assert.Null(atBoundary.Reason);
        Assert.Equal(FailureAtUtc, atBoundary.LastFailureAtUtc);
        Assert.Null(atBoundary.DegradedUntilUtc);
    }

    [Fact]
    public void Record_success_restores_availability_immediately()
    {
        var clock = new FakeTimeProvider(FailureAtUtc);
        var state = new BusinessGatewayDownstreamHealthState(clock);
        state.RecordFailure("Notification", "notification unavailable");

        state.RecordSuccess("Notification");

        var entry = Assert.Single(state.Snapshot());
        Assert.Equal("available", entry.Status);
        Assert.Null(entry.Reason);
        Assert.Equal(FailureAtUtc, entry.LastFailureAtUtc);
        Assert.Null(entry.DegradedUntilUtc);
    }
}
