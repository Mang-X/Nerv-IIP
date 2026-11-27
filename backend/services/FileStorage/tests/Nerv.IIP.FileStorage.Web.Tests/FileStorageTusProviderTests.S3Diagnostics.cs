using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nerv.IIP.Contracts.FileStorage;
using Nerv.IIP.Testing;

namespace Nerv.IIP.FileStorage.Web.Tests;

public sealed partial class FileStorageTusProviderTests
{
    [Fact]
    public async Task S3_socket_requests_keep_endpoint_and_credentials_out_of_host_diagnostics()
    {
        var root = CreateTempDirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, ".filestorage-volume-v1"), "s3-staging-volume");
            using var server = new TcpListener(IPAddress.Loopback, 0);
            server.Start();
            var endpoint = $"http://{server.LocalEndpoint}";
            var activities = new ConcurrentQueue<string>();
            using var activityListener = CaptureHttpActivities(activities);

            var httpPort = ((IPEndPoint)server.LocalEndpoint).Port;
            var measuredRequests = 0;
            using var meterListener = new MeterListener();
            meterListener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == "System.Net.Http" && instrument.Name == "http.client.request.duration")
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            meterListener.SetMeasurementEventCallback<double>((_, _, tags, _) =>
            {
                foreach (var tag in tags)
                {
                    if (tag.Key == "server.port" && tag.Value is int port && port == httpPort)
                    {
                        Interlocked.Increment(ref measuredRequests);
                    }
                }
            });
            meterListener.Start();

            using var logs = new CapturingLogProvider();
            await using var factory = new FileStorageWebApplicationFactory();
            await using var host = ConfigureStorage(factory, "s3", root: root, logProvider: logs, change: values =>
            {
                values["FileStorage:S3:Endpoint"] = endpoint;
                values["FileStorage:Tus:VolumeIdentity"] = "s3-staging-volume";
                values["OpenTelemetry:Enabled"] = "true";
            });
            using var client = CreateInternalServiceClient(host);
            await TestTimeout.RunAsync("loopback S3 readiness and HTTP telemetry control", async ct =>
            {
                var s3Request = client.GetAsync("/health/tus-volume", ct);
                Assert.Equal("HEAD /files-bucket HTTP/1.1", await RespondForbiddenAsync(server, ct));
                using var response = await s3Request;
                Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
                Assert.Contains(logs.Messages, message => message.Contains("The S3 bucket is unavailable.", StringComparison.Ordinal));

                var diagnostics = string.Join('\n', logs.Messages.Concat(activities));
                AssertSanitized(diagnostics);
                Assert.DoesNotContain(endpoint, diagnostics);
                Assert.DoesNotContain(root, diagnostics);
                Assert.Equal(0, Volatile.Read(ref measuredRequests));

                using var ordinaryHttp = host.Services.GetRequiredService<IHttpClientFactory>().CreateClient();
                var control = ordinaryHttp.GetAsync(endpoint + "/telemetry-control", ct);
                Assert.Equal("GET /telemetry-control HTTP/1.1", await RespondForbiddenAsync(server, ct));
                using var controlResponse = await control;
                Assert.Equal(HttpStatusCode.Forbidden, controlResponse.StatusCode);
                Assert.Contains(activities, activity => activity.Contains(endpoint + "/telemetry-control", StringComparison.Ordinal));
                Assert.Equal(1, Volatile.Read(ref measuredRequests));
            }, TimeSpan.FromSeconds(10));
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [Fact]
    public async Task S3_staging_open_failure_logs_fixed_diagnostics_and_preserves_retry()
    {
        var root = CreateTempDirectory();
        try
        {
            var activities = new ConcurrentQueue<string>();
            using var listener = CaptureHttpActivities(activities);
            using var logs = new CapturingLogProvider();
            using var s3 = new TestS3Handler();
            await using var factory = new FileStorageWebApplicationFactory();
            await using var host = ConfigureStorage(factory, "s3", s3, root, logProvider: logs);
            using var client = CreateInternalServiceClient(host);
            var bytes = "hello"u8.ToArray();
            var upload = await CreateTusUploadSessionAsync(client, bytes.Length, CreateTextAttachmentRequest());
            FileStream? lockedStaging = null;
            s3.OnGet = () =>
            {
                var path = Assert.Single(Directory.EnumerateFiles(root, "staging-*.bin"));
                lockedStaging = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
            };

            try
            {
                using var request = CreateTusPatchRequest(upload.Upload.Url, 0, bytes);
                using var failure = await client.SendAsync(request);
                Assert.Equal(HttpStatusCode.ServiceUnavailable, failure.StatusCode);
                Assert.NotNull(lockedStaging);
                Assert.Equal(0, s3.PutCount);
                Assert.Null(await ReadFileAsync(host, upload.FileId));
                Assert.Contains(logs.Messages, message => message.Contains(
                    "The S3 upload commit could not access the required bytes.", StringComparison.Ordinal));
                var diagnostics = string.Join('\n', logs.Messages.Concat(activities));
                AssertSanitized(diagnostics);
                Assert.DoesNotContain(root, diagnostics);
            }
            finally
            {
                lockedStaging?.Dispose();
                s3.OnGet = null;
            }

            using var retry = await client.PostAsJsonAsync($"/api/files/v1/upload-sessions/{upload.UploadSessionId}/complete",
                new CompleteUploadSessionRequest("org-001", "prod", "attachment", null, bytes.Length));
            retry.EnsureSuccessStatusCode();
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [Fact]
    public async Task S3_staging_write_failure_returns_fixed_diagnostics_and_keeps_path_out_of_host_logs()
    {
        var root = CreateTempDirectory();
        try
        {
            var activities = new ConcurrentQueue<string>();
            using var listener = CaptureHttpActivities(activities);
            using var logs = new CapturingLogProvider();
            using var s3 = new TestS3Handler();
            await using var factory = new FileStorageWebApplicationFactory();
            await using var host = ConfigureStorage(factory, "s3", s3, root, logProvider: logs);
            using var client = CreateInternalServiceClient(host);
            var bytes = "hello"u8.ToArray();
            var upload = await CreateTusUploadSessionAsync(client, bytes.Length, CreateTextAttachmentRequest());

            var sessionHash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(upload.UploadSessionId))).ToLowerInvariant();
            var blockingDirectory = Path.Combine(root, $"staging-{sessionHash}.bin");
            Directory.CreateDirectory(blockingDirectory);

            try
            {
                using var request = CreateTusPatchRequest(upload.Upload.Url, 0, bytes);
                using var failure = await client.SendAsync(request);
                Assert.Equal(HttpStatusCode.ServiceUnavailable, failure.StatusCode);
                var body = await failure.Content.ReadAsStringAsync();
                Assert.Equal("The staging volume could not access the upload bytes.", body);
                Assert.Equal(0, s3.PutCount);
                Assert.Null(await ReadFileAsync(host, upload.FileId));

                var diagnostics = string.Join('\n', logs.Messages.Concat(activities));
                AssertSanitized(diagnostics);
                Assert.DoesNotContain(root, diagnostics);
                Assert.DoesNotContain(blockingDirectory, diagnostics);
                Assert.DoesNotContain(nameof(UnauthorizedAccessException), diagnostics);
            }
            finally
            {
                Directory.Delete(blockingDirectory, recursive: true);
            }

            await PatchTusBytesAsync(client, upload.Upload.Url, 0, bytes);
            Assert.NotNull(await ReadFileAsync(host, upload.FileId));
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    private static ActivityListener CaptureHttpActivities(ConcurrentQueue<string> activities)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name is "System.Net.Http" or "Microsoft.AspNetCore",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => activities.Enqueue(string.Join(" ", activity.TagObjects))
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static async Task<string?> RespondForbiddenAsync(TcpListener server, CancellationToken ct)
    {
        using var connection = await server.AcceptTcpClientAsync(ct);
        await using var stream = connection.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        var requestLine = await reader.ReadLineAsync(ct);
        while (await reader.ReadLineAsync(ct) is { Length: > 0 })
        {
        }
        await stream.WriteAsync("HTTP/1.1 403 Forbidden\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray(), ct);
        return requestLine;
    }

    private sealed class CapturingLogProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Messages);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) => messages.Enqueue($"{formatter(state, exception)} {state} {exception}");
        }
    }
}
