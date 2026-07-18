using Nerv.IIP.ConnectorHost.Application;
using Nerv.IIP.ConnectorHost.Connectors.Abstractions;
using Nerv.IIP.Contracts.ConnectorProtocol;
using Nerv.IIP.Sdk.ConnectorProtocol;

namespace Nerv.IIP.ConnectorHost.Application.Tests;

public sealed class ReportingLoopTests
{
    [Fact]
    public async Task Configured_collection_connector_id_is_identical_in_registration_and_collection_health()
    {
        var target = new ConnectorTarget("node", "node", "opcua", "collector", "Collector", "1", "line-a-primary", "OPC", "running", "healthy", [], new Dictionary<string, string>(),
            new ConnectorCollectionHealthSnapshot("line-a-primary", "opcua", Guid.CreateVersion7(), 0, 0, 0, null));
        var client = new RecordingConnectorProtocolClient();
        var loop = new ConnectorReportingLoop([new StaticConnector(target)], client, ConnectorHostRuntimeContext.DefaultLocal);

        await loop.RunCycleAsync(CancellationToken.None);

        Assert.Equal("line-a-primary", client.Registrations.Single().InstanceKey);
        Assert.Equal("line-a-primary", client.StateSnapshots.Single().CollectionHealth!.ConnectorId);
    }

    [Fact]
    public async Task Reports_optional_connection_state_without_changing_legacy_payload()
    {
        var observed = DateTimeOffset.Parse("2026-07-17T00:00:00Z");
        var target = new ConnectorTarget("node", "node", "opcua", "collector", "Collector", "1", "line-a-primary", "OPC", "running", "healthy", [], new Dictionary<string, string>(),
            new ConnectorCollectionHealthSnapshot("line-a-primary", "opcua", Guid.CreateVersion7(), 0, 0, 0, null,
                new ConnectorConnectionStateSnapshot("lost", observed, null, observed, "transport", "socket-closed")));
        var client = new RecordingConnectorProtocolClient();
        var loop = new ConnectorReportingLoop([new StaticConnector(target)], client, ConnectorHostRuntimeContext.DefaultLocal);

        await loop.RunCycleAsync(CancellationToken.None);

        Assert.Equal("lost", client.StateSnapshots.Single().CollectionHealth!.Connection!.Status);
    }

    [Fact]
    public async Task Reports_typed_collection_health_without_mapping_bucket_or_reconnect_metrics()
    {
        var epoch = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var sampledAt = DateTimeOffset.Parse("2026-07-13T01:02:03Z");
        var target = new ConnectorTarget("node", "node", "opcua", "collector", "Collector", "1", "opcua-opc-main", "OPC", "running", "healthy", [], new Dictionary<string, string>(),
            new ConnectorCollectionHealthSnapshot("opcua-opc-main", "opcua", epoch, 17, 2, 3, sampledAt));
        var client = new RecordingConnectorProtocolClient();
        var loop = new ConnectorReportingLoop([new StaticConnector(target)], client, ConnectorHostRuntimeContext.DefaultLocal);

        await loop.RunCycleAsync(CancellationToken.None);

        var report = Assert.Single(client.StateSnapshots).CollectionHealth;
        Assert.NotNull(report);
        Assert.Equal("opcua-opc-main", report.ConnectorId);
        Assert.Equal("opcua", report.SourceSystem);
        Assert.Equal(epoch, report.CounterEpoch);
        Assert.Equal(17, report.ReceivedCount);
        Assert.Equal(2, report.DroppedCount);
        Assert.Equal(3, report.ErrorCount);
        Assert.Equal(sampledAt, report.LastSampleAtUtc);
    }
    [Fact]
    public async Task Reporting_cycle_sends_registration_before_heartbeat_and_state_snapshot()
    {
        var client = new RecordingConnectorProtocolClient();
        var loop = new ConnectorReportingLoop([new StaticConnector()], client, ConnectorHostRuntimeContext.DefaultLocal);

        await loop.RunCycleAsync(CancellationToken.None);

        Assert.Equal(["registration:demo-api-001", "heartbeat:demo-api-001", "state:demo-api-001"], client.Calls);
    }

    [Fact]
    public async Task Stable_target_is_not_registered_again_on_every_heartbeat_cycle()
    {
        var client = new RecordingConnectorProtocolClient();
        var loop = new ConnectorReportingLoop([new StaticConnector()], client, ConnectorHostRuntimeContext.DefaultLocal);

        await loop.RunCycleAsync(CancellationToken.None);
        await loop.RunCycleAsync(CancellationToken.None);

        Assert.Equal(1, client.Calls.Count(call => call == "registration:demo-api-001"));
        Assert.Equal(2, client.Calls.Count(call => call == "heartbeat:demo-api-001"));
        Assert.Equal(2, client.Calls.Count(call => call == "state:demo-api-001"));
    }

    [Fact]
    public async Task Reporting_cycle_preserves_registration_heartbeat_state_order_for_each_target()
    {
        var client = new RecordingConnectorProtocolClient();
        var loop = new ConnectorReportingLoop(
            [new StaticConnector(CreateTarget("target-a")), new StaticConnector(CreateTarget("target-b"))],
            client,
            ConnectorHostRuntimeContext.DefaultLocal);

        await loop.RunCycleAsync(CancellationToken.None);

        Assert.Equal(
            [
                "registration:target-a", "heartbeat:target-a", "state:target-a",
                "registration:target-b", "heartbeat:target-b", "state:target-b"
            ],
            client.Calls);
    }

    [Fact]
    public async Task Heartbeat_reports_host_reachable_when_collection_is_degraded()
    {
        var client = new RecordingConnectorProtocolClient();
        var target = CreateTarget("target-a") with { HealthStatus = "degraded" };
        var loop = new ConnectorReportingLoop([new StaticConnector(target)], client, ConnectorHostRuntimeContext.DefaultLocal);

        await loop.RunCycleAsync(CancellationToken.None);

        Assert.True(Assert.Single(client.Heartbeats).Reachable);
    }

    [Fact]
    public async Task Failed_apphub_request_is_retried_on_next_cycle()
    {
        var client = new RecordingConnectorProtocolClient { FailFirstRegistration = true };
        var loop = new ConnectorReportingLoop([new StaticConnector()], client, ConnectorHostRuntimeContext.DefaultLocal);

        await Assert.ThrowsAsync<HttpRequestException>(() => loop.RunCycleAsync(CancellationToken.None));
        await loop.RunCycleAsync(CancellationToken.None);

        Assert.Equal(2, client.Calls.Count(x => x == "registration:demo-api-001"));
    }

    private sealed class StaticConnector(ConnectorTarget? target = null) : IConnector
    {
        public Task<IReadOnlyList<ConnectorTarget>> DiscoverAsync(CancellationToken cancellationToken)
        {
            IReadOnlyList<ConnectorTarget> targets =
            [
                target ?? new("node-001", "local-docker", "docker", "demo-api", "Demo API", "1.0.0", "demo-api-001", "demo-api", "running", "healthy", [new ConnectorCapability("lifecycle.restart", "1.0", "lifecycle", ["restart"])], new Dictionary<string, string>())
            ];
            return Task.FromResult(targets);
        }
    }

    private static ConnectorTarget CreateTarget(string instanceKey) => new(
        $"node-{instanceKey}",
        instanceKey,
        "test",
        "collector",
        "Collector",
        "1.0",
        instanceKey,
        instanceKey,
        "running",
        "healthy",
        [],
        new Dictionary<string, string>());

    private sealed class RecordingConnectorProtocolClient : IConnectorProtocolClient
    {
        private bool _failed;
        public bool FailFirstRegistration { get; init; }
        public List<string> Calls { get; } = [];
        public List<ApplicationRegistration> Registrations { get; } = [];
        public List<ApplicationHeartbeat> Heartbeats { get; } = [];
        public List<InstanceStateSnapshot> StateSnapshots { get; } = [];

        public Task<ApplicationRegistrationResult> SendRegistrationAsync(ApplicationRegistration registration, CancellationToken cancellationToken = default)
        {
            Calls.Add($"registration:{registration.InstanceKey}");
            Registrations.Add(registration);
            if (FailFirstRegistration && !_failed)
            {
                _failed = true;
                throw new HttpRequestException("AppHub unavailable");
            }

            return Task.FromResult(new ApplicationRegistrationResult($"reg-{registration.InstanceKey}", registration.InstanceKey, $"token-{registration.InstanceKey}"));
        }

        public Task SendHeartbeatAsync(ApplicationHeartbeat heartbeat, CancellationToken cancellationToken = default)
        {
            Calls.Add($"heartbeat:{heartbeat.InstanceKey}");
            Heartbeats.Add(heartbeat);
            return Task.CompletedTask;
        }

        public Task SendStateSnapshotAsync(InstanceStateSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            Calls.Add($"state:{snapshot.InstanceKey}");
            StateSnapshots.Add(snapshot);
            return Task.CompletedTask;
        }
    }
}
