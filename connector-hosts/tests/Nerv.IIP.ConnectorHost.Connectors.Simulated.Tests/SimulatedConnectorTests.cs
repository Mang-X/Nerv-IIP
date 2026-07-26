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
