using System.Text.Json;
using Nerv.IIP.Contracts.ConnectorProtocol;

namespace Nerv.IIP.Contracts.ConnectorProtocol.Tests;

public sealed class ConnectorProtocolJsonTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void ApplicationRegistration_round_trips_with_web_json_options()
    {
        var context = new ConnectorRequestContext("1.0", "1.0", "corr-001", DateTimeOffset.Parse("2026-05-14T00:00:00Z"), "org-001", "env-dev", "connector-host-001");
        var source = new ApplicationRegistration(
            context,
            "idem-001",
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

        var json = JsonSerializer.Serialize(source, JsonOptions);
        var result = JsonSerializer.Deserialize<ApplicationRegistration>(json, JsonOptions);

        Assert.NotNull(result);
        Assert.Equal("demo-api", result.ApplicationKey);
        Assert.Equal("demo-api-001", result.InstanceKey);
        Assert.Equal("lifecycle.restart", result.Capabilities.Single().CapabilityCode);
    }
}
