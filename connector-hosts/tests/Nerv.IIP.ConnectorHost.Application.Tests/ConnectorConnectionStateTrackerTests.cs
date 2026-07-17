using Nerv.IIP.ConnectorHost.Connectors.Abstractions;

namespace Nerv.IIP.ConnectorHost.Application.Tests;

public sealed class ConnectorConnectionStateTrackerTests
{
    [Fact]
    public void Repeated_identical_state_is_coalesced()
    {
        var timeProvider = new MutableTimeProvider();
        var signals = new List<string>();
        var tracker = new ConnectorConnectionStateTracker("connector-a", timeProvider, signals.Add);

        tracker.MarkLost("transport", "socket-closed");
        var first = tracker.Snapshot;
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        tracker.MarkLost("transport", "socket-closed");

        Assert.Same(first, tracker.Snapshot);
        Assert.Equal(["connector-a"], signals);
    }

    [Fact]
    public void Recovery_creates_a_new_connected_since_timestamp()
    {
        var timeProvider = new MutableTimeProvider();
        var tracker = new ConnectorConnectionStateTracker("connector-a", timeProvider, _ => { });
        tracker.MarkConnected();
        var firstConnectedSince = tracker.Snapshot.ConnectedSinceUtc;
        timeProvider.Advance(TimeSpan.FromSeconds(5));
        tracker.MarkLost("transport", "socket-closed");
        timeProvider.Advance(TimeSpan.FromSeconds(5));

        tracker.MarkConnected();

        Assert.NotEqual(firstConnectedSince, tracker.Snapshot.ConnectedSinceUtc);
        Assert.Equal(timeProvider.GetUtcNow(), tracker.Snapshot.ConnectedSinceUtc);
        Assert.Null(tracker.Snapshot.DisconnectedSinceUtc);
    }

    [Fact]
    public void Changed_loss_diagnostic_preserves_disconnected_since_timestamp()
    {
        var timeProvider = new MutableTimeProvider();
        var tracker = new ConnectorConnectionStateTracker("connector-a", timeProvider, _ => { });
        tracker.MarkConnected();
        timeProvider.Advance(TimeSpan.FromSeconds(2));
        tracker.MarkLost("transport", "socket-closed");
        var disconnectedSince = tracker.Snapshot.DisconnectedSinceUtc;
        timeProvider.Advance(TimeSpan.FromSeconds(3));

        tracker.MarkLost("transport", "connection-refused");

        Assert.Equal(disconnectedSince, tracker.Snapshot.DisconnectedSinceUtc);
        Assert.Equal(timeProvider.GetUtcNow(), tracker.Snapshot.ObservedAtUtc);
        Assert.Equal("connection-refused", tracker.Snapshot.DiagnosticCode);
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = DateTimeOffset.Parse("2026-07-17T00:00:00Z");

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan amount) => _utcNow += amount;
    }
}
