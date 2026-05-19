using Nerv.IIP.AppHub.Domain.AggregatesModel.ApplicationInstanceAggregate;
using Nerv.IIP.Contracts.ConnectorProtocol;

namespace Nerv.IIP.AppHub.Domain.Tests;

public sealed class ApplicationInstanceAggregateTests
{
    [Fact]
    public void Instance_registration_starts_unknown_and_registration_update_replaces_manifest()
    {
        var instance = CreateInstance();

        instance.UpdateRegistration(
            "org-001",
            "env-dev",
            "demo-api",
            "2.0.0",
            "node-002",
            "Demo API Two",
            new Dictionary<string, string> { ["containerId"] = "def456" },
            [Capability("lifecycle.stop")]);

        Assert.Equal("2.0.0", instance.Version);
        Assert.Equal("node-002", instance.NodeKey);
        Assert.Equal("Demo API Two", instance.InstanceName);
        Assert.Equal("unknown", instance.ReportedStatus);
        Assert.Equal("unknown", instance.HealthStatus);
        Assert.Equal("def456", instance.Metadata["containerId"]);
        Assert.Equal("lifecycle.stop", Assert.Single(instance.Capabilities).CapabilityCode);
    }

    [Fact]
    public void Heartbeat_records_first_liveness_and_updates_existing_liveness()
    {
        var instance = CreateInstance();
        var firstHeartbeat = DateTimeOffset.Parse("2026-05-14T00:00:05Z");
        var secondHeartbeat = DateTimeOffset.Parse("2026-05-14T00:00:10Z");

        instance.RecordHeartbeat(firstHeartbeat, true, 12);
        instance.RecordHeartbeat(secondHeartbeat, false, 35);

        Assert.NotNull(instance.Heartbeat);
        Assert.Equal(secondHeartbeat, instance.Heartbeat.LastHeartbeatAtUtc);
        Assert.False(instance.Heartbeat.Reachable);
        Assert.Equal(35, instance.Heartbeat.LatencyMs);
    }

    [Fact]
    public void State_snapshots_track_history_and_publish_status_changes_after_initial_status()
    {
        var instance = CreateInstance();
        var firstObservedAt = DateTimeOffset.Parse("2026-05-14T00:00:10Z");
        var secondObservedAt = DateTimeOffset.Parse("2026-05-14T00:00:20Z");
        var thirdObservedAt = DateTimeOffset.Parse("2026-05-14T00:00:30Z");

        instance.RecordStateSnapshot(firstObservedAt, "running", "healthy", "started", new Dictionary<string, string> { ["phase"] = "start" });
        instance.RecordStateSnapshot(secondObservedAt, "running", "healthy", "same", new Dictionary<string, string> { ["phase"] = "steady" });
        instance.RecordStateSnapshot(thirdObservedAt, "stopped", "unhealthy", "stopped", new Dictionary<string, string> { ["phase"] = "stop" });

        Assert.Equal("stopped", instance.ReportedStatus);
        Assert.Equal("unhealthy", instance.HealthStatus);
        Assert.Equal("stop", instance.Metadata["phase"]);
        Assert.Equal(3, instance.StateHistory.Count);
        var statusChange = Assert.Single(instance.StatusChanges);
        Assert.Equal("running", statusChange.PreviousStatus);
        Assert.Equal("stopped", statusChange.CurrentStatus);
        Assert.Equal(thirdObservedAt, statusChange.ChangedAtUtc);
    }

    [Fact]
    public void Operation_refresh_is_idempotent_per_result_key()
    {
        var instance = CreateInstance();
        instance.RecordStateSnapshot(DateTimeOffset.Parse("2026-05-14T00:00:10Z"), "running", "healthy", "started", new Dictionary<string, string>());

        var first = instance.RecordOperationTaskCompletedRefresh(
            "idem-result-001",
            "op-000001",
            "lifecycle.restart",
            DateTimeOffset.Parse("2026-05-14T00:01:00Z"),
            "corr-001");
        var duplicate = instance.RecordOperationTaskCompletedRefresh(
            "idem-result-001",
            "op-000001",
            "lifecycle.restart",
            DateTimeOffset.Parse("2026-05-14T00:01:05Z"),
            "corr-001");

        Assert.True(first);
        Assert.False(duplicate);
        Assert.Equal(2, instance.StateHistory.Count);
        Assert.Equal("idem-result-001", instance.Metadata["ops.lastCompletedOperationIdempotencyKey"]);
    }

    private static ApplicationInstance CreateInstance()
    {
        return new ApplicationInstance(
            "org-001",
            "env-dev",
            "demo-api",
            "1.0.0",
            "node-001",
            "demo-api-001",
            "Demo API",
            new Dictionary<string, string> { ["containerId"] = "abc123" },
            [Capability("lifecycle.restart")]);
    }

    private static CapabilityDescriptor Capability(string code)
    {
        return new CapabilityDescriptor(code, "1.0", "lifecycle", ["restart"], new Dictionary<string, string>());
    }
}
