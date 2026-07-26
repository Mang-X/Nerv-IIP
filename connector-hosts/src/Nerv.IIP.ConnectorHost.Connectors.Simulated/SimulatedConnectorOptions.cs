using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Nerv.IIP.ConnectorHost.Connectors.Simulated;

public sealed class SimulatedConnectorOptions
{
    private const int MaximumDeliveryAttempts = 64;
    private const int MaximumRetryBaseMilliseconds = 86_400_000;
    private static readonly string[] CanonicalConnectorIds =
        ["CONN-OPCUA-01", "CONN-MQTT-01", "CONN-MODBUS-01"];
    private static readonly string[] SensitiveFieldFragments =
        ["password", "secret", "credential", "token", "apikey", "privatekey"];

    private SimulatedConnectorOptions(
        bool enabled,
        int seed,
        DateTimeOffset epochUtc,
        int sampleIntervalMilliseconds,
        int maxDeliveryAttempts,
        int retryBaseMilliseconds,
        int maxPendingSamples,
        int maxPendingStateTransitionsPerDevice,
        int commandReceiptCacheCapacity,
        SimulatedPhaseDurations phases,
        IReadOnlyList<SimulatedConnectorProfile> connectors)
    {
        Enabled = enabled;
        Seed = seed;
        EpochUtc = epochUtc;
        SampleIntervalMilliseconds = sampleIntervalMilliseconds;
        MaxDeliveryAttempts = maxDeliveryAttempts;
        RetryBaseMilliseconds = retryBaseMilliseconds;
        MaxPendingSamples = maxPendingSamples;
        MaxPendingStateTransitionsPerDevice = maxPendingStateTransitionsPerDevice;
        CommandReceiptCacheCapacity = commandReceiptCacheCapacity;
        Phases = phases;
        Connectors = connectors;
    }

    public bool Enabled { get; }
    public int Seed { get; }
    public DateTimeOffset EpochUtc { get; }
    public int SampleIntervalMilliseconds { get; }
    public int MaxDeliveryAttempts { get; }
    public int RetryBaseMilliseconds { get; }
    public int MaxPendingSamples { get; }
    public int MaxPendingStateTransitionsPerDevice { get; }
    public int CommandReceiptCacheCapacity { get; }
    public SimulatedPhaseDurations Phases { get; }
    public IReadOnlyList<SimulatedConnectorProfile> Connectors { get; }

    public static SimulatedConnectorOptions Bind(IConfigurationSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        RejectSensitiveFields(section);

        var phases = new SimulatedPhaseDurations(
            RequiredDuration(section, "Phases:Normal"),
            RequiredDuration(section, "Phases:Degrading"),
            RequiredDuration(section, "Phases:Alarm"),
            RequiredDuration(section, "Phases:Recovered"));
        var connectors = section.GetSection("Connectors").GetChildren()
            .Select(BindConnector)
            .ToArray();
        ValidateConnectorShape(connectors);

        return new SimulatedConnectorOptions(
            section.GetValue("Enabled", false),
            RequiredInt(section, "Seed", allowZero: true),
            RequiredDateTimeOffset(section, "EpochUtc"),
            RequiredPositiveInt(section, "SampleIntervalMilliseconds"),
            RequiredBoundedPositiveInt(
                section,
                "MaxDeliveryAttempts",
                MaximumDeliveryAttempts),
            RequiredBoundedPositiveInt(
                section,
                "RetryBaseMilliseconds",
                MaximumRetryBaseMilliseconds),
            RequiredPositiveInt(section, "MaxPendingSamples"),
            RequiredPositiveInt(section, "MaxPendingStateTransitionsPerDevice"),
            RequiredPositiveInt(section, "CommandReceiptCacheCapacity"),
            phases,
            connectors);
    }

    private static SimulatedConnectorProfile BindConnector(IConfigurationSection section)
    {
        var connectorId = Required(section, "ConnectorId");
        var sourceSystem = Required(section, "SourceSystem").ToLowerInvariant();
        var protocol = Required(section, "Protocol").ToLowerInvariant();
        var displayName = Required(section, "DisplayName");
        var devices = new List<SimulatedDeviceProfile>();
        foreach (var group in section.GetSection("DeviceGroups").GetChildren())
        {
            var prefix = Required(group, "Prefix");
            var count = RequiredPositiveInt(group, "Count");
            var startingOrdinal = group.GetValue("StartingOrdinal", 1);
            if (startingOrdinal <= 0)
            {
                throw new InvalidOperationException("Simulated device starting ordinal must be greater than zero.");
            }

            var tags = group.GetSection("Tags").GetChildren()
                .Select(tag => BindTag(tag, connectorId, sourceSystem, protocol, prefix))
                .ToArray();
            if (tags.Length == 0)
            {
                throw new InvalidOperationException($"Simulated device group '{prefix}' must declare at least one tag.");
            }

            for (var offset = 0; offset < count; offset++)
            {
                var ordinal = startingOrdinal + offset;
                var deviceAssetId = $"{prefix}{ordinal:D2}";
                devices.Add(new SimulatedDeviceProfile(
                    connectorId,
                    sourceSystem,
                    protocol,
                    deviceAssetId,
                    TimeSpan.Zero,
                    tags.Select(tag => tag with
                    {
                        DeviceAssetId = deviceAssetId,
                        ProtocolAddress = tag.ProtocolAddress.Replace(
                            "{deviceId}",
                            deviceAssetId,
                            StringComparison.Ordinal)
                    }).ToArray()));
            }
        }

        ApplyOverrides(section.GetSection("Overrides"), devices);
        return new SimulatedConnectorProfile(connectorId, sourceSystem, protocol, displayName, devices);
    }

    private static SimulatedTagProfile BindTag(
        IConfigurationSection section,
        string connectorId,
        string sourceSystem,
        string protocol,
        string devicePrefix)
    {
        var tagKey = Required(section, "TagKey").ToLowerInvariant();
        var normalMinimum = RequiredDecimal(section, "NormalMinimum");
        var normalMaximum = RequiredDecimal(section, "NormalMaximum");
        var alarmValue = RequiredDecimal(section, "AlarmValue");
        var writable = section.GetValue("Writable", false);
        var writableMinimum = RequiredDecimal(section, "WritableMinimum");
        var writableMaximum = RequiredDecimal(section, "WritableMaximum");
        ValidateRanges(
            $"{devicePrefix}*/{tagKey}",
            normalMinimum,
            normalMaximum,
            writableMinimum,
            writableMaximum,
            alarmValue);
        return new SimulatedTagProfile(
            connectorId,
            sourceSystem,
            protocol,
            string.Empty,
            tagKey,
            Required(section, "Unit"),
            normalMinimum,
            normalMaximum,
            alarmValue,
            writable,
            writableMinimum,
            writableMaximum,
            Required(section, "ProtocolAddressTemplate"),
            TimeSpan.Zero,
            section.GetValue("AlarmScenarioEnabled", false));
    }

    private static void ApplyOverrides(
        IConfigurationSection overridesSection,
        List<SimulatedDeviceProfile> devices)
    {
        foreach (var item in overridesSection.GetChildren())
        {
            var deviceAssetId = Required(item, "DeviceAssetId");
            var deviceIndex = devices.FindIndex(device =>
                string.Equals(device.DeviceAssetId, deviceAssetId, StringComparison.Ordinal));
            if (deviceIndex < 0)
            {
                throw new InvalidOperationException(
                    $"Simulated device override targets unknown device '{deviceAssetId}'.");
            }

            var device = devices[deviceIndex];
            var phaseOffset = OptionalDuration(item, "PhaseOffset") ?? device.PhaseOffset;
            var tags = device.Tags.ToArray();
            foreach (var tagOverride in item.GetSection("Tags").GetChildren())
            {
                var tagKey = Required(tagOverride, "TagKey").ToLowerInvariant();
                var tagIndex = Array.FindIndex(tags, tag =>
                    string.Equals(tag.TagKey, tagKey, StringComparison.Ordinal));
                if (tagIndex < 0)
                {
                    throw new InvalidOperationException(
                        $"Simulated tag override targets unknown tag '{deviceAssetId}/{tagKey}'.");
                }

                var current = tags[tagIndex];
                var updated = current with
                {
                    NormalMinimum = OptionalDecimal(tagOverride, "NormalMinimum") ?? current.NormalMinimum,
                    NormalMaximum = OptionalDecimal(tagOverride, "NormalMaximum") ?? current.NormalMaximum,
                    AlarmValue = OptionalDecimal(tagOverride, "AlarmValue") ?? current.AlarmValue,
                    WritableMinimum = OptionalDecimal(tagOverride, "WritableMinimum") ?? current.WritableMinimum,
                    WritableMaximum = OptionalDecimal(tagOverride, "WritableMaximum") ?? current.WritableMaximum,
                    PhaseOffset = phaseOffset,
                    AlarmScenarioEnabled = tagOverride.GetValue(
                        "AlarmScenarioEnabled",
                        current.AlarmScenarioEnabled)
                };
                ValidateRanges(
                    $"{deviceAssetId}/{tagKey}",
                    updated.NormalMinimum,
                    updated.NormalMaximum,
                    updated.WritableMinimum,
                    updated.WritableMaximum,
                    updated.AlarmValue);
                tags[tagIndex] = updated;
            }

            devices[deviceIndex] = device with { Tags = tags };
        }
    }

    private static void ValidateConnectorShape(IReadOnlyList<SimulatedConnectorProfile> connectors)
    {
        var connectorIds = connectors.Select(connector => connector.ConnectorId).ToArray();
        if (connectorIds.Length != CanonicalConnectorIds.Length
            || !CanonicalConnectorIds.All(required => connectorIds.Contains(required, StringComparer.Ordinal)))
        {
            throw new InvalidOperationException(
                "Simulated connector configuration must contain the three canonical connector identities.");
        }

        if (connectorIds.Distinct(StringComparer.Ordinal).Count() != connectorIds.Length)
        {
            throw new InvalidOperationException("Simulated connector identities must be unique.");
        }

        var devices = connectors.SelectMany(connector => connector.Devices).ToArray();
        if (devices.Select(device => device.DeviceAssetId).Distinct(StringComparer.Ordinal).Count() != devices.Length)
        {
            throw new InvalidOperationException("Simulated device identities must be unique.");
        }

        foreach (var device in devices)
        {
            if (device.Tags.Select(tag => tag.TagKey).Distinct(StringComparer.Ordinal).Count() != device.Tags.Count)
            {
                throw new InvalidOperationException(
                    $"Simulated tag identities must be unique for device '{device.DeviceAssetId}'.");
            }
        }
    }

    private static void ValidateRanges(
        string identity,
        decimal normalMinimum,
        decimal normalMaximum,
        decimal writableMinimum,
        decimal writableMaximum,
        decimal alarmValue)
    {
        if (normalMinimum >= normalMaximum)
        {
            throw new InvalidOperationException(
                $"Simulated normal range is invalid for '{identity}'.");
        }

        if (writableMinimum > writableMaximum
            || normalMinimum < writableMinimum
            || normalMaximum > writableMaximum
            || alarmValue < writableMinimum
            || alarmValue > writableMaximum)
        {
            throw new InvalidOperationException(
                $"Simulated writable range is invalid for '{identity}'.");
        }
    }

    private static void RejectSensitiveFields(IConfigurationSection section)
    {
        foreach (var item in section.AsEnumerable(makePathsRelative: true))
        {
            var fieldName = item.Key.Split(':', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            if (fieldName is not null
                && SensitiveFieldFragments.Any(fragment =>
                    fieldName.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"Simulated configuration must not contain secret or credential field '{fieldName}'.");
            }
        }
    }

    private static string Required(IConfiguration configuration, string key) =>
        string.IsNullOrWhiteSpace(configuration[key])
            ? throw new InvalidOperationException($"Simulated configuration value '{key}' is required.")
            : configuration[key]!.Trim();

    private static int RequiredPositiveInt(IConfiguration configuration, string key) =>
        RequiredInt(configuration, key, allowZero: false);

    private static int RequiredBoundedPositiveInt(
        IConfiguration configuration,
        string key,
        int maximum)
    {
        var value = RequiredPositiveInt(configuration, key);
        if (value > maximum)
        {
            throw new InvalidOperationException(
                $"Simulated configuration value '{key}' must not exceed {maximum}.");
        }

        return value;
    }

    private static int RequiredInt(IConfiguration configuration, string key, bool allowZero)
    {
        if (!int.TryParse(
                Required(configuration, key),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var value)
            || (allowZero ? value < 0 : value <= 0))
        {
            throw new InvalidOperationException(
                $"Simulated configuration value '{key}' must be greater than zero.");
        }

        return value;
    }

    private static decimal RequiredDecimal(IConfiguration configuration, string key) =>
        decimal.TryParse(
            Required(configuration, key),
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : throw new InvalidOperationException(
                $"Simulated configuration value '{key}' must be a decimal.");

    private static decimal? OptionalDecimal(IConfiguration configuration, string key) =>
        string.IsNullOrWhiteSpace(configuration[key])
            ? null
            : RequiredDecimal(configuration, key);

    private static DateTimeOffset RequiredDateTimeOffset(IConfiguration configuration, string key) =>
        DateTimeOffset.TryParse(
            Required(configuration, key),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var value)
            ? value
            : throw new InvalidOperationException(
                $"Simulated configuration value '{key}' must be a UTC timestamp.");

    private static TimeSpan RequiredDuration(IConfiguration configuration, string key)
    {
        var value = OptionalDuration(configuration, key);
        if (!value.HasValue || value.Value <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"Simulated phase duration '{key}' must be present and greater than zero.");
        }

        return value.Value;
    }

    private static TimeSpan? OptionalDuration(IConfiguration configuration, string key) =>
        string.IsNullOrWhiteSpace(configuration[key])
            ? null
            : TimeSpan.TryParse(
                configuration[key],
                CultureInfo.InvariantCulture,
                out var value)
                ? value
                : throw new InvalidOperationException(
                    $"Simulated duration '{key}' is invalid.");
}

public sealed record SimulatedPhaseDurations(
    TimeSpan Normal,
    TimeSpan Degrading,
    TimeSpan Alarm,
    TimeSpan Recovered)
{
    public TimeSpan Period => Normal + Degrading + Alarm + Recovered;
}

public sealed record SimulatedConnectorProfile(
    string ConnectorId,
    string SourceSystem,
    string Protocol,
    string DisplayName,
    IReadOnlyList<SimulatedDeviceProfile> Devices);

public sealed record SimulatedDeviceProfile(
    string ConnectorId,
    string SourceSystem,
    string Protocol,
    string DeviceAssetId,
    TimeSpan PhaseOffset,
    IReadOnlyList<SimulatedTagProfile> Tags);

public sealed record SimulatedTagProfile(
    string ConnectorId,
    string SourceSystem,
    string Protocol,
    string DeviceAssetId,
    string TagKey,
    string Unit,
    decimal NormalMinimum,
    decimal NormalMaximum,
    decimal AlarmValue,
    bool Writable,
    decimal WritableMinimum,
    decimal WritableMaximum,
    string ProtocolAddress,
    TimeSpan PhaseOffset,
    bool AlarmScenarioEnabled);
