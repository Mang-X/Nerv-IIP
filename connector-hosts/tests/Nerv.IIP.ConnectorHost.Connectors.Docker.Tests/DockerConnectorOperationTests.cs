using Nerv.IIP.Contracts.Ops;

namespace Nerv.IIP.ConnectorHost.Connectors.Docker.Tests;

public sealed class DockerConnectorOperationTests
{
    [Fact]
    public async Task Docker_connector_executes_restart_for_existing_container()
    {
        var connector = new DockerConnector([
            new DockerContainerDescriptor("local-demo-001", "nerv/demo-api:1.0.0", "demo-api", "running")
        ]);
        var task = CreateTask("docker-container-local-demo-001");

        var result = await connector.ExecuteAsync(task, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("restart accepted", result.Output["message"]);
        Assert.Equal("docker-container-local-demo-001", result.Output["instanceKey"]);
    }

    [Fact]
    public async Task Docker_connector_returns_not_found_for_missing_container()
    {
        var connector = new DockerConnector([
            new DockerContainerDescriptor("local-demo-001", "nerv/demo-api:1.0.0", "demo-api", "running")
        ]);
        var task = CreateTask("docker-container-missing");

        var result = await connector.ExecuteAsync(task, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("docker.container.not_found", result.FailureCode);
    }

    private static OperationTaskDispatchItem CreateTask(string instanceKey)
    {
        return new OperationTaskDispatchItem(
            "op-000001",
            "attempt-000001",
            "org-001",
            "env-dev",
            "connector-host-001",
            instanceKey,
            "lifecycle.restart",
            "corr-docker-operation-test",
            new Dictionary<string, string>(),
            "lease-000001",
            DateTimeOffset.Parse("2026-05-19T00:00:00Z"),
            DateTimeOffset.Parse("2026-05-19T00:05:00Z"),
            1,
            3);
    }
}
