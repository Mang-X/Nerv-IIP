using Nerv.IIP.ConnectorHost.Connectors.Docker;

namespace Nerv.IIP.ConnectorHost.Connectors.Docker.Tests;

public sealed class DockerConnectorTests
{
    [Fact]
    public async Task Docker_container_maps_to_stable_target_fields_and_capabilities()
    {
        var connector = new DockerConnector([
            new DockerContainerDescriptor("abc123456789", "registry.local/demo-api:1.2.3", "demo-api", "running")
        ]);

        var target = (await connector.DiscoverAsync(CancellationToken.None)).Single();

        Assert.Equal("local-docker", target.NodeKey);
        Assert.Equal("demo-api", target.ApplicationKey);
        Assert.Equal("1.2.3", target.Version);
        Assert.Equal("docker-container-abc123456789", target.InstanceKey);
        Assert.Contains(target.Capabilities, x => x.Code == "runtime");
        Assert.Contains(target.Capabilities, x => x.Code == "log");
        Assert.Contains(target.Capabilities, x => x.Code == "lifecycle.restart");
    }
}
