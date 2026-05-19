using System.Diagnostics;
using System.Text.Json;

namespace Nerv.IIP.ConnectorHost.Connectors.Docker;

public sealed record DockerCliContainer(string ContainerId, string Image, string Name, string State, string Status);

public sealed record DockerCliCommandResult(int? ExitCode, string Stdout, string Stderr, bool TimedOut)
{
    public static DockerCliCommandResult Succeeded(string stdout = "", string stderr = "") => new(0, stdout, stderr, false);
    public static DockerCliCommandResult Failed(int exitCode, string stdout, string stderr) => new(exitCode, stdout, stderr, false);
    public static DockerCliCommandResult Timeout(string stderr, string stdout = "") => new(null, stdout, stderr, true);
}

public sealed class DockerCliException(string message, DockerCliCommandResult result) : Exception(message)
{
    public DockerCliCommandResult Result { get; } = result;
}

public interface IDockerCli
{
    Task<IReadOnlyList<DockerCliContainer>> ListContainersAsync(CancellationToken cancellationToken);
    Task<DockerCliCommandResult> RestartContainerAsync(string containerName, int gracePeriodSeconds, TimeSpan commandTimeout, CancellationToken cancellationToken);
}

public sealed class DockerCli(IDockerProcessRunner? processRunner = null) : IDockerCli
{
    private readonly IDockerProcessRunner _processRunner = processRunner ?? new DockerProcessRunner();

    public async Task<IReadOnlyList<DockerCliContainer>> ListContainersAsync(CancellationToken cancellationToken)
    {
        var result = await _processRunner.RunAsync(
            "docker",
            ["container", "ls", "--all", "--no-trunc", "--format", "{{json .}}"],
            TimeSpan.FromSeconds(15),
            cancellationToken);

        if (result.TimedOut)
        {
            throw new DockerCliException("Docker container discovery timed out.", result);
        }

        if (result.ExitCode != 0)
        {
            throw new DockerCliException($"Docker container discovery failed with exit code {result.ExitCode}.", result);
        }

        return result.Stdout
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(ParseContainer)
            .ToList();
    }

    public Task<DockerCliCommandResult> RestartContainerAsync(string containerName, int gracePeriodSeconds, TimeSpan commandTimeout, CancellationToken cancellationToken)
    {
        return _processRunner.RunAsync(
            "docker",
            ["restart", "--time", gracePeriodSeconds.ToString(), containerName],
            commandTimeout,
            cancellationToken);
    }

    private static DockerCliContainer ParseContainer(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return new DockerCliContainer(
            GetString(root, "ID"),
            GetString(root, "Image"),
            NormalizeName(GetString(root, "Names")),
            GetString(root, "State"),
            GetString(root, "Status"));
    }

    private static string GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var property) ? property.GetString() ?? "" : "";
    }

    private static string NormalizeName(string names)
    {
        var name = names.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? names;
        return name.Trim().TrimStart('/');
    }
}

public interface IDockerProcessRunner
{
    Task<DockerCliCommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken);
}

public sealed class DockerProcessRunner : IDockerProcessRunner
{
    public async Task<DockerCliCommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = CreateStartInfo(fileName, arguments),
            EnableRaisingEvents = true
        };

        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeoutTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutTokenSource.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutTokenSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            var timeoutStdout = await ReadCompletedOutputAsync(stdoutTask);
            var timeoutStderr = await ReadCompletedOutputAsync(stderrTask);
            return DockerCliCommandResult.Timeout(timeoutStderr, timeoutStdout);
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        return new DockerCliCommandResult(process.ExitCode, stdout.Trim(), stderr.Trim(), false);
    }

    private static ProcessStartInfo CreateStartInfo(string fileName, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static async Task<string> ReadCompletedOutputAsync(Task<string> outputTask)
    {
        var completed = await Task.WhenAny(outputTask, Task.Delay(TimeSpan.FromSeconds(1)));
        return completed == outputTask ? (await outputTask).Trim() : "";
    }
}
