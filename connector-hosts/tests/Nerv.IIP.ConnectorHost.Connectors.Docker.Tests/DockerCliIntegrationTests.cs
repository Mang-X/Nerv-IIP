using System.Diagnostics;

namespace Nerv.IIP.ConnectorHost.Connectors.Docker.Tests;

public sealed class DockerCliIntegrationTests
{
    [DockerCliFact]
    public async Task Docker_connector_discovers_and_restarts_a_real_container()
    {
        var containerName = $"nerv-iip-connector-{Guid.NewGuid():N}"[..32];
        await RunDockerAsync("pull", "alpine:3.20");
        await RunDockerAsync("container", "create", "--name", containerName, "alpine:3.20", "sleep", "300");

        try
        {
            await RunDockerAsync("container", "start", containerName);
            var connector = new DockerConnector(new DockerCli());

            var target = (await connector.DiscoverAsync(CancellationToken.None)).Single(x => x.InstanceKey == $"docker-container-{containerName}");
            var result = await connector.ExecuteAsync(DockerConnectorOperationTests.CreateTaskForIntegration(target.InstanceKey), CancellationToken.None);

            Assert.Equal(containerName, target.InstanceName);
            Assert.True(result.Succeeded);
            Assert.Equal("restart completed", result.Output["message"]);
            Assert.Equal(containerName, result.Output["containerName"]);
        }
        finally
        {
            await RunDockerAllowFailureAsync("container", "rm", "--force", containerName);
        }
    }

    private static async Task RunDockerAsync(params string[] arguments)
    {
        await RunDockerAsync(arguments, allowFailure: false);
    }

    private static async Task RunDockerAllowFailureAsync(params string[] arguments)
    {
        await RunDockerAsync(arguments, allowFailure: true);
    }

    private static async Task RunDockerAsync(IReadOnlyList<string> arguments, bool allowFailure)
    {
        using var process = new Process
        {
            StartInfo =
            {
                FileName = "docker",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (!allowFailure && process.ExitCode != 0)
        {
            throw new InvalidOperationException($"docker {string.Join(' ', arguments)} failed with exit code {process.ExitCode}. stdout: {stdout} stderr: {stderr}");
        }
    }
}

internal sealed class DockerCliFactAttribute : FactAttribute
{
    public DockerCliFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("NERV_IIP_DOCKER_INTEGRATION"), "1", StringComparison.Ordinal))
        {
            Skip = "Set NERV_IIP_DOCKER_INTEGRATION=1 to run real Docker integration tests.";
            return;
        }

        if (!DockerDaemonAvailable())
        {
            Skip = "Docker CLI or Docker daemon is not available.";
        }
    }

    private static bool DockerDaemonAvailable()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("docker")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                ArgumentList = { "version", "--format", "{{.Server.Version}}" }
            });
            if (process is null)
            {
                return false;
            }

            return process.WaitForExit(5000) && process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
