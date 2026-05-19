using Nerv.IIP.ConnectorHost.Connectors.Docker;

namespace Nerv.IIP.ConnectorHost.Connectors.Docker.Tests;

public sealed class DockerConnectorTests
{
    [Fact]
    public async Task Docker_connector_discovers_containers_from_docker_cli_json()
    {
        var docker = new RecordingDockerCli([
            new DockerCliContainer("abc123456789", "registry.local/demo-api:1.2.3", "local-demo-001", "running", "Up 5 minutes")
        ]);
        var connector = new DockerConnector(docker);

        var target = (await connector.DiscoverAsync(CancellationToken.None)).Single();

        Assert.Equal("local-docker", target.NodeKey);
        Assert.Equal("demo-api", target.ApplicationKey);
        Assert.Equal("1.2.3", target.Version);
        Assert.Equal("docker-container-local-demo-001", target.InstanceKey);
        Assert.Equal("local-demo-001", target.InstanceName);
        Assert.Contains(target.Capabilities, x => x.Code == "runtime");
        Assert.Contains(target.Capabilities, x => x.Code == "log");
        Assert.Contains(target.Capabilities, x => x.Code == "lifecycle.restart");
        Assert.Equal("abc123456789", target.Metadata["containerId"]);
        Assert.Equal("registry.local/demo-api:1.2.3", target.Metadata["image"]);
        Assert.Equal("Up 5 minutes", target.Metadata["dockerStatus"]);
    }
}
