using Nerv.IIP.AppHub.Domain;
using Nerv.IIP.Contracts.ConnectorProtocol;

namespace Nerv.IIP.AppHub.Domain.Tests;

public sealed class AppHubRegistrationInvariantTests
{
    [Fact]
    public void Same_connector_host_cannot_create_duplicate_instance_rows()
    {
        var store = new InMemoryAppHubStateStore();
        var first = Registration("idem-001", "Demo API");
        var duplicateFromSameHost = Registration("idem-002", "Demo API Renamed");

        store.Register(first);
        store.Register(duplicateFromSameHost);

        var instance = Assert.Single(store.Instances);
        Assert.Equal("connector-host-001", first.Context.ConnectorHostId);
        Assert.Equal("demo-api-001", instance.InstanceKey);
        Assert.Equal("Demo API Renamed", instance.InstanceName);
        Assert.Single(store.CapabilityManifests);
    }

    private static ApplicationRegistration Registration(string idempotencyKey, string instanceName)
    {
        var context = new ConnectorRequestContext(
            "1.0",
            "1.0",
            "corr-001",
            DateTimeOffset.Parse("2026-05-14T00:00:00Z"),
            "org-001",
            "env-dev",
            "connector-host-001");

        return new ApplicationRegistration(
            context,
            idempotencyKey,
            "node-001",
            "local-docker",
            "docker",
            "demo-api",
            "Demo API",
            "1.0.0",
            "demo-api-001",
            instanceName,
            [new CapabilityDescriptor("lifecycle.restart", "1.0", "lifecycle", ["restart"], new Dictionary<string, string>())],
            new Dictionary<string, string>());
    }
}
