using Nerv.IIP.AppHub.Domain;
using Nerv.IIP.Contracts.ConnectorProtocol;

namespace Nerv.IIP.AppHub.Domain.Tests;

public sealed class AppHubStateStoreTests
{
    [Fact]
    public void Registration_heartbeat_and_state_snapshot_update_instance_facts()
    {
        var store = new InMemoryAppHubStateStore();
        var registration = Samples.Registration("idem-001");

        var first = store.Register(registration);
        var duplicate = store.Register(registration);
        store.RecordHeartbeat(Samples.Heartbeat());
        store.RecordStateSnapshot(Samples.State("running", "healthy"));
        store.RecordStateSnapshot(Samples.State("running", "healthy"));
        store.RecordStateSnapshot(Samples.State("stopped", "unhealthy"));

        Assert.Equal(first.RegistrationId, duplicate.RegistrationId);
        Assert.Single(store.Applications);
        Assert.Single(store.Nodes);
        Assert.Single(store.Instances);
        Assert.Equal(3, store.StateHistory.Count);
        Assert.Single(store.PublishedStatusChanges);

        InstanceDetailFact detail = store.GetInstanceDetail("org-001", "env-dev", "demo-api-001");
        Assert.Equal("stopped", detail.ReportedStatus);
        Assert.Equal("unhealthy", detail.HealthStatus);
        Assert.NotNull(detail.LastHeartbeatAtUtc);
        Assert.Equal("lifecycle.restart", detail.Capabilities.Single().CapabilityCode);
    }

    private static class Samples
    {
        private static readonly ConnectorRequestContext Context = new("1.0", "1.0", "corr-001", DateTimeOffset.Parse("2026-05-14T00:00:00Z"), "org-001", "env-dev", "connector-host-001");

        public static ApplicationRegistration Registration(string idempotencyKey) => new(
            Context,
            idempotencyKey,
            "node-001",
            "local-docker",
            "docker",
            "demo-api",
            "Demo API",
            "1.0.0",
            "demo-api-001",
            "demo-api",
            [new CapabilityDescriptor("lifecycle.restart", "1.0", "lifecycle", ["restart"], new Dictionary<string, string>())],
            new Dictionary<string, string> { ["containerId"] = "abc123" });

        public static ApplicationHeartbeat Heartbeat() => new(
            Context,
            "demo-api-001",
            DateTimeOffset.Parse("2026-05-14T00:00:05Z"),
            true,
            DateTimeOffset.Parse("2026-05-14T00:00:00Z"),
            12,
            new Dictionary<string, string>());

        public static InstanceStateSnapshot State(string reportedStatus, string healthStatus) => new(
            Context,
            "demo-api-001",
            DateTimeOffset.Parse("2026-05-14T00:00:10Z"),
            reportedStatus,
            healthStatus,
            "summary",
            new Dictionary<string, string>(),
            new Dictionary<string, decimal>(),
            new Dictionary<string, string>());
    }
}
