using Nerv.IIP.Contracts.Ops;

namespace Nerv.IIP.ConnectorHost.Connectors.Docker.Tests;

public sealed class DockerConnectorOperationTests
{
    [Fact]
    public async Task Docker_connector_executes_real_restart_command_for_instance_container()
    {
        var docker = new RecordingDockerCli([
            new DockerCliContainer("abc123456789", "nerv/demo-api:1.0.0", "local-demo-001", "running", "Up 5 minutes")
        ]);
        var connector = new DockerConnector(docker);
        var task = CreateTask("docker-container-local-demo-001");

        var result = await connector.ExecuteAsync(task, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("restart completed", result.Output["message"]);
        Assert.Equal("docker-container-local-demo-001", result.Output["instanceKey"]);
        Assert.Equal("local-demo-001", result.Output["containerName"]);
        Assert.Equal("abc123456789", result.Output["containerId"]);
        Assert.Equal(["local-demo-001"], docker.RestartedContainers);
    }

    [Fact]
    public async Task Docker_connector_returns_not_found_for_missing_container()
    {
        var connector = new DockerConnector(new RecordingDockerCli([
            new DockerCliContainer("abc123456789", "nerv/demo-api:1.0.0", "local-demo-001", "running", "Up 5 minutes")
        ]));
        var task = CreateTask("docker-container-missing");

        var result = await connector.ExecuteAsync(task, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("docker.container.not_found", result.FailureCode);
        Assert.Equal("validation", result.FailureCategory);
        Assert.False(result.Retryable);
        Assert.Equal("docker-container-missing", result.Output["instanceKey"]);
    }

    [Fact]
    public async Task Docker_connector_classifies_restart_timeout_with_diagnostics()
    {
        var docker = new RecordingDockerCli([
            new DockerCliContainer("abc123456789", "nerv/demo-api:1.0.0", "local-demo-001", "running", "Up 5 minutes")
        ])
        {
            RestartResult = DockerCliCommandResult.Timeout("restart exceeded test timeout", "partial output")
        };
        var connector = new DockerConnector(docker);

        var result = await connector.ExecuteAsync(CreateTask("docker-container-local-demo-001"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("docker.restart.timeout", result.FailureCode);
        Assert.Equal("timeout", result.FailureCategory);
        Assert.True(result.Retryable);
        Assert.Equal("partial output", result.Output["stdout"]);
        Assert.Equal("restart exceeded test timeout", result.Output["stderr"]);
    }

    [Fact]
    public async Task Docker_connector_classifies_daemon_unavailable_restart_failure()
    {
        var docker = new RecordingDockerCli([
            new DockerCliContainer("abc123456789", "nerv/demo-api:1.0.0", "local-demo-001", "running", "Up 5 minutes")
        ])
        {
            RestartResult = DockerCliCommandResult.Failed(1, "", "Cannot connect to the Docker daemon at npipe:////./pipe/docker_engine")
        };
        var connector = new DockerConnector(docker);

        var result = await connector.ExecuteAsync(CreateTask("docker-container-local-demo-001"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("docker.daemon.unavailable", result.FailureCode);
        Assert.Equal("unreachable", result.FailureCategory);
        Assert.True(result.Retryable);
        Assert.Equal("1", result.Output["exitCode"]);
    }

    [Fact]
    public async Task Docker_connector_classifies_daemon_unavailable_during_restart_discovery()
    {
        var docker = new RecordingDockerCli([])
        {
            ListException = new DockerCliException(
                "Docker container discovery failed.",
                DockerCliCommandResult.Failed(1, "", "Cannot connect to the Docker daemon at unix:///var/run/docker.sock"))
        };
        var connector = new DockerConnector(docker);

        var result = await connector.ExecuteAsync(CreateTask("docker-container-local-demo-001"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("docker.daemon.unavailable", result.FailureCode);
        Assert.Equal("unreachable", result.FailureCategory);
        Assert.True(result.Retryable);
        Assert.Equal("docker-container-local-demo-001", result.Output["instanceKey"]);
        Assert.Equal("local-demo-001", result.Output["containerName"]);
    }

    internal static OperationTaskDispatchItem CreateTaskForIntegration(string instanceKey) => CreateTask(instanceKey);

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
            300,
            3);
    }
}
