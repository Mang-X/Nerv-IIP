using System.Diagnostics;

namespace Nerv.IIP.ConnectorHost.Connectors.OpcUa.Tests;

public sealed class OpcUaSimulatorIntegrationTests
{
    private const string OpcPlcImage = "mcr.microsoft.com/iotedge/opc-plc:latest";

    [OpcUaSimulatorFact]
    public async Task Opcua_connector_subscribes_microsoft_opc_plc_and_posts_bucketed_sample()
    {
        var containerName = $"nerv-opcplc-{Guid.NewGuid():N}"[..32];
        await RunDockerAsync("pull", OpcPlcImage);

        try
        {
            await RunDockerAsync(
                "run",
                "-d",
                "--rm",
                "--name",
                containerName,
                "-p",
                "50000:50000",
                "-p",
                "18080:8080",
                OpcPlcImage,
                "--pn=50000",
                "--autoaccept",
                "--sph",
                "--sn=2",
                "--sr=1",
                "--st=uint",
                "--fn=2",
                "--fr=1",
                "--ft=uint",
                "--gn=1");
            await WaitForOpcPlcAsync(containerName);

            var samples = new RecordingIndustrialTelemetrySamplesClient();
            using var opcUaClient = new OpcUaNetStandardClient(new EnvironmentOpcUaCredentialResolver());
            var connector = new OpcUaConnector(
                new OpcUaConnectorOptions(
                    ConnectorId: "opcua-smoke",
                    ConnectorHostId: "connector-host-smoke",
                    OrganizationId: "org-001",
                    EnvironmentId: "env-dev",
                    EndpointUrl: "opc.tcp://localhost:50000",
                    SecurityPolicy: "Basic256Sha256",
                    SecurityMode: "SignAndEncrypt",
                    CredentialReference: null,
                    BrowseRootNodeId: "ns=3;s=OpcPlc",
                    Tags:
                    [
                        new OpcUaTagSubscription(
                            DeviceAssetId: "device-opcplc-1",
                            TagKey: "fast-uint-1",
                            NodeId: "ns=3;s=FastUInt1",
                            SamplingIntervalMilliseconds: 500,
                            BucketSeconds: 1)
                    ],
                    MaxReconnectAttempts: 1,
                    AutoAcceptUntrustedServerCertificates: true),
                opcUaClient,
                samples,
                () => DateTimeOffset.UtcNow);

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            await connector.RunCollectionCycleAsync(timeout.Token);

            Assert.NotEmpty(samples.Requests);
            Assert.All(samples.Requests, request =>
            {
                Assert.Equal("org-001", request.OrganizationId);
                Assert.Equal("env-dev", request.EnvironmentId);
                Assert.Equal("device-opcplc-1", request.DeviceAssetId);
                Assert.Equal("fast-uint-1", request.TagKey);
                Assert.Equal("opcua", request.SourceSystem);
                Assert.Equal("connector-host-smoke/opcua-smoke", request.SourceConnector);
                Assert.StartsWith("opcua:opcua-smoke:fast-uint-1:", request.SourceSequence, StringComparison.Ordinal);
                Assert.True(request.SampleCount > 0);
            });
            Assert.Equal(samples.Requests.Count, connector.CurrentState.PostedBuckets);
            Assert.Equal(0, connector.CurrentState.DroppedSamples);
            Assert.Equal("healthy", connector.CurrentState.HealthStatus);
        }
        finally
        {
            await RunDockerAllowFailureAsync("rm", "-f", containerName);
        }
    }

    private static async Task WaitForOpcPlcAsync(string containerName)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        string logs = string.Empty;
        while (DateTimeOffset.UtcNow < deadline)
        {
            logs = await RunDockerForOutputAsync(["logs", "--tail", "80", containerName], allowFailure: true);
            if (logs.Contains("OPC UA Server started", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        throw new InvalidOperationException($"OPC PLC simulator did not start before timeout. logs: {logs}");
    }

    private static async Task RunDockerAsync(params string[] arguments)
    {
        _ = await RunDockerForOutputAsync(arguments, allowFailure: false);
    }

    private static async Task RunDockerAllowFailureAsync(params string[] arguments)
    {
        _ = await RunDockerForOutputAsync(arguments, allowFailure: true);
    }

    private static async Task<string> RunDockerForOutputAsync(IReadOnlyList<string> arguments, bool allowFailure)
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

        return $"{stdout}{Environment.NewLine}{stderr}";
    }

    private sealed class RecordingIndustrialTelemetrySamplesClient : IIndustrialTelemetrySamplesClient
    {
        public List<RecordIndustrialTelemetrySampleRequest> Requests { get; } = [];

        public Task RecordSampleAsync(RecordIndustrialTelemetrySampleRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.CompletedTask;
        }
    }
}

internal sealed class OpcUaSimulatorFactAttribute : FactAttribute
{
    public OpcUaSimulatorFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("NERV_IIP_OPCUA_SIMULATOR_INTEGRATION"), "1", StringComparison.Ordinal))
        {
            Skip = "Set NERV_IIP_OPCUA_SIMULATOR_INTEGRATION=1 to run the real OPC UA simulator integration test.";
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
