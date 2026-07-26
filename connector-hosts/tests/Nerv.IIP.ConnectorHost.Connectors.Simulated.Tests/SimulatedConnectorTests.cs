namespace Nerv.IIP.ConnectorHost.Connectors.Simulated.Tests;

public sealed class SimulatedConnectorTests
{
    [Fact]
    public void Compact_configuration_expands_to_the_exact_world_device_and_tag_shape()
    {
        var options = SimulatedTestConfiguration.Bind();

        Assert.Equal(
            ["CONN-OPCUA-01", "CONN-MQTT-01", "CONN-MODBUS-01"],
            options.Connectors.Select(connector => connector.ConnectorId));
        Assert.Equal(46, options.Connectors.SelectMany(connector => connector.Devices).Count());
        Assert.Equal(96, options.Connectors.SelectMany(connector => connector.Devices).SelectMany(device => device.Tags).Count());
        Assert.Equal(
            46,
            options.Connectors.SelectMany(connector => connector.Devices).Select(device => device.DeviceAssetId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            96,
            options.Connectors.SelectMany(connector => connector.Devices).SelectMany(device => device.Tags)
                .Select(tag => $"{tag.DeviceAssetId}/{tag.TagKey}")
                .Distinct(StringComparer.Ordinal)
                .Count());
    }

    [Fact]
    public void Configuration_rejects_duplicate_connector_identity()
    {
        AssertInvalid(values => values["Simulated:Connectors:1:ConnectorId"] = "CONN-OPCUA-01", "connector");
    }

    [Fact]
    public void Configuration_rejects_duplicate_device_identity()
    {
        AssertInvalid(
            values => values["Simulated:Connectors:0:DeviceGroups:1:Prefix"] = "DEV-CNC-",
            "device");
    }

    [Fact]
    public void Configuration_rejects_duplicate_tag_identity()
    {
        AssertInvalid(
            values => values["Simulated:Connectors:0:DeviceGroups:0:Tags:1:TagKey"] = "spindle-temperature",
            "tag");
    }

    [Theory]
    [InlineData("Simulated:Phases:Normal")]
    [InlineData("Simulated:Phases:Degrading")]
    [InlineData("Simulated:Phases:Alarm")]
    [InlineData("Simulated:Phases:Recovered")]
    public void Configuration_rejects_a_missing_phase_duration(string key)
    {
        AssertInvalid(values => values.Remove(key), "phase");
    }

    [Theory]
    [InlineData("Simulated:SampleIntervalMilliseconds")]
    [InlineData("Simulated:MaxDeliveryAttempts")]
    [InlineData("Simulated:RetryBaseMilliseconds")]
    [InlineData("Simulated:MaxPendingSamples")]
    [InlineData("Simulated:CommandReceiptCacheCapacity")]
    public void Configuration_rejects_non_positive_capacity_or_timing(string key)
    {
        AssertInvalid(values => values[key] = "0", "greater than zero");
    }

    [Theory]
    [InlineData("Simulated:MaxDeliveryAttempts", "65")]
    [InlineData("Simulated:RetryBaseMilliseconds", "86400001")]
    public void Configuration_rejects_delivery_values_above_operational_bounds(
        string key,
        string value)
    {
        AssertInvalid(values => values[key] = value, "must not exceed");
    }

    [Fact]
    public void Maximum_delivery_values_saturate_retry_time_without_overflow()
    {
        var options = SimulatedTestConfiguration.Bind(mutate: values =>
        {
            values["Simulated:MaxDeliveryAttempts"] = "64";
            values["Simulated:RetryBaseMilliseconds"] = "86400000";
        });
        var outbox = new SimulatedSampleOutbox(
            capacity: 1,
            options.MaxDeliveryAttempts,
            TimeSpan.FromMilliseconds(options.RetryBaseMilliseconds));
        var request = new Nerv.IIP.ConnectorHost.Connectors.Abstractions.RecordIndustrialTelemetrySampleRequest(
            "org-001",
            "env-dev",
            "DEV-CNC-01",
            "vibration",
            DateTimeOffset.Parse("2026-07-26T00:00:00Z"),
            DateTimeOffset.Parse("2026-07-26T00:00:02Z"),
            1,
            2.5m,
            2.5m,
            2.5m,
            "simulated:CONN-OPCUA-01:DEV-CNC-01:vibration:0",
            "opcua",
            "connector-host-001/CONN-OPCUA-01");
        var nearMaximum = DateTimeOffset.MaxValue.AddHours(-1);

        outbox.Enqueue(request, nearMaximum);

        Assert.False(outbox.MarkFailed(request.SourceSequence, nearMaximum));
        Assert.Empty(outbox.GetDue(nearMaximum));
    }

    [Fact]
    public void Configuration_rejects_invalid_normal_and_writable_ranges()
    {
        AssertInvalid(
            values => values["Simulated:Connectors:0:DeviceGroups:0:Tags:0:NormalMaximum"] = "49",
            "range");
        AssertInvalid(
            values => values["Simulated:Connectors:0:DeviceGroups:0:Tags:0:WritableMaximum"] = "40",
            "range");
    }

    [Theory]
    [InlineData("Password")]
    [InlineData("ClientSecret")]
    [InlineData("CredentialReference")]
    [InlineData("AccessToken")]
    public void Configuration_rejects_secret_or_credential_fields(string field)
    {
        AssertInvalid(values => values[$"Simulated:{field}"] = "must-not-be-here", "secret");
    }

    private static void AssertInvalid(Action<Dictionary<string, string?>> mutation, string messageFragment)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => SimulatedTestConfiguration.Bind(mutate: mutation));
        Assert.Contains(messageFragment, exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
