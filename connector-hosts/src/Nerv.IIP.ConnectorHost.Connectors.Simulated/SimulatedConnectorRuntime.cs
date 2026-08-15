using Nerv.IIP.ConnectorHost.Application;
using Nerv.IIP.ConnectorHost.Connectors.Abstractions;

namespace Nerv.IIP.ConnectorHost.Connectors.Simulated;

internal sealed class SimulatedConnectorRuntime
{
    public SimulatedConnectorRuntime(
        SimulatedConnectorProfile profile,
        SimulatedConnectorOptions options,
        TimeProvider timeProvider,
        IConnectorReportSignal? reportSignal,
        IConnectorManifestSignal? manifestSignal)
    {
        Profile = profile;
        Outbox = new SimulatedSampleOutbox(
            options.MaxPendingSamples,
            options.MaxDeliveryAttempts,
            TimeSpan.FromMilliseconds(options.RetryBaseMilliseconds));
        ConnectionTracker = new ConnectorConnectionStateTracker(
            profile.ConnectorId,
            timeProvider,
            reportSignal is null ? static _ => { } : reportSignal.Signal);
        ManifestTracker = new ConnectorTagManifestTracker(
            profile.ConnectorId,
            profile.SourceSystem,
            profile.Devices
                .SelectMany(device => device.Tags)
                .Select(tag => new ConnectorTagManifestDefinition(
                    tag.DeviceAssetId,
                    tag.TagKey,
                    true,
                    tag.ProtocolAddress))
                .ToArray(),
            timeProvider,
            manifestSignal is null ? static _ => { } : manifestSignal.Signal);
        DeviceStates = profile.Devices.ToDictionary(
            device => device.DeviceAssetId,
            _ => SimulatedDeviceRuntimeState.Create(
                "running",
                options.MaxPendingStateTransitionsPerDevice),
            StringComparer.Ordinal);
        ConnectionTracker.MarkAlive();
    }

    public object Gate { get; } = new();
    public SimulatedConnectorProfile Profile { get; }
    public SimulatedSampleOutbox Outbox { get; }
    public ConnectorConnectionStateTracker ConnectionTracker { get; }
    public ConnectorTagManifestTracker ManifestTracker { get; }
    public Guid CounterEpoch { get; } = Guid.CreateVersion7();
    public Dictionary<(string DeviceAssetId, string TagKey), long> LastGeneratedCycles { get; } = [];
    public Dictionary<(string DeviceAssetId, string TagKey), decimal?> ControlledValues { get; } = [];
    public Dictionary<string, SimulatedDeviceRuntimeState> DeviceStates { get; }
    public bool ManifestActivated { get; set; }
    public long ReceivedCount { get; set; }
    public long DroppedCount { get; set; }
    public long ErrorCount { get; set; }
    public DateTimeOffset? LastSampleAtUtc { get; set; }
}

internal sealed class SimulatedDeviceRuntimeState
{
    private SimulatedDeviceRuntimeState(
        string state,
        int pendingTransitionCapacity)
    {
        State = state;
        PendingTransitionCapacity = pendingTransitionCapacity;
        PendingObservations.AddLast(new SimulatedPendingDeviceStateObservation(
            state,
            null,
            null,
            true));
    }

    public string State { get; set; }
    public int PendingTransitionCapacity { get; }
    public LinkedList<SimulatedPendingDeviceStateObservation> PendingObservations { get; } = [];
    public int PendingTransitionCount =>
        PendingObservations.Count(observation => !observation.IsInitial);

    public static SimulatedDeviceRuntimeState Create(
        string state,
        int pendingTransitionCapacity) =>
        new(state, pendingTransitionCapacity);
}

internal sealed record SimulatedPendingDeviceStateObservation(
    string State,
    string? SourceSequence,
    DateTimeOffset? OccurredAtUtc,
    bool IsInitial);
