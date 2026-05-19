namespace Nerv.IIP.ConnectorHost.Connectors.Docker.Tests;

internal sealed class RecordingDockerCli(IReadOnlyList<DockerCliContainer> containers) : IDockerCli
{
    public DockerCliCommandResult RestartResult { get; set; } = DockerCliCommandResult.Succeeded("local-demo-001");
    public Exception? ListException { get; set; }
    public List<string> RestartedContainers { get; } = [];

    public Task<IReadOnlyList<DockerCliContainer>> ListContainersAsync(CancellationToken cancellationToken)
    {
        if (ListException is not null)
        {
            throw ListException;
        }

        return Task.FromResult(containers);
    }

    public Task<DockerCliCommandResult> RestartContainerAsync(string containerName, int gracePeriodSeconds, TimeSpan commandTimeout, CancellationToken cancellationToken)
    {
        RestartedContainers.Add(containerName);
        return Task.FromResult(RestartResult);
    }
}
