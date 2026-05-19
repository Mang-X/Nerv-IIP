using Nerv.IIP.ConnectorHost.Connectors.Abstractions;
using Nerv.IIP.Contracts.Ops;

namespace Nerv.IIP.ConnectorHost.Connectors.Docker;

public sealed record DockerConnectorOptions(
    string NodeKey = "local-docker",
    string NodeName = "local-docker",
    int RestartGracePeriodSeconds = 10,
    int RestartCommandTimeoutSeconds = 30);

public sealed class DockerConnector(
    IDockerCli? docker = null,
    DockerConnectorOptions? options = null) : IConnector, IConnectorOperationExecutor
{
    private const string InstanceKeyPrefix = "docker-container-";
    private readonly IDockerCli _docker = docker ?? new DockerCli();
    private readonly DockerConnectorOptions _options = options ?? new DockerConnectorOptions();

    public bool CanExecute(OperationTaskDispatchItem task)
    {
        return task.OperationCode == "lifecycle.restart" && task.InstanceKey.StartsWith(InstanceKeyPrefix, StringComparison.Ordinal);
    }

    public async Task<ConnectorOperationExecution> ExecuteAsync(OperationTaskDispatchItem task, CancellationToken cancellationToken)
    {
        var containerName = task.InstanceKey[InstanceKeyPrefix.Length..];
        IReadOnlyList<DockerCliContainer> containers;
        try
        {
            containers = await _docker.ListContainersAsync(cancellationToken);
        }
        catch (DockerCliException ex)
        {
            var classification = ClassifyFailure(ex.Result)
                ?? new DockerFailureClassification("docker.command.failed", ex.Message, "runtime", true);
            return ConnectorOperationExecution.Failed(
                classification.Code,
                classification.Message,
                classification.Category,
                classification.Retryable,
                CreateDiagnosticOutput(task.InstanceKey, containerName, ex.Result));
        }

        var container = containers.FirstOrDefault(x => string.Equals(x.Name, containerName, StringComparison.Ordinal));
        if (container is null)
        {
            return ConnectorOperationExecution.Failed(
                "docker.container.not_found",
                $"Container for {task.InstanceKey} was not found.",
                "validation",
                false,
                new Dictionary<string, string> { ["instanceKey"] = task.InstanceKey, ["containerName"] = containerName });
        }

        var result = await _docker.RestartContainerAsync(
            container.Name,
            _options.RestartGracePeriodSeconds,
            TimeSpan.FromSeconds(_options.RestartCommandTimeoutSeconds),
            cancellationToken);

        var output = CreateRestartOutput(task.InstanceKey, container, result);
        var failure = ClassifyFailure(result);
        if (failure is not null)
        {
            return ConnectorOperationExecution.Failed(
                failure.Code,
                failure.Message,
                failure.Category,
                failure.Retryable,
                output);
        }

        output["message"] = "restart completed";
        return ConnectorOperationExecution.Success(output);
    }

    public async Task<IReadOnlyList<ConnectorTarget>> DiscoverAsync(CancellationToken cancellationToken)
    {
        var containers = await _docker.ListContainersAsync(cancellationToken);
        return containers.Select(Map).ToList();
    }

    private ConnectorTarget Map(DockerCliContainer container)
    {
        var (applicationKey, version) = ParseImage(container.Image);
        var instanceKey = $"{InstanceKeyPrefix}{container.Name}";
        return new ConnectorTarget(
            _options.NodeKey,
            _options.NodeName,
            "docker",
            applicationKey,
            ToDisplayName(applicationKey),
            version,
            instanceKey,
            container.Name,
            NormalizeStatus(container.State),
            container.State.Equals("running", StringComparison.OrdinalIgnoreCase) ? "healthy" : "unhealthy",
            [
                new ConnectorCapability("runtime", "1.0", "runtime", ["inspect"]),
                new ConnectorCapability("log", "1.0", "observability", ["tail"]),
                new ConnectorCapability("lifecycle.restart", "1.0", "lifecycle", ["restart"])
            ],
            new Dictionary<string, string>
            {
                ["containerId"] = container.ContainerId,
                ["image"] = container.Image,
                ["dockerStatus"] = container.Status
            });
    }

    private static Dictionary<string, string> CreateRestartOutput(string instanceKey, DockerCliContainer container, DockerCliCommandResult result)
    {
        return new Dictionary<string, string>
        {
            ["instanceKey"] = instanceKey,
            ["containerName"] = container.Name,
            ["containerId"] = container.ContainerId,
            ["exitCode"] = result.ExitCode?.ToString() ?? "",
            ["stdout"] = result.Stdout,
            ["stderr"] = result.Stderr
        };
    }

    private static Dictionary<string, string> CreateDiagnosticOutput(string instanceKey, string containerName, DockerCliCommandResult result)
    {
        return new Dictionary<string, string>
        {
            ["instanceKey"] = instanceKey,
            ["containerName"] = containerName,
            ["exitCode"] = result.ExitCode?.ToString() ?? "",
            ["stdout"] = result.Stdout,
            ["stderr"] = result.Stderr
        };
    }

    private static DockerFailureClassification? ClassifyFailure(DockerCliCommandResult result)
    {
        if (result.TimedOut)
        {
            return new("docker.restart.timeout", "Docker command timed out.", "timeout", true);
        }

        if (result.ExitCode == 0)
        {
            return null;
        }

        var diagnostic = $"{result.Stdout}\n{result.Stderr}";
        if (diagnostic.Contains("No such container", StringComparison.OrdinalIgnoreCase))
        {
            return new("docker.container.not_found", "Docker reported that the container no longer exists.", "validation", false);
        }

        if (diagnostic.Contains("Cannot connect to the Docker daemon", StringComparison.OrdinalIgnoreCase)
            || diagnostic.Contains("is the docker daemon running", StringComparison.OrdinalIgnoreCase)
            || diagnostic.Contains("error during connect", StringComparison.OrdinalIgnoreCase))
        {
            return new("docker.daemon.unavailable", "Docker daemon is unavailable.", "unreachable", true);
        }

        if (diagnostic.Contains("permission denied", StringComparison.OrdinalIgnoreCase)
            || diagnostic.Contains("access is denied", StringComparison.OrdinalIgnoreCase))
        {
            return new("docker.permission_denied", "Docker restart was denied by the local runtime.", "permission", false);
        }

        return new("docker.restart.failed", "Docker restart command failed.", "runtime", true);
    }

    private static (string ApplicationKey, string Version) ParseImage(string image)
    {
        var imageName = image.Split('/').Last();
        var parts = imageName.Split(':', 2);
        return (parts[0], parts.Length == 2 ? parts[1] : "latest");
    }

    private static string ToDisplayName(string key)
    {
        return string.Join(' ', key.Split('-', StringSplitOptions.RemoveEmptyEntries).Select(x => char.ToUpperInvariant(x[0]) + x[1..]));
    }

    private static string NormalizeStatus(string status)
    {
        return status.Equals("running", StringComparison.OrdinalIgnoreCase) ? "running" : "stopped";
    }

    private sealed record DockerFailureClassification(string Code, string Message, string Category, bool Retryable);
}
