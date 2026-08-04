using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Nerv.IIP.ConnectorHost.Host.Tests;

public sealed class SimulatedConnectorHostProcessTests : IDisposable
{
    private static readonly string[] CanonicalConnectorIds =
        ["CONN-OPCUA-01", "CONN-MQTT-01", "CONN-MODBUS-01"];

    private static readonly TimeSpan SignalDeliveryBudget = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan GracefulStopBudget = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ForcedStopBudget = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan OutputDrainBudget = TimeSpan.FromSeconds(5);

    private HostProcess? _hostProcess;

    /// <summary>
    /// Last-resort reclamation. xUnit's per-test <c>Timeout</c> abandons the test task, so the
    /// <c>finally</c> block inside the test is not guaranteed to run; the test-class instance is
    /// still disposed. Anything the test leaked is force-killed here, tree included, so the test
    /// host can always exit and <c>dotnet test</c> can always return.
    /// </summary>
    public void Dispose()
    {
        var hostProcess = Interlocked.Exchange(ref _hostProcess, null);
        hostProcess?.Dispose();
    }

    [Fact]
    public void Built_host_executable_resolves_for_the_current_platform()
    {
        var executable = ResolveHostExecutablePath();

        Assert.True(
            File.Exists(executable),
            $"Built Host executable not found at '{executable}'.");
    }

    [UnixHostProcessFact(Timeout = 30_000)]
    public async Task Built_host_process_reports_three_simulated_connectors_and_executes_control()
    {
        await using var platform = await LoopbackPlatform.StartAsync();
        var executable = ResolveHostExecutablePath();
        Assert.True(File.Exists(executable), $"Built Host executable not found at '{executable}'.");

        using var host = StartHost(executable, platform.BaseAddress);
        _hostProcess = host;
        var stopOutcome = HostStopOutcome.Graceful;
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
            stopOutcome = await host.StopAsync();
            Interlocked.Exchange(ref _hostProcess, null);
        }

        // Drained once, before the assertions, so both failure messages carry the real Host output
        // instead of re-paying a bounded drain inside every interpolated assertion message.
        var output = await host.DrainOutputAsync();
        Assert.True(
            stopOutcome == HostStopOutcome.Graceful,
            $"Host did not stop after SIGTERM and required a forced kill. Host output: {output}");
        Assert.True(
            host.HasExited,
            $"Host process did not exit within the cleanup deadline. Host output: {output}");
    }

    private static string ResolveHostExecutablePath() =>
        Path.Combine(
            AppContext.BaseDirectory,
            OperatingSystem.IsWindows()
                ? "Nerv.IIP.ConnectorHost.Host.exe"
                : "Nerv.IIP.ConnectorHost.Host");

    private static HostProcess StartHost(string executable, Uri platformBaseAddress)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false,

            // The child must not inherit the test host's console handles. An inherited stdout /
            // stderr keeps the test host's own output pipe open for as long as the child (or any
            // descendant of it) lives, which makes the test runner wait for EOF that never
            // arrives. Redirecting also stops a full pipe from stalling the Host mid-run.
            RedirectStandardOutput = true,
            RedirectStandardError = true
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
        var process = Process.Start(start)
            ?? throw new InvalidOperationException("Failed to start built Connector Host process.");
        return new HostProcess(process);
    }

    private enum HostStopOutcome
    {
        /// <summary>The Host exited on its own after SIGTERM.</summary>
        Graceful,

        /// <summary>The Host outlived the graceful budget and its process tree was killed.</summary>
        ForcedKill
    }

    /// <summary>
    /// Owns the built Host child process and its redirected pipes, and guarantees both are
    /// reclaimed on every exit path: normal completion, assertion failure, and an abandoned
    /// (timed-out) test where only <see cref="IDisposable.Dispose"/> still runs.
    /// </summary>
    private sealed class HostProcess : IDisposable
    {
        private readonly Process _process;
        private readonly StringBuilder _stdoutBuffer = new();
        private readonly StringBuilder _stderrBuffer = new();
        private readonly Task _stdout;
        private readonly Task _stderr;
        private int _disposed;

        public HostProcess(Process process)
        {
            _process = process;

            // The pumps never fault: a broken/disposed pipe is folded into the buffer as a marker.
            // That is what makes the reader tasks deterministically observed even when `Dispose`
            // closes the process handle while a read is still pending — an unobserved
            // `ObjectDisposedException` from a reader would otherwise surface later, on the
            // finalizer thread, as an unrelated test failure.
            _stdout = PumpAsync(process.StandardOutput, _stdoutBuffer);
            _stderr = PumpAsync(process.StandardError, _stderrBuffer);
        }

        public bool HasExited => _process.HasExited;

        /// <summary>
        /// Sends SIGTERM and reports how the Host actually went down. Every wait is bounded, so
        /// this cannot park the test; the SIGTERM helper process is bounded too, because it is a
        /// child of the test host and would otherwise be one more unbounded await.
        /// </summary>
        public async Task<HostStopOutcome> StopAsync()
        {
            var process = _process;
            if (process.HasExited)
            {
                return HostStopOutcome.Graceful;
            }

            using var signal = Process.Start(new ProcessStartInfo("/bin/kill")
            {
                UseShellExecute = false,
                ArgumentList = { "-TERM", process.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) }
            });
            if (signal is not null)
            {
                try
                {
                    await signal.WaitForExitAsync().WaitAsync(SignalDeliveryBudget);
                }
                catch (TimeoutException)
                {
                    TryKillTree(signal);
                }
            }

            try
            {
                await process.WaitForExitAsync().WaitAsync(GracefulStopBudget);
                return HostStopOutcome.Graceful;
            }
            catch (TimeoutException)
            {
                // entireProcessTree: anything the Host spawned (for example the docker CLI) also
                // holds the redirected pipes, so killing only the direct child would leave the
                // reader tasks — and the test host — waiting for an EOF that never comes.
                TryKillTree(process);
                try
                {
                    await process.WaitForExitAsync().WaitAsync(ForcedStopBudget);
                }
                catch (TimeoutException)
                {
                    // Reported by the caller's HasExited assertion rather than hidden here.
                }

                return HostStopOutcome.ForcedKill;
            }
        }

        /// <summary>
        /// Bounded drain of the redirected pipes. The pumps only complete at EOF, so waiting on
        /// them unbounded would reintroduce exactly the hang this class exists to prevent; instead
        /// the wait is capped and whatever has already been buffered is reported either way.
        /// </summary>
        public async Task<string> DrainOutputAsync()
        {
            var atEof = true;
            try
            {
                await Task.WhenAll(_stdout, _stderr).WaitAsync(OutputDrainBudget);
            }
            catch (TimeoutException)
            {
                atEof = false;
            }

            var suffix = atEof
                ? string.Empty
                : $" (partial: pipes still open after {OutputDrainBudget.TotalSeconds:0.###}s)";
            return $"stdout=<{Snapshot(_stdoutBuffer)}> stderr=<{Snapshot(_stderrBuffer)}>{suffix}";
        }

        public void Dispose()
        {
            // Both the test's `using` and the test class's last-resort cleanup can land here, and
            // xUnit's `Timeout` can make them concurrent — so the early-out has to be atomic.
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                if (!_process.HasExited)
                {
                    TryKillTree(_process);
                    _process.WaitForExit((int)ForcedStopBudget.TotalMilliseconds);
                }
            }
            catch (InvalidOperationException)
            {
                // The process was already reaped or the handle already released (an
                // ObjectDisposedException from a racing Dispose lands here too); nothing to reclaim.
            }
            finally
            {
                _process.Dispose();
            }
        }

        private static void TryKillTree(Process target)
        {
            try
            {
                if (!target.HasExited)
                {
                    target.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // Also covers ObjectDisposedException (it derives from InvalidOperationException),
                // which is what a concurrent Dispose racing this call throws.
            }
            catch (NotSupportedException)
            {
            }
        }

        private static async Task PumpAsync(StreamReader reader, StringBuilder buffer)
        {
            var chunk = new char[4096];
            try
            {
                while (true)
                {
                    var read = await reader.ReadAsync(chunk).ConfigureAwait(false);
                    if (read == 0)
                    {
                        return;
                    }

                    lock (buffer)
                    {
                        buffer.Append(chunk, 0, read);
                    }
                }
            }
            catch (Exception ex)
            {
                // Killing the tree or disposing the process tears the pipe down under the reader.
                // Recording it keeps the pump task successfully completed — and therefore observed.
                lock (buffer)
                {
                    buffer.Append($"<reader ended: {ex.GetType().Name}>");
                }
            }
        }

        private static string Snapshot(StringBuilder buffer)
        {
            lock (buffer)
            {
                return buffer.ToString().Trim();
            }
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
                // Bounded: the accept loop is the last thing standing between a failed test and a
                // test host that never exits.
                await _serveTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (TimeoutException)
            {
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

internal sealed class UnixHostProcessFactAttribute : FactAttribute
{
    public UnixHostProcessFactAttribute()
    {
        if (OperatingSystem.IsWindows())
        {
            Skip = "Exact-child graceful shutdown evidence uses Unix SIGTERM; "
                + "Windows runs the platform-specific executable resolution contract only.";
        }
    }
}
