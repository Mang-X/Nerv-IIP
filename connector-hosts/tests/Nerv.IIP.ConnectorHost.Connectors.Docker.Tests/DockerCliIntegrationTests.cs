using System.Diagnostics;
using System.Runtime.ExceptionServices;
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
        await DockerContainerLifecycle.RunAsync(
            containerName,
            RunDockerCommandAsync,
            async () =>
            {
                var connector = new DockerConnector(new DockerCli());

                var target = (await connector.DiscoverAsync(CancellationToken.None)).Single(x => x.InstanceKey == $"docker-container-{containerName}");
                var result = await connector.ExecuteAsync(DockerConnectorOperationTests.CreateTaskForIntegration(target.InstanceKey), CancellationToken.None);

                Assert.Equal(containerName, target.InstanceName);
                Assert.True(result.Succeeded);
                Assert.Equal("restart completed", result.Output["message"]);
                Assert.Equal(containerName, result.Output["containerName"]);
            });
    }

    private static async Task RunDockerAsync(params string[] arguments)
    {
        await RunDockerCommandAsync(arguments, allowFailure: false, DockerCommandBudget);
    }

    private static async Task<DockerCommandResult> RunDockerCommandAsync(
        IReadOnlyList<string> arguments,
        bool allowFailure,
        TimeSpan budget)
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
                budget);
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

        return new DockerCommandResult(process.ExitCode, stdout);
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

internal readonly record struct DockerCommandResult(int ExitCode, string StandardOutput);

internal delegate Task<DockerCommandResult> DockerCommandRunner(
    IReadOnlyList<string> arguments,
    bool allowFailure,
    TimeSpan budget);

internal static class DockerContainerLifecycle
{
    private static readonly TimeSpan DockerCommandBudget = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan CleanupCommandBudget = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan CleanupSweepInterval = TimeSpan.FromMilliseconds(250);

    public static async Task RunAsync(
        string containerName,
        DockerCommandRunner runDockerAsync,
        Func<Task> body,
        int cleanupSweepCount = 5,
        Func<TimeSpan, Task>? delayAsync = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cleanupSweepCount);
        delayAsync ??= delay => Task.Delay(delay);

        Exception? scenarioException = null;
        try
        {
            await runDockerAsync(
                ["container", "create", "--name", containerName, "alpine:3.20", "sleep", "300"],
                allowFailure: false,
                DockerCommandBudget);
            await runDockerAsync(
                ["container", "start", containerName],
                allowFailure: false,
                DockerCommandBudget);
            await body();
        }
        catch (Exception exception)
        {
            scenarioException = exception;
        }

        Exception? cleanupException = null;
        try
        {
            await EnsureAbsentAsync(
                containerName,
                runDockerAsync,
                cleanupSweepCount,
                delayAsync);
        }
        catch (Exception exception)
        {
            cleanupException = exception;
        }

        if (scenarioException is not null && cleanupException is not null)
        {
            throw new AggregateException(
                "The Docker container scenario failed and its exact-name cleanup could not be confirmed.",
                scenarioException,
                cleanupException);
        }

        if (scenarioException is not null)
        {
            ExceptionDispatchInfo.Capture(scenarioException).Throw();
        }

        if (cleanupException is not null)
        {
            ExceptionDispatchInfo.Capture(cleanupException).Throw();
        }
    }

    private static async Task EnsureAbsentAsync(
        string containerName,
        DockerCommandRunner runDockerAsync,
        int cleanupSweepCount,
        Func<TimeSpan, Task> delayAsync)
    {
        for (var attempt = 1; attempt <= cleanupSweepCount; attempt++)
        {
            try
            {
                await runDockerAsync(
                    ["container", "rm", "--force", containerName],
                    allowFailure: true,
                    CleanupCommandBudget);
            }
            catch (Exception) when (attempt < cleanupSweepCount)
            {
                // A later sweep can still remove a daemon-side create that outlived its CLI process.
            }

            if (attempt < cleanupSweepCount)
            {
                await delayAsync(CleanupSweepInterval);
            }
        }

        var remaining = await runDockerAsync(
            ["container", "ls", "--all", "--quiet", "--filter", $"name=^/{containerName}$"],
            allowFailure: false,
            CleanupCommandBudget);
        if (!string.IsNullOrWhiteSpace(remaining.StandardOutput))
        {
            throw new Xunit.Sdk.XunitException(
                $"Docker cleanup did not remove the exact container '{containerName}' after "
                + $"{cleanupSweepCount} bounded sweeps.");
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
