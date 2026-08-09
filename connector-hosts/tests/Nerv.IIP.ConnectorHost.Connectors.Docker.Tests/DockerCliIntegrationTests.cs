using System.Diagnostics;
using Nerv.IIP.ConnectorHost.TestUtilities;

namespace Nerv.IIP.ConnectorHost.Connectors.Docker.Tests;

[Collection(ConnectorTimeoutCollection.Name)]
public sealed class DockerCliIntegrationTests
{
    private static readonly TimeSpan DockerCommandBudget = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DockerCleanupBudget = TimeSpan.FromSeconds(10);

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
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var exitTask = process.WaitForExitAsync();
        var completionTask = Task.WhenAll(stdoutTask, stderrTask, exitTask);
        var command = $"docker {string.Join(' ', arguments)}";
        try
        {
            await BoundedObservation.ObserveAsync(
                completionTask,
                $"{command} to exit and drain redirected output",
                () => $"process exited={HasExited(process)}, stdout={stdoutTask.Status}, stderr={stderrTask.Status}",
                DockerCommandBudget);
        }
        catch (Xunit.Sdk.XunitException)
        {
            if (!HasExited(process))
            {
                process.Kill(entireProcessTree: true);
            }

            try
            {
                await BoundedObservation.ObserveAsync(
                    completionTask,
                    "the timed-out Docker CLI process tree to stop and drain redirected output",
                    () => $"process exited={HasExited(process)}, stdout={stdoutTask.Status}, stderr={stderrTask.Status}",
                    DockerCleanupBudget);
            }
            catch (Exception cleanupException) when (cleanupException is Xunit.Sdk.XunitException
                or IOException
                or InvalidOperationException)
            {
                // Preserve the primary command timeout, whose diagnostic includes command/output state.
            }

            throw;
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (!allowFailure && process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{command} failed with exit code {process.ExitCode}. stdout: {stdout} stderr: {stderr}");
        }
    }

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}

internal sealed class DockerCliFactAttribute : FactAttribute
{
    public DockerCliFactAttribute()
    {
        Timeout = ConnectorTimeoutCollection.TestTimeoutMilliseconds;

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

            if (process.WaitForExit(5000))
            {
                return process.ExitCode == 0;
            }

            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
            return false;
        }
        catch
        {
            return false;
        }
    }
}
