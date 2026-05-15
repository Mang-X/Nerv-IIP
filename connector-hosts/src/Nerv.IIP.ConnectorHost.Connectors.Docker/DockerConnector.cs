using Nerv.IIP.ConnectorHost.Connectors.Abstractions;

namespace Nerv.IIP.ConnectorHost.Connectors.Docker;

public sealed record DockerContainerDescriptor(string ContainerId, string Image, string Name, string Status);

public sealed class DockerConnector(IReadOnlyList<DockerContainerDescriptor>? containers = null) : IConnector
{
    private readonly IReadOnlyList<DockerContainerDescriptor> _containers = containers ?? [];

    public Task<IReadOnlyList<ConnectorTarget>> DiscoverAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ConnectorTarget> targets = _containers.Select(Map).ToList();
        return Task.FromResult(targets);
    }

    private static ConnectorTarget Map(DockerContainerDescriptor container)
    {
        var (applicationKey, version) = ParseImage(container.Image);
        var instanceKey = $"docker-container-{container.ContainerId}";
        return new ConnectorTarget(
            "local-docker",
            "local-docker",
            "docker",
            applicationKey,
            ToDisplayName(applicationKey),
            version,
            instanceKey,
            container.Name,
            NormalizeStatus(container.Status),
            container.Status.Equals("running", StringComparison.OrdinalIgnoreCase) ? "healthy" : "unhealthy",
            [
                new ConnectorCapability("runtime", "1.0", "runtime", ["inspect"]),
                new ConnectorCapability("log", "1.0", "observability", ["tail"]),
                new ConnectorCapability("lifecycle.restart", "1.0", "lifecycle", ["restart"])
            ],
            new Dictionary<string, string> { ["containerId"] = container.ContainerId, ["image"] = container.Image });
    }

    private static (string ApplicationKey, string Version) ParseImage(string image)
    {
        var imageName = image.Split('/').Last();
        var parts = imageName.Split(':', 2);
        return (parts[0], parts.Length == 2 ? parts[1] : "latest");
    }

    private static string ToDisplayName(string key) => string.Join(' ', key.Split('-', StringSplitOptions.RemoveEmptyEntries).Select(x => char.ToUpperInvariant(x[0]) + x[1..]));

    private static string NormalizeStatus(string status) => status.Equals("running", StringComparison.OrdinalIgnoreCase) ? "running" : "stopped";
}
