using Microsoft.Extensions.Configuration;

namespace Nerv.IIP.ConnectorHost.Connectors.Simulated.Tests;

public sealed class SimulatedScenarioEvaluatorTests
{
    [Fact]
    public void Controlled_clock_enters_each_phase_at_the_exact_configured_boundary()
    {
        var options = SimulatedTestConfiguration.Bind();
        var evaluator = new SimulatedScenarioEvaluator(options);
        var tag = options.Connectors
            .Single(connector => connector.ConnectorId == "CONN-OPCUA-01")
            .Devices.Single(device => device.DeviceAssetId == "DEV-CNC-01")
            .Tags.Single(point => point.TagKey == "vibration");
        var epoch = DateTimeOffset.Parse("2026-07-26T00:00:00Z");

        Assert.Equal("normal", evaluator.Evaluate(tag, epoch, 7).Phase);
        Assert.Equal("degrading", evaluator.Evaluate(tag, epoch.AddMinutes(15), 7).Phase);
        Assert.Equal("alarm", evaluator.Evaluate(tag, epoch.AddMinutes(25), 7).Phase);
        Assert.Equal("recovered", evaluator.Evaluate(tag, epoch.AddMinutes(35), 7).Phase);
        Assert.Equal("normal", evaluator.Evaluate(tag, epoch.AddMinutes(45), 7).Phase);
    }

    [Fact]
    public void Fixed_seed_identity_and_cycle_produce_a_literal_value_and_source_sequence()
    {
        var options = SimulatedTestConfiguration.Bind();
        var evaluator = new SimulatedScenarioEvaluator(options);
        var tag = options.Connectors
            .Single(connector => connector.ConnectorId == "CONN-OPCUA-01")
            .Devices.Single(device => device.DeviceAssetId == "DEV-CNC-03")
            .Tags.Single(point => point.TagKey == "vibration");

        var sample = evaluator.Evaluate(tag, DateTimeOffset.Parse("2026-07-26T00:00:00Z"), 7);

        Assert.Equal("normal", sample.Phase);
        Assert.Equal(3.0348m, sample.Value);
        Assert.Equal("simulated:CONN-OPCUA-01:DEV-CNC-03:vibration:7", sample.SourceSequence);
    }

    [Fact]
    public void Device_reordering_and_one_point_override_do_not_perturb_other_identity_streams()
    {
        var baselineOptions = SimulatedTestConfiguration.Bind();
        var reorderedOptions = SimulatedTestConfiguration.Bind(reorderGroups: true);
        var overriddenOptions = SimulatedTestConfiguration.Bind(overrideCnc03Vibration: true);
        var baseline = EvaluateAll(baselineOptions, cycle: 19);
        var reordered = EvaluateAll(reorderedOptions, cycle: 19);
        var overridden = EvaluateAll(overriddenOptions, cycle: 19);

        Assert.Equal(baseline, reordered);
        Assert.NotEqual(baseline["DEV-CNC-03/vibration"], overridden["DEV-CNC-03/vibration"]);
        foreach (var identity in baseline.Keys.Where(identity => identity != "DEV-CNC-03/vibration"))
        {
            Assert.Equal(baseline[identity], overridden[identity]);
        }
    }

    private static IReadOnlyDictionary<string, (decimal Value, string SourceSequence)> EvaluateAll(
        SimulatedConnectorOptions options,
        long cycle)
    {
        var evaluator = new SimulatedScenarioEvaluator(options);
        return options.Connectors
            .SelectMany(connector => connector.Devices)
            .SelectMany(device => device.Tags)
            .ToDictionary(
                tag => $"{tag.DeviceAssetId}/{tag.TagKey}",
                tag =>
                {
                    var sample = evaluator.Evaluate(tag, DateTimeOffset.Parse("2026-07-26T00:00:00Z"), cycle);
                    return (sample.Value, sample.SourceSequence);
                },
                StringComparer.Ordinal);
    }
}

internal static class SimulatedTestConfiguration
{
    public static SimulatedConnectorOptions Bind(
        bool reorderGroups = false,
        bool overrideCnc03Vibration = false,
        Action<Dictionary<string, string?>>? mutate = null)
    {
        var values = Values(reorderGroups, overrideCnc03Vibration);
        mutate?.Invoke(values);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return SimulatedConnectorOptions.Bind(configuration.GetSection("Simulated"));
    }

    public static Dictionary<string, string?> Values(
        bool reorderGroups = false,
        bool overrideCnc03Vibration = false)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Simulated:Enabled"] = "true",
            ["Simulated:Seed"] = "603",
            ["Simulated:EpochUtc"] = "2026-07-26T00:00:00Z",
            ["Simulated:SampleIntervalMilliseconds"] = "2000",
            ["Simulated:MaxDeliveryAttempts"] = "3",
            ["Simulated:RetryBaseMilliseconds"] = "100",
            ["Simulated:MaxPendingSamples"] = "128",
            ["Simulated:CommandReceiptCacheCapacity"] = "16",
            ["Simulated:Phases:Normal"] = "00:15:00",
            ["Simulated:Phases:Degrading"] = "00:10:00",
            ["Simulated:Phases:Alarm"] = "00:10:00",
            ["Simulated:Phases:Recovered"] = "00:10:00",
        };

        var connectors = new[]
        {
            new ConnectorFixture("CONN-OPCUA-01", "opcua", "机加车间 OPC UA 网关",
            [
                new("DEV-CNC-", 10,
                [
                    new("spindle-temperature", "degC", 50m, 62m, 80m, false),
                    new("vibration", "mm/s", 2.3m, 3.8m, 7.2m, false),
                    new("spindle-speed", "rpm", 2200m, 3000m, 3800m, true),
                ]),
                new("DEV-GRD-", 4,
                [
                    new("vibration", "mm/s", 2.1m, 3.5m, 6.2m, false),
                    new("wheel-speed", "rpm", 1400m, 1700m, 2050m, true),
                ]),
                new("DEV-WLD-", 3,
                [
                    new("weld-current", "A", 175m, 230m, 300m, true),
                    new("temperature", "degC", 52m, 67m, 92m, false),
                ]),
            ]),
            new ConnectorFixture("CONN-MQTT-01", "mqtt", "装配/检测 MQTT 网关",
            [
                new("DEV-ASM-", 12,
                [
                    new("press-force", "kN", 11m, 15m, 19m, true),
                    new("cycle-count", "count", 28m, 34m, 40m, false),
                ]),
                new("DEV-TST-", 4,
                [
                    new("damping-force", "N", 900m, 1165m, 1500m, true),
                ]),
            ]),
            new ConnectorFixture("CONN-MODBUS-01", "modbus", "辅助设备 Modbus 网关",
            [
                new("DEV-CTG-", 3,
                [
                    new("bath-temperature", "degC", 27m, 31.6m, 36m, true),
                    new("bath-ph", "pH", 6.05m, 6.55m, 5.3m, true),
                ]),
                new("DEV-PKG-", 2,
                [
                    new("cycle-count", "count", 50m, 65m, 75m, false),
                ]),
                new("DEV-AUX-", 8,
                [
                    new("air-pressure", "bar", 6.75m, 7.7m, 5.7m, true),
                    new("temperature", "degC", 62m, 75m, 98m, false),
                ]),
            ]),
        };

        for (var connectorIndex = 0; connectorIndex < connectors.Length; connectorIndex++)
        {
            var connector = connectors[connectorIndex];
            var connectorPath = $"Simulated:Connectors:{connectorIndex}";
            values[$"{connectorPath}:ConnectorId"] = connector.ConnectorId;
            values[$"{connectorPath}:SourceSystem"] = connector.SourceSystem;
            values[$"{connectorPath}:DisplayName"] = connector.DisplayName;
            values[$"{connectorPath}:Protocol"] = connector.SourceSystem;

            var groups = reorderGroups ? connector.Groups.Reverse().ToArray() : connector.Groups;
            for (var groupIndex = 0; groupIndex < groups.Length; groupIndex++)
            {
                var group = groups[groupIndex];
                var groupPath = $"{connectorPath}:DeviceGroups:{groupIndex}";
                values[$"{groupPath}:Prefix"] = group.Prefix;
                values[$"{groupPath}:Count"] = group.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                values[$"{groupPath}:StartingOrdinal"] = "1";
                for (var tagIndex = 0; tagIndex < group.Tags.Length; tagIndex++)
                {
                    var tag = group.Tags[tagIndex];
                    var tagPath = $"{groupPath}:Tags:{tagIndex}";
                    values[$"{tagPath}:TagKey"] = tag.TagKey;
                    values[$"{tagPath}:Unit"] = tag.Unit;
                    values[$"{tagPath}:NormalMinimum"] = tag.NormalMinimum.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    values[$"{tagPath}:NormalMaximum"] = tag.NormalMaximum.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    values[$"{tagPath}:AlarmValue"] = tag.AlarmValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    values[$"{tagPath}:Writable"] = tag.Writable ? "true" : "false";
                    values[$"{tagPath}:WritableMinimum"] = Math.Min(tag.NormalMinimum, tag.AlarmValue).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    values[$"{tagPath}:WritableMaximum"] = Math.Max(tag.NormalMaximum, tag.AlarmValue).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    values[$"{tagPath}:ProtocolAddressTemplate"] = $"{connector.SourceSystem}://{{deviceId}}/{tag.TagKey}";
                }
            }
        }

        if (overrideCnc03Vibration)
        {
            values["Simulated:Connectors:0:Overrides:0:DeviceAssetId"] = "DEV-CNC-03";
            values["Simulated:Connectors:0:Overrides:0:Tags:0:TagKey"] = "vibration";
            values["Simulated:Connectors:0:Overrides:0:Tags:0:NormalMinimum"] = "3.0";
            values["Simulated:Connectors:0:Overrides:0:Tags:0:NormalMaximum"] = "4.0";
        }

        return values;
    }

    private sealed record ConnectorFixture(
        string ConnectorId,
        string SourceSystem,
        string DisplayName,
        DeviceGroupFixture[] Groups);

    private sealed record DeviceGroupFixture(
        string Prefix,
        int Count,
        TagFixture[] Tags);

    private sealed record TagFixture(
        string TagKey,
        string Unit,
        decimal NormalMinimum,
        decimal NormalMaximum,
        decimal AlarmValue,
        bool Writable);
}
