using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace Nerv.IIP.ConnectorHost.Host.Tests;

public sealed class SimulatedConnectorHostProcessTests
{
    private static readonly string[] CanonicalConnectorIds =
        ["CONN-OPCUA-01", "CONN-MQTT-01", "CONN-MODBUS-01"];

    [Fact(Timeout = 30_000)]
    public async Task Built_host_process_reports_three_simulated_connectors_and_executes_control()
    {
        await using var platform = await LoopbackPlatform.StartAsync();
        var executable = Path.Combine(
            AppContext.BaseDirectory,
            "Nerv.IIP.ConnectorHost.Host");
        Assert.True(File.Exists(executable), $"Built Host executable not found at '{executable}'.");

        using var process = StartHost(executable, platform.BaseAddress);
        var usedForcedCleanup = false;
        try
        {
            await platform.WaitForEvidenceAsync(TimeSpan.FromSeconds(15));

            Assert.Equal(
                CanonicalConnectorIds.Order(StringComparer.Ordinal),
                platform.CanonicalRegistrations.Keys.Order(StringComparer.Ordinal));
            Assert.All(
                CanonicalConnectorIds,
                connectorId => Assert.Equal(1, platform.CanonicalRegistrations[connectorId]));
            Assert.All(
                CanonicalConnectorIds,
                connectorId => Assert.True(
                    platform.Heartbeats.GetValueOrDefault(connectorId) >= 2,
                    $"Expected at least two heartbeats for {connectorId}."));
            Assert.All(
                CanonicalConnectorIds,
                connectorId => Assert.True(
                    platform.CollectionHealthSnapshots.GetValueOrDefault(connectorId) >= 1,
                    $"Expected CollectionHealth state for {connectorId}."));
            Assert.Equal(
                ["modbus", "mqtt", "opcua"],
                platform.TelemetrySources.Keys.Order(StringComparer.Ordinal));
            Assert.Equal(
                new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    ["CONN-OPCUA-01"] = 44,
                    ["CONN-MQTT-01"] = 28,
                    ["CONN-MODBUS-01"] = 24
                },
                platform.ManifestTagCounts);
            Assert.True(platform.ControlClaimed);
            Assert.True(platform.GoodCorrelatedResult);
            Assert.Empty(platform.Errors);
        }
        finally
        {
            usedForcedCleanup = await StopExactProcessAsync(process);
        }

        Assert.False(
            usedForcedCleanup,
            "Host did not stop after SIGTERM and required exact-child kill.");
        Assert.True(
            process.HasExited,
            "Host process did not exit within the cleanup deadline.");
    }

    private static Process StartHost(string executable, Uri platformBaseAddress)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false
        };
        start.Environment["DOTNET_ENVIRONMENT"] = "Development";
        start.Environment["Platform__AppHubBaseUrl"] = platformBaseAddress.ToString();
        start.Environment["Platform__OpsBaseUrl"] = platformBaseAddress.ToString();
        start.Environment["Platform__IndustrialTelemetryBaseUrl"] = platformBaseAddress.ToString();
        start.Environment["ConnectorHost__ConnectorHostId"] = "connector-host-001";
        start.Environment["ConnectorHost__ConnectorSecret"] = "process-test-connector-secret";
        start.Environment["ConnectorHost__OrganizationId"] = "org-001";
        start.Environment["ConnectorHost__EnvironmentId"] = "env-dev";
        start.Environment["ConnectorHost__HeartbeatSeconds"] = "2";
        start.Environment["ConnectorHost__ConnectionProbeSeconds"] = "4";
        start.Environment["ConnectorHost__CollectionCycleSeconds"] = "1";
        start.Environment["ConnectorHost__OperationPollSeconds"] = "1";
        start.Environment["ConnectorHost__ConnectionDetectionBudgetSeconds"] = "4";
        start.Environment["ConnectorHost__BackendDeadlineSeconds"] = "8";
        start.Environment["InternalService__BearerToken"] = "process-test-internal-token";
        start.Environment["Logging__LogLevel__Default"] = "Warning";
        start.Environment["Logging__LogLevel__Microsoft.Hosting.Lifetime"] = "Warning";
        start.Environment["Simulated__Enabled"] = "true";
        start.Environment["Simulated__Phases__Normal"] = "00:00:01";
        start.Environment["Simulated__Phases__Degrading"] = "00:00:01";
        start.Environment["Simulated__Phases__Alarm"] = "00:00:01";
        start.Environment["Simulated__Phases__Recovered"] = "00:00:01";
        return Process.Start(start)
            ?? throw new InvalidOperationException("Failed to start built Connector Host process.");
    }

    private static async Task<bool> StopExactProcessAsync(Process process)
    {
        if (process.HasExited)
        {
            return false;
        }

        using var signal = Process.Start(new ProcessStartInfo("/bin/kill")
        {
            UseShellExecute = false,
            ArgumentList = { "-TERM", process.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) }
        });
        if (signal is not null)
        {
            await signal.WaitForExitAsync();
        }

        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            return false;
        }
        catch (TimeoutException)
        {
            process.Kill(entireProcessTree: false);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            return true;
        }
    }

    private sealed class LoopbackPlatform : IAsyncDisposable
    {
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _shutdown = new();
        private readonly Task _serveTask;
        private int _controlClaimed;
        private int _goodCorrelatedResult;

        private LoopbackPlatform(HttpListener listener, Uri baseAddress)
        {
            _listener = listener;
            BaseAddress = baseAddress;
            _serveTask = ServeAsync();
        }

        public Uri BaseAddress { get; }
        public ConcurrentDictionary<string, int> CanonicalRegistrations { get; } = new(StringComparer.Ordinal);
        public ConcurrentDictionary<string, int> Heartbeats { get; } = new(StringComparer.Ordinal);
        public ConcurrentDictionary<string, int> CollectionHealthSnapshots { get; } = new(StringComparer.Ordinal);
        public ConcurrentDictionary<string, int> TelemetrySources { get; } = new(StringComparer.Ordinal);
        public ConcurrentDictionary<string, int> ManifestTagCounts { get; } = new(StringComparer.Ordinal);
        public ConcurrentBag<string> Errors { get; } = [];
        public bool ControlClaimed => Volatile.Read(ref _controlClaimed) != 0;
        public bool GoodCorrelatedResult => Volatile.Read(ref _goodCorrelatedResult) != 0;

        public static Task<LoopbackPlatform> StartAsync()
        {
            var port = ReservePort();
            var baseAddress = new Uri($"http://127.0.0.1:{port}/");
            var listener = new HttpListener();
            listener.Prefixes.Add(baseAddress.ToString());
            listener.Start();
            return Task.FromResult(new LoopbackPlatform(listener, baseAddress));
        }

        public async Task WaitForEvidenceAsync(TimeSpan timeout)
        {
            using var deadline = new CancellationTokenSource(timeout);
            while (!HasAllEvidence())
            {
                if (deadline.IsCancellationRequested)
                {
                    throw new Xunit.Sdk.XunitException(
                        $"Timed out waiting for process evidence. registrations={Format(CanonicalRegistrations)}, "
                        + $"heartbeats={Format(Heartbeats)}, health={Format(CollectionHealthSnapshots)}, "
                        + $"sources={Format(TelemetrySources)}, claimed={ControlClaimed}, "
                        + $"goodResult={GoodCorrelatedResult}, errors={string.Join(" | ", Errors)}");
                }

                await Task.Delay(50, deadline.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }

        public async ValueTask DisposeAsync()
        {
            _shutdown.Cancel();
            _listener.Stop();
            try
            {
                await _serveTask;
            }
            catch (Exception) when (_shutdown.IsCancellationRequested)
            {
            }

            _listener.Close();
            _shutdown.Dispose();
        }

        private bool HasAllEvidence() =>
            CanonicalConnectorIds.All(id => CanonicalRegistrations.GetValueOrDefault(id) == 1)
            && CanonicalConnectorIds.All(id => Heartbeats.GetValueOrDefault(id) >= 2)
            && CanonicalConnectorIds.All(id => CollectionHealthSnapshots.GetValueOrDefault(id) >= 1)
            && CanonicalConnectorIds.All(id => ManifestTagCounts.ContainsKey(id))
            && new[] { "opcua", "mqtt", "modbus" }.All(source => TelemetrySources.ContainsKey(source))
            && ControlClaimed
            && GoodCorrelatedResult;

        private async Task ServeAsync()
        {
            while (!_shutdown.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync().WaitAsync(_shutdown.Token);
                }
                catch (Exception) when (_shutdown.IsCancellationRequested)
                {
                    break;
                }

                _ = HandleAsync(context);
            }
        }

        private async Task HandleAsync(HttpListenerContext context)
        {
            try
            {
                using var document = await JsonDocument.ParseAsync(
                    context.Request.InputStream,
                    cancellationToken: _shutdown.Token);
                var root = document.RootElement;
                object response = new { success = true, message = "ok", code = 0, data = new { } };
                switch (context.Request.Url?.AbsolutePath)
                {
                    case "/api/connectors/v1/registrations":
                    {
                        var instanceKey = root.GetProperty("instanceKey").GetString()!;
                        if (CanonicalConnectorIds.Contains(instanceKey, StringComparer.Ordinal))
                        {
                            CanonicalRegistrations.AddOrUpdate(instanceKey, 1, static (_, count) => count + 1);
                        }

                        response = new
                        {
                            success = true,
                            message = "registered",
                            code = 0,
                            data = new
                            {
                                registrationId = $"registration-{instanceKey}",
                                instanceKey,
                                ingestionToken = $"ingestion-{instanceKey}"
                            }
                        };
                        break;
                    }
                    case "/api/connectors/v1/heartbeats":
                    {
                        var instanceKey = root.GetProperty("instanceKey").GetString()!;
                        if (CanonicalConnectorIds.Contains(instanceKey, StringComparer.Ordinal))
                        {
                            Heartbeats.AddOrUpdate(instanceKey, 1, static (_, count) => count + 1);
                        }

                        break;
                    }
                    case "/api/connectors/v1/state-snapshots":
                    {
                        var instanceKey = root.GetProperty("instanceKey").GetString()!;
                        if (CanonicalConnectorIds.Contains(instanceKey, StringComparer.Ordinal)
                            && root.TryGetProperty("collectionHealth", out var health)
                            && health.ValueKind == JsonValueKind.Object)
                        {
                            CollectionHealthSnapshots.AddOrUpdate(instanceKey, 1, static (_, count) => count + 1);
                        }

                        break;
                    }
                    case "/api/business/v1/iiot/samples":
                    {
                        var source = root.GetProperty("sourceSystem").GetString()!;
                        TelemetrySources.AddOrUpdate(source, 1, static (_, count) => count + 1);
                        break;
                    }
                    case "/api/business/v1/iiot/connector-tag-manifests":
                    {
                        ManifestTagCounts[root.GetProperty("collectionConnectorId").GetString()!] =
                            root.GetProperty("entries").GetArrayLength();
                        response = new
                        {
                            data = new
                            {
                                disposition = "accepted",
                                acceptedManifestRevision = root.GetProperty("manifestRevision").GetString(),
                                acceptedManifestObservedAtUtc = root.GetProperty("manifestObservedAtUtc").GetDateTimeOffset()
                            }
                        };
                        break;
                    }
                    case "/api/ops/v1/operation-tasks/claims":
                    {
                        var firstClaim = Interlocked.CompareExchange(ref _controlClaimed, 1, 0) == 0;
                        response = new
                        {
                            success = true,
                            message = "claimed",
                            code = 0,
                            data = new
                            {
                                items = firstClaim ? new[]
                                {
                                    new
                                    {
                                        operationTaskId = "process-control-001",
                                        attemptId = "process-attempt-001",
                                        organizationId = "org-001",
                                        environmentId = "env-dev",
                                        connectorHostId = "connector-host-001",
                                        instanceKey = "CONN-OPCUA-01",
                                        operationCode = "device.control.command",
                                        correlationId = "process-correlation-001",
                                        parameters = new Dictionary<string, string>
                                        {
                                            ["commandType"] = "start-stop",
                                            ["deviceAssetId"] = "DEV-CNC-01",
                                            ["value"] = "stop"
                                        },
                                        leaseId = "process-lease-001",
                                        leasedAtUtc = DateTimeOffset.Parse("2026-07-26T00:00:00Z"),
                                        leasedUntilUtc = DateTimeOffset.Parse("2026-07-26T00:05:00Z"),
                                        attemptNo = 1,
                                        leaseDurationSeconds = 300,
                                        maxAttempts = 3
                                    }
                                } : []
                            }
                        };
                        break;
                    }
                    case "/api/ops/v1/operation-results":
                    {
                        if (root.GetProperty("operationTaskId").GetString() == "process-control-001"
                            && root.GetProperty("context").GetProperty("correlationId").GetString() == "process-correlation-001"
                            && root.GetProperty("executionStatus").GetString() == "succeeded"
                            && root.GetProperty("output").GetProperty("deviceReceiptCode").GetString() == "Good")
                        {
                            Interlocked.Exchange(ref _goodCorrelatedResult, 1);
                        }

                        break;
                    }
                }

                var payload = JsonSerializer.SerializeToUtf8Bytes(response);
                context.Response.StatusCode = (int)HttpStatusCode.OK;
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = payload.Length;
                await context.Response.OutputStream.WriteAsync(payload, _shutdown.Token);
                context.Response.Close();
            }
            catch (Exception ex)
            {
                Errors.Add($"{context.Request.Url?.AbsolutePath}: {ex.GetType().Name}: {ex.Message}");
                context.Response.Abort();
            }
        }

        private static int ReservePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static string Format(ConcurrentDictionary<string, int> values) =>
            string.Join(",", values.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}:{pair.Value}"));
    }
}
