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

    private static ApplicationInstance CreateInstance() => new("org", "env", "host", "app", "1", "node", "opc-main", "Instance", new Dictionary<string, string>(), []);
}
