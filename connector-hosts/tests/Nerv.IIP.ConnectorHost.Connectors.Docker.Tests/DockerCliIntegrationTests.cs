using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Nerv.IIP.ConnectorHost.TestUtilities;

namespace Nerv.IIP.ConnectorHost.Connectors.Docker.Tests;

[Collection(ConnectorTimeoutCollection.Name)]
public sealed class DockerCliIntegrationTests
{
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
        await RunDockerCommandAsync(arguments, allowFailure: false, DockerCliIntegrationBudget.CommandBudget);
    }

    private static async Task<DockerCommandResult> RunDockerCommandAsync(
        IReadOnlyList<string> arguments,
        bool allowFailure,
        TimeSpan budget)
    {
        if (budget <= DockerCliIntegrationBudget.ProcessDrainBudget)
        {
            throw new ArgumentOutOfRangeException(
                nameof(budget),
                budget,
                $"Docker command budget must exceed the {DockerCliIntegrationBudget.ProcessDrainBudget} process-drain reserve.");
        }

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
                budget - DockerCliIntegrationBudget.ProcessDrainBudget);
        }
        catch (Xunit.Sdk.XunitException)
        {
            try
            {
                if (!HasExited(process))
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception killException) when (killException is InvalidOperationException
                or System.ComponentModel.Win32Exception
                or NotSupportedException)
            {
                // The command timeout remains primary; the bounded drain below still gets a chance.
            }

            try
            {
                await BoundedObservation.ObserveAsync(
                    completionTask,
                    "the timed-out Docker CLI process tree to stop and drain redirected output",
                    () => $"process exited={HasExited(process)}, stdout={stdoutTask.Status}, stderr={stderrTask.Status}",
                    DockerCliIntegrationBudget.ProcessDrainBudget);
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

internal static class DockerCliIntegrationBudget
{
    public const int TestTimeoutMilliseconds = 480_000;
    public const int CleanupSweepCount = 5;
    public static readonly TimeSpan CommandBudget = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan ProcessDrainBudget = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan ConnectorBodyBudget = TimeSpan.FromSeconds(66);
    public static readonly TimeSpan CleanupCommandBudget = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan CleanupSweepInterval = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan MaximumSetupBudget = CommandBudget + CommandBudget;
    public static readonly TimeSpan MaximumCleanupBudget = TimeSpan.FromTicks(
        CleanupCommandBudget.Ticks * 2 * CleanupSweepCount
        + CleanupSweepInterval.Ticks * (CleanupSweepCount - 1));

    public static readonly TimeSpan MaximumInternalBudget =
        // pull + create/start + connector discovery/restart + exact-name stable cleanup
        CommandBudget
        + MaximumSetupBudget
        + ConnectorBodyBudget
        + MaximumCleanupBudget;
}

internal delegate Task<DockerCommandResult> DockerCommandRunner(
    IReadOnlyList<string> arguments,
    bool allowFailure,
    TimeSpan budget);

internal static class DockerContainerLifecycle
{
    private const int DefaultCleanupSweepCount = DockerCliIntegrationBudget.CleanupSweepCount;
    private const int RequiredStableAbsenceObservations = 3;
    private static readonly TimeSpan DockerCommandBudget = DockerCliIntegrationBudget.CommandBudget;
    private static readonly TimeSpan CleanupCommandBudget = DockerCliIntegrationBudget.CleanupCommandBudget;
    private static readonly TimeSpan CleanupSweepInterval = DockerCliIntegrationBudget.CleanupSweepInterval;

    public static async Task RunAsync(
        string containerName,
        DockerCommandRunner runDockerAsync,
        Func<Task> body,
        Func<TimeSpan, Task>? delayAsync = null)
    {
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
                delayAsync);
        }
        catch (Exception exception)
        {
            cleanupException = exception;
        }

        if (scenarioException is not null && cleanupException is not null)
        {
            scenarioException.Data["DockerCleanupException"] = cleanupException;
            ExceptionDispatchInfo.Capture(scenarioException).Throw();
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
        Func<TimeSpan, Task> delayAsync)
    {
        var consecutiveAbsentObservations = 0;
        Exception? lastCleanupException = null;
        for (var attempt = 1; attempt <= DefaultCleanupSweepCount; attempt++)
        {
            var removedExactResource = false;
            try
            {
                var removal = await runDockerAsync(
                    ["container", "rm", "--force", containerName],
                    allowFailure: true,
                    CleanupCommandBudget);
                removedExactResource = !string.IsNullOrWhiteSpace(removal.StandardOutput);
            }
            catch (Exception exception)
            {
                lastCleanupException = exception;
                consecutiveAbsentObservations = 0;
            }

            try
            {
                var remaining = await runDockerAsync(
                    ["container", "ls", "--all", "--quiet", "--filter", $"name=^/{containerName}$"],
                    allowFailure: false,
                    CleanupCommandBudget);
                if (string.IsNullOrWhiteSpace(remaining.StandardOutput))
                {
                    consecutiveAbsentObservations = removedExactResource
                        ? 1
                        : consecutiveAbsentObservations + 1;
                }
                else
                {
                    consecutiveAbsentObservations = 0;
                }
            }
            catch (Exception exception)
            {
                lastCleanupException = exception;
                consecutiveAbsentObservations = 0;
            }

            if (attempt < DefaultCleanupSweepCount)
            {
                await delayAsync(CleanupSweepInterval);
            }
        }

        if (consecutiveAbsentObservations >= RequiredStableAbsenceObservations)
        {
            return;
        }

        if (lastCleanupException is not null)
        {
            ExceptionDispatchInfo.Capture(lastCleanupException).Throw();
        }

        throw new Xunit.Sdk.XunitException(
            $"Docker cleanup could not confirm {RequiredStableAbsenceObservations} stable exact-name "
            + $"absence observations for '{containerName}' within {DefaultCleanupSweepCount} bounded sweeps.");
    }
}

internal sealed class DockerCliFactAttribute : FactAttribute
{
    public DockerCliFactAttribute()
    {
        Timeout = DockerCliIntegrationBudget.TestTimeoutMilliseconds;

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
