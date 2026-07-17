using Nerv.IIP.AppHub.Domain.AggregatesModel.ApplicationInstanceAggregate;
using Nerv.IIP.Contracts.ConnectorProtocol;

namespace Nerv.IIP.AppHub.Domain.Tests;

public sealed class ConnectorCollectionHealthProjectionTests
{
    [Fact]
    public void New_epoch_replaces_counters_without_treating_reset_as_a_decrease()
    {
        var instance = CreateInstance();
        instance.RecordCollectionHealth(new ConnectorCollectionHealth("opc-main", "opcua", Guid.Parse("11111111-1111-1111-1111-111111111111"), DateTimeOffset.Parse("2026-07-13T01:00:00Z"), 100, 4, 2, DateTimeOffset.Parse("2026-07-13T00:59:59Z")));

        instance.RecordCollectionHealth(new ConnectorCollectionHealth("opc-main", "opcua", Guid.Parse("22222222-2222-2222-2222-222222222222"), DateTimeOffset.Parse("2026-07-13T01:05:00Z"), 3, 0, 0, DateTimeOffset.Parse("2026-07-13T01:04:59Z")));

        Assert.Equal(3, instance.CollectionHealth!.ReceivedCount);
        Assert.Equal(Guid.Parse("22222222-2222-2222-2222-222222222222"), instance.CollectionHealth.CounterEpoch);
    }

    [Fact]
    public void Same_epoch_ignores_out_of_order_counter_regression()
    {
        var instance = CreateInstance();
        var epoch = Guid.Parse("11111111-1111-1111-1111-111111111111");
        instance.RecordCollectionHealth(new ConnectorCollectionHealth("opc-main", "opcua", epoch, DateTimeOffset.Parse("2026-07-13T01:05:00Z"), 100, 4, 2, null));

        instance.RecordCollectionHealth(new ConnectorCollectionHealth("opc-main", "opcua", epoch, DateTimeOffset.Parse("2026-07-13T01:00:00Z"), 3, 0, 0, null));

        Assert.Equal(100, instance.CollectionHealth!.ReceivedCount);
    }

    [Fact]
    public void Late_report_from_an_obsolete_epoch_cannot_replace_newer_epoch()
    {
        var instance = CreateInstance();
        var epochA = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var epochB = Guid.Parse("22222222-2222-2222-2222-222222222222");
        instance.RecordCollectionHealth(new ConnectorCollectionHealth("opc-main", "opcua", epochA, DateTimeOffset.Parse("2026-07-13T01:00:00Z"), 100, 4, 2, null));
        instance.RecordCollectionHealth(new ConnectorCollectionHealth("opc-main", "opcua", epochB, DateTimeOffset.Parse("2026-07-13T01:05:00Z"), 3, 0, 0, null));

        instance.RecordCollectionHealth(new ConnectorCollectionHealth("opc-main", "opcua", epochA, DateTimeOffset.Parse("2026-07-13T01:02:00Z"), 120, 5, 3, null));

        Assert.Equal(epochB, instance.CollectionHealth!.CounterEpoch);
        Assert.Equal(3, instance.CollectionHealth.ReceivedCount);
    }

    [Fact]
    public void Retired_epoch_cannot_return_even_with_a_later_reported_time()
    {
        var instance = CreateInstance();
        var epochA = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var epochB = Guid.Parse("22222222-2222-2222-2222-222222222222");
        instance.RecordCollectionHealth(new ConnectorCollectionHealth("opc-main", "opcua", epochA, DateTimeOffset.Parse("2026-07-13T01:00:00Z"), 1, 0, 0, null));
        instance.RecordCollectionHealth(new ConnectorCollectionHealth("opc-main", "opcua", epochB, DateTimeOffset.Parse("2026-07-13T01:05:00Z"), 1, 0, 0, null));

        instance.RecordCollectionHealth(new ConnectorCollectionHealth("opc-main", "opcua", epochA, DateTimeOffset.Parse("2026-07-13T01:10:00Z"), 200, 10, 3, null));

        Assert.Equal(epochB, instance.CollectionHealth!.CounterEpoch);
        Assert.Equal(1, instance.CollectionHealth.ReceivedCount);
    }

    [Fact]
    public void Previously_unseen_epoch_with_an_older_report_cannot_replace_current_epoch()
    {
        var instance = CreateInstance();
        var currentEpoch = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var unseenOldEpoch = Guid.Parse("11111111-1111-1111-1111-111111111111");
        instance.RecordCollectionHealth(new ConnectorCollectionHealth("opc-main", "opcua", currentEpoch, DateTimeOffset.Parse("2026-07-13T01:05:00Z"), 3, 0, 0, null));

        instance.RecordCollectionHealth(new ConnectorCollectionHealth("opc-main", "opcua", unseenOldEpoch, DateTimeOffset.Parse("2026-07-13T01:02:00Z"), 120, 5, 3, null));

        Assert.Equal(currentEpoch, instance.CollectionHealth!.CounterEpoch);
        Assert.Equal(3, instance.CollectionHealth.ReceivedCount);
    }

    [Fact]
    public void Retired_epoch_cannot_return_after_more_than_sixteen_resets()
    {
        var instance = CreateInstance();
        var firstEpoch = Guid.Parse("00000001-0000-0000-0000-000000000000");
        var start = DateTimeOffset.Parse("2026-07-13T01:00:00Z");
        instance.RecordCollectionHealth(new ConnectorCollectionHealth("opc-main", "opcua", firstEpoch, start, 1, 0, 0, null));
        for (var index = 2; index <= 20; index++)
        {
            var epoch = Guid.Parse($"{index:x8}-0000-0000-0000-000000000000");
            instance.RecordCollectionHealth(new ConnectorCollectionHealth("opc-main", "opcua", epoch, start.AddMinutes(index), index, 0, 0, null));
        }

        instance.RecordCollectionHealth(new ConnectorCollectionHealth("opc-main", "opcua", firstEpoch, start.AddHours(2), 999, 9, 9, null));

        Assert.NotEqual(firstEpoch, instance.CollectionHealth!.CounterEpoch);
        Assert.Equal(20, instance.CollectionHealth.ReceivedCount);
    }

    [Fact]
    public void Same_epoch_partial_unknown_report_preserves_known_facts()
    {
        var instance = CreateInstance();
        var epoch = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var sample = DateTimeOffset.Parse("2026-07-13T01:00:00Z");
        instance.RecordCollectionHealth(new ConnectorCollectionHealth("opc-main", "opcua", epoch, sample, 10, 2, 1, sample));

        instance.RecordCollectionHealth(new ConnectorCollectionHealth("opc-main", "opcua", epoch, sample.AddMinutes(1), null, null, null, null));

        Assert.Equal(10, instance.CollectionHealth!.ReceivedCount);
        Assert.Equal(2, instance.CollectionHealth.DroppedCount);
        Assert.Equal(1, instance.CollectionHealth.ErrorCount);
        Assert.Equal(sample, instance.CollectionHealth.LastSampleAtUtc);
        Assert.Equal("opcua", instance.CollectionHealth.SourceSystem);
    }

    [Fact]
    public void New_epoch_unknown_facts_do_not_inherit_previous_epoch_counters()
    {
        var instance = CreateInstance();
        var sample = DateTimeOffset.Parse("2026-07-13T01:00:00Z");
        instance.RecordCollectionHealth(new ConnectorCollectionHealth("opc-main", "opcua", Guid.Parse("11111111-1111-1111-1111-111111111111"), sample, 10, 2, 1, sample));

        instance.RecordCollectionHealth(new ConnectorCollectionHealth("opc-main", "opcua", Guid.Parse("22222222-2222-2222-2222-222222222222"), sample.AddMinutes(1), null, null, null, null));

        Assert.Null(instance.CollectionHealth!.ReceivedCount);
        Assert.Null(instance.CollectionHealth.DroppedCount);
        Assert.Null(instance.CollectionHealth.ErrorCount);
        Assert.Null(instance.CollectionHealth.LastSampleAtUtc);
    }

    [Fact]
    public void Newer_connection_transition_updates_even_when_counter_report_is_stale()
    {
        var instance = CreateInstance();
        var epoch = Guid.Parse("11111111-1111-1111-1111-111111111111");
        instance.RecordCollectionHealth(Health(
            epoch,
            reportedAtUtc: "2026-07-13T01:10:00Z",
            receivedCount: 100,
            connection: Alive("2026-07-13T01:10:00Z", "2026-07-13T01:00:00Z")));

        instance.RecordCollectionHealth(Health(
            epoch,
            reportedAtUtc: "2026-07-13T01:05:00Z",
            receivedCount: 10,
            connection: Lost("2026-07-13T01:11:00Z", "2026-07-13T01:11:00Z")));

        Assert.Equal(100, instance.CollectionHealth!.ReceivedCount);
        Assert.Equal("lost", instance.CollectionHealth.ConnectionStatus);
        Assert.Equal(DateTimeOffset.Parse("2026-07-13T01:11:00Z"), instance.CollectionHealth.ConnectionObservedAtUtc);
    }

    [Fact]
    public void Newer_counter_report_updates_even_when_connection_observation_is_stale()
    {
        var instance = CreateInstance();
        var epoch = Guid.Parse("11111111-1111-1111-1111-111111111111");
        instance.RecordCollectionHealth(Health(
            epoch,
            reportedAtUtc: "2026-07-13T01:10:00Z",
            receivedCount: 100,
            connection: Lost("2026-07-13T01:10:00Z", "2026-07-13T01:10:00Z")));

        instance.RecordCollectionHealth(Health(
            epoch,
            reportedAtUtc: "2026-07-13T01:11:00Z",
            receivedCount: 120,
            connection: Alive("2026-07-13T01:09:00Z", "2026-07-13T01:09:00Z")));

        Assert.Equal(120, instance.CollectionHealth!.ReceivedCount);
        Assert.Equal("lost", instance.CollectionHealth.ConnectionStatus);
        Assert.Equal(DateTimeOffset.Parse("2026-07-13T01:10:00Z"), instance.CollectionHealth.ConnectionObservedAtUtc);
    }

    [Fact]
    public void Stale_connection_observation_cannot_revive_a_newer_loss()
    {
        var instance = CreateInstance();
        var epoch = Guid.Parse("11111111-1111-1111-1111-111111111111");
        instance.RecordCollectionHealth(Health(
            epoch,
            reportedAtUtc: "2026-07-13T01:10:00Z",
            receivedCount: 100,
            connection: Lost("2026-07-13T01:12:00Z", "2026-07-13T01:12:00Z")));

        instance.RecordCollectionHealth(Health(
            epoch,
            reportedAtUtc: "2026-07-13T01:13:00Z",
            receivedCount: 130,
            connection: Alive("2026-07-13T01:11:00Z", "2026-07-13T01:11:00Z")));

        Assert.Equal("lost", instance.CollectionHealth!.ConnectionStatus);
        Assert.Equal(DateTimeOffset.Parse("2026-07-13T01:12:00Z"), instance.CollectionHealth.DisconnectedSinceUtc);
        Assert.Null(instance.CollectionHealth.ConnectedSinceUtc);
    }

    [Fact]
    public void Legacy_null_connection_does_not_clear_known_connection_fact()
    {
        var instance = CreateInstance();
        var epoch = Guid.Parse("11111111-1111-1111-1111-111111111111");
        instance.RecordCollectionHealth(Health(
            epoch,
            reportedAtUtc: "2026-07-13T01:10:00Z",
            receivedCount: 100,
            connection: Alive("2026-07-13T01:10:00Z", "2026-07-13T01:00:00Z")));

        instance.RecordCollectionHealth(Health(
            epoch,
            reportedAtUtc: "2026-07-13T01:11:00Z",
            receivedCount: 120,
            connection: null));

        Assert.Equal(120, instance.CollectionHealth!.ReceivedCount);
        Assert.Equal("alive", instance.CollectionHealth.ConnectionStatus);
        Assert.Equal(DateTimeOffset.Parse("2026-07-13T01:00:00Z"), instance.CollectionHealth.ConnectedSinceUtc);
    }

    [Fact]
    public void Identical_connection_observation_is_idempotent_while_newer_counters_advance()
    {
        var instance = CreateInstance();
        var epoch = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var connection = Lost("2026-07-13T01:10:00Z", "2026-07-13T01:10:00Z");
        instance.RecordCollectionHealth(Health(
            epoch,
            reportedAtUtc: "2026-07-13T01:10:00Z",
            receivedCount: 100,
            connection));

        instance.RecordCollectionHealth(Health(
            epoch,
            reportedAtUtc: "2026-07-13T01:11:00Z",
            receivedCount: 120,
            connection));

        Assert.Equal(120, instance.CollectionHealth!.ReceivedCount);
        Assert.Equal("lost", instance.CollectionHealth.ConnectionStatus);
        Assert.Equal("transport", instance.CollectionHealth.ConnectionReasonCategory);
        Assert.Equal("connection-lost", instance.CollectionHealth.ConnectionDiagnosticCode);
    }

    [Theory]
    [MemberData(nameof(EqualTimestampConflicts))]
    public void Conflicting_connection_observation_at_same_timestamp_is_rejected_before_counters_change(
        ConnectorConnectionState conflictingConnection)
    {
        var instance = CreateInstance();
        var epoch = Guid.Parse("11111111-1111-1111-1111-111111111111");
        instance.RecordCollectionHealth(Health(
            epoch,
            reportedAtUtc: "2026-07-13T01:10:00Z",
            receivedCount: 100,
            Lost("2026-07-13T01:10:00Z", "2026-07-13T01:10:00Z")));

        var exception = Assert.Throws<ArgumentException>(() => instance.RecordCollectionHealth(Health(
            epoch,
            reportedAtUtc: "2026-07-13T01:11:00Z",
            receivedCount: 120,
            conflictingConnection)));

        Assert.Contains("ObservedAtUtc", exception.Message, StringComparison.Ordinal);
        Assert.Equal(100, instance.CollectionHealth!.ReceivedCount);
        Assert.Equal("lost", instance.CollectionHealth.ConnectionStatus);
        Assert.Equal("transport", instance.CollectionHealth.ConnectionReasonCategory);
        Assert.Equal("connection-lost", instance.CollectionHealth.ConnectionDiagnosticCode);
    }

    [Theory]
    [MemberData(nameof(InvalidConnections))]
    public void Invalid_connection_state_is_rejected(ConnectorConnectionState connection)
    {
        var instance = CreateInstance();

        var exception = Assert.Throws<ArgumentException>(() => instance.RecordCollectionHealth(Health(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            reportedAtUtc: "2026-07-13T01:10:00Z",
            receivedCount: 100,
            connection: connection)));

        Assert.Contains("connection", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    public static TheoryData<ConnectorConnectionState> InvalidConnections => new()
    {
        new ConnectorConnectionState("connected", DateTimeOffset.Parse("2026-07-13T01:10:00Z"), DateTimeOffset.Parse("2026-07-13T01:00:00Z")),
        new ConnectorConnectionState("alive", DateTimeOffset.Parse("2026-07-13T01:10:00Z")),
        new ConnectorConnectionState("alive", DateTimeOffset.Parse("2026-07-13T01:10:00Z"), DateTimeOffset.Parse("2026-07-13T01:00:00Z"), DateTimeOffset.Parse("2026-07-13T01:09:00Z")),
        new ConnectorConnectionState("lost", DateTimeOffset.Parse("2026-07-13T01:10:00Z")),
        new ConnectorConnectionState("lost", DateTimeOffset.Parse("2026-07-13T01:10:00Z"), DateTimeOffset.Parse("2026-07-13T01:00:00Z"), DateTimeOffset.Parse("2026-07-13T01:09:00Z")),
        new ConnectorConnectionState("unknown", DateTimeOffset.Parse("2026-07-13T01:10:00Z"), DateTimeOffset.Parse("2026-07-13T01:00:00Z")),
        new ConnectorConnectionState("unknown", DateTimeOffset.Parse("2026-07-13T01:10:00Z"), DisconnectedSinceUtc: DateTimeOffset.Parse("2026-07-13T01:09:00Z")),
    };

    public static TheoryData<ConnectorConnectionState> EqualTimestampConflicts => new()
    {
        new ConnectorConnectionState(
            "alive",
            DateTimeOffset.Parse("2026-07-13T01:10:00Z"),
            ConnectedSinceUtc: DateTimeOffset.Parse("2026-07-13T01:00:00Z")),
        new ConnectorConnectionState(
            "lost",
            DateTimeOffset.Parse("2026-07-13T01:10:00Z"),
            DisconnectedSinceUtc: DateTimeOffset.Parse("2026-07-13T01:09:00Z"),
            ReasonCategory: "transport",
            DiagnosticCode: "connection-lost"),
        new ConnectorConnectionState(
            "lost",
            DateTimeOffset.Parse("2026-07-13T01:10:00Z"),
            DisconnectedSinceUtc: DateTimeOffset.Parse("2026-07-13T01:10:00Z"),
            ReasonCategory: "protocol",
            DiagnosticCode: "connection-lost"),
        new ConnectorConnectionState(
            "lost",
            DateTimeOffset.Parse("2026-07-13T01:10:00Z"),
            DisconnectedSinceUtc: DateTimeOffset.Parse("2026-07-13T01:10:00Z"),
            ReasonCategory: "transport",
            DiagnosticCode: "session-closed"),
    };

    private static ConnectorCollectionHealth Health(
        Guid epoch,
        string reportedAtUtc,
        long? receivedCount,
        ConnectorConnectionState? connection) => new(
            "opc-main",
            "opcua",
            epoch,
            DateTimeOffset.Parse(reportedAtUtc),
            receivedCount,
            0,
            0,
            null,
            connection);

    private static ConnectorConnectionState Alive(string observedAtUtc, string connectedSinceUtc) => new(
        "alive",
        DateTimeOffset.Parse(observedAtUtc),
        DateTimeOffset.Parse(connectedSinceUtc));

    private static ConnectorConnectionState Lost(string observedAtUtc, string disconnectedSinceUtc) => new(
        "lost",
        DateTimeOffset.Parse(observedAtUtc),
        DisconnectedSinceUtc: DateTimeOffset.Parse(disconnectedSinceUtc),
        ReasonCategory: "transport",
        DiagnosticCode: "connection-lost");

    private static ApplicationInstance CreateInstance() => new("org", "env", "host", "app", "1", "node", "opc-main", "Instance", new Dictionary<string, string>(), []);
}
