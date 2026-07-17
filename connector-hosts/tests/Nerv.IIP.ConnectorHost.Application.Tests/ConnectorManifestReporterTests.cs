using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Nerv.IIP.ConnectorHost.Application;
using Nerv.IIP.ConnectorHost.Connectors.Abstractions;
using Nerv.IIP.ConnectorHost.TestUtilities;

namespace Nerv.IIP.ConnectorHost.Application.Tests;

public sealed class ConnectorManifestReporterTests
{
    private static readonly DateTimeOffset ActivationObservedAtUtc = DateTimeOffset.Parse("2026-07-17T00:00:00Z");

    [Fact]
    public void Canonical_revision_matches_ascii_vector_and_excludes_activation()
    {
        ConnectorTagManifestEntrySnapshot[] entries =
        [
            Entry(" DEV-B ", " Temperature ", true, " ns=2;s=Temperature ", "error", "E-1", "first failure"),
            Entry("DEV-A", "pressure", false, null, "disabled"),
        ];
        var activationChanged = entries
            .Select(entry => entry with
            {
                ActivationStatus = "active",
                ActivationObservedAtUtc = entry.ActivationObservedAtUtc.AddMinutes(1),
                ActivationErrorCode = null,
                ActivationErrorMessage = null,
            })
            .Reverse()
            .ToArray();

        var revision = ConnectorManifestHasher.Compute(" OPCUA ", entries);
        var enabledChanged = entries.Select(entry => entry.DeviceAssetId.Trim() == "DEV-A" ? entry with { Enabled = true } : entry).ToArray();
        var addressChanged = entries.Select(entry => entry.DeviceAssetId.Trim() == "DEV-B" ? entry with { ProtocolAddress = "ns=2;s=Changed" } : entry).ToArray();

        Assert.Equal("e0ff8c1111083580a719587480101437f3fcd5bf76bb822fc3ae5f2698631e44", revision);
        Assert.Equal(revision, ConnectorManifestHasher.Compute("opcua", activationChanged));
        Assert.NotEqual(revision, ConnectorManifestHasher.Compute("opcua", enabledChanged));
        Assert.NotEqual(revision, ConnectorManifestHasher.Compute("opcua", addressChanged));
    }

    [Fact]
    public void Canonical_json_and_hash_match_unicode_escape_vector()
    {
        ConnectorTagManifestEntrySnapshot[] entries =
        [
            Entry(
                " DEV-中文😀-\"\\\n\u0001 ",
                " TAG-\"\\\n\u0002 ",
                true,
                " ns=2;s=中文😀-\"\\\n\u0003 ",
                "active"),
        ];
        const string expectedJson = """{"sourceSystem":"opcua-\u4E2D\u6587\uD83D\uDE00","entries":[{"deviceAssetId":"DEV-\u4E2D\u6587\uD83D\uDE00-\u0022\\\n\u0001","tagKey":"tag-\u0022\\\n\u0002","enabled":true,"protocolAddress":"ns=2;s=\u4E2D\u6587\uD83D\uDE00-\u0022\\"}]}""";

        var bytes = ConnectorManifestHasher.ComputeCanonicalUtf8Bytes(" OPCUA-中文😀 ", entries);

        Assert.All(bytes, value => Assert.InRange(value, (byte)0, (byte)127));
        Assert.Equal(expectedJson, Encoding.UTF8.GetString(bytes));
        Assert.Equal("e047382c6f4bb10de8f61e5fd1112ee46805620f5749528c16ace0b923d8ae71", ConnectorManifestHasher.Compute(" OPCUA-中文😀 ", entries));
    }

    [Fact]
    public async Task Failed_report_retries_the_identical_observation_then_acknowledgement_clears_pending()
    {
        var clock = new ControllableTimeProvider();
        var client = new ScriptedManifestClient(
            new HttpRequestException("unavailable"),
            Accepted());
        var reporter = CreateReporter(client, clock);
        var snapshot = Snapshot(Entry("dev-1", "temperature", true, "ns=2;s=T", "pending"));

        await reporter.ReportAsync(snapshot, cancellationToken: default);
        clock.Advance(TimeSpan.FromSeconds(1));
        await reporter.ReportAsync(snapshot, cancellationToken: default);
        await reporter.ReportAsync(snapshot, cancellationToken: default);

        Assert.Equal(2, client.Requests.Count);
        Assert.Equal(client.Requests[0], client.Requests[1]);
    }

    [Fact]
    public async Task Retry_delay_is_exponential_and_caps_at_thirty_seconds()
    {
        var clock = new ControllableTimeProvider();
        var client = new ScriptedManifestClient(Enumerable.Repeat<Exception>(new HttpRequestException("unavailable"), 8).ToArray());
        var reporter = CreateReporter(client, clock);
        var snapshot = Snapshot(Entry("dev-1", "temperature", true, null, "pending"));
        var expectedDelays = new[] { 1, 2, 4, 8, 16, 30, 30 };

        await reporter.ReportAsync(snapshot, cancellationToken: default);
        for (var index = 0; index < expectedDelays.Length; index++)
        {
            var delay = expectedDelays[index];
            clock.Advance(TimeSpan.FromSeconds(delay - 1));
            await reporter.ReportAsync(snapshot, cancellationToken: default);
            Assert.Equal(index + 1, client.Requests.Count);
            clock.Advance(TimeSpan.FromSeconds(1));
            await reporter.ReportAsync(snapshot, cancellationToken: default);
            Assert.Equal(index + 2, client.Requests.Count);
        }

        Assert.Equal(8, client.Requests.Count);
    }

    [Fact]
    public async Task Newer_activation_coalesces_pending_without_changing_configuration_revision_or_observation()
    {
        var clock = new ControllableTimeProvider();
        var client = new ScriptedManifestClient(new HttpRequestException("unavailable"), Accepted());
        var reporter = CreateReporter(client, clock);
        var pending = Snapshot(Entry("dev-1", "temperature", true, "ns=2;s=T", "pending"));
        var active = Snapshot(Entry("dev-1", "temperature", true, "ns=2;s=T", "active", observedAtUtc: ActivationObservedAtUtc.AddSeconds(2)));

        await reporter.ReportAsync(pending, cancellationToken: default);
        clock.Advance(TimeSpan.FromSeconds(1));
        await reporter.ReportAsync(active, cancellationToken: default);

        Assert.Equal(2, client.Requests.Count);
        Assert.Equal(client.Requests[0].ManifestRevision, client.Requests[1].ManifestRevision);
        Assert.Equal(client.Requests[0].ManifestObservedAtUtc, client.Requests[1].ManifestObservedAtUtc);
        Assert.Equal("active", Assert.Single(client.Requests[1].Entries).ActivationStatus);
    }

    [Fact]
    public async Task Configuration_change_coalesces_pending_even_when_previous_activation_time_is_later()
    {
        var clock = new ControllableTimeProvider();
        var client = new ScriptedManifestClient(new HttpRequestException("unavailable"), Accepted());
        var reporter = CreateReporter(client, clock);
        var futureActivation = ActivationObservedAtUtc.AddHours(1);
        var first = Snapshot(Entry("dev-1", "temperature", true, null, "active", observedAtUtc: futureActivation));
        var changed = Snapshot(Entry("dev-1", "temperature", false, null, "disabled", observedAtUtc: futureActivation));

        await reporter.ReportAsync(first, cancellationToken: default);
        clock.Advance(TimeSpan.FromSeconds(1));
        await reporter.ReportAsync(changed, cancellationToken: default);

        Assert.Equal(2, client.Requests.Count);
        Assert.False(Assert.Single(client.Requests[1].Entries).Enabled);
        Assert.NotEqual(client.Requests[0].ManifestRevision, client.Requests[1].ManifestRevision);
        Assert.True(client.Requests[1].ManifestObservedAtUtc > client.Requests[0].ManifestObservedAtUtc);
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("conflict")]
    public async Task Rejected_report_advances_exact_observation_past_service_acknowledgement(string disposition)
    {
        var clock = new ControllableTimeProvider();
        var acceptedAtUtc = clock.GetUtcNow().AddMinutes(5).AddTicks(7);
        var client = new ScriptedManifestClient(
            new ConnectorTagManifestAcknowledgement(disposition, new string('a', 64), acceptedAtUtc),
            Accepted());
        var reporter = CreateReporter(client, clock);
        var snapshot = Snapshot(Entry("dev-1", "temperature", true, null, "pending"));

        await reporter.ReportAsync(snapshot, cancellationToken: default);
        await reporter.ReportAsync(snapshot, cancellationToken: default);

        Assert.Equal(2, client.Requests.Count);
        Assert.Equal(acceptedAtUtc.AddTicks(1), client.Requests[1].ManifestObservedAtUtc);
        Assert.True(client.Requests[1].ManifestObservedAtUtc > client.Requests[0].ManifestObservedAtUtc);
    }

    [Fact]
    public async Task Configuration_change_and_rebirth_advance_last_attempted_observation_monotonically()
    {
        var clock = new ControllableTimeProvider();
        var client = new ScriptedManifestClient(Accepted(), Accepted(), Accepted());
        var reporter = CreateReporter(client, clock);
        var first = Snapshot(Entry("dev-1", "temperature", true, null, "pending"));
        var changed = Snapshot(Entry("dev-1", "temperature", false, null, "disabled"));

        await reporter.ReportAsync(first, cancellationToken: default);
        await reporter.ReportAsync(changed, cancellationToken: default);
        await reporter.ReportAsync(changed, forceRebirth: true, cancellationToken: default);

        Assert.Equal(3, client.Requests.Count);
        Assert.Equal(client.Requests[0].ManifestObservedAtUtc.AddTicks(1), client.Requests[1].ManifestObservedAtUtc);
        Assert.Equal(client.Requests[1].ManifestObservedAtUtc.AddTicks(1), client.Requests[2].ManifestObservedAtUtc);
    }

    [Fact]
    public async Task Http_client_posts_internal_contract_and_does_not_include_error_response_body()
    {
        var handler = new RecordingHandler(HttpStatusCode.BadGateway, "sensitive downstream stack");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://iiot") };
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "internal-token");
        var client = new HttpConnectorTagManifestClient(httpClient);
        var report = new ConnectorTagManifestReport(
            "org-1", "env-1", "line-a", "opcua", new string('a', 64), ActivationObservedAtUtc,
            [Entry("dev-1", "temperature", true, "ns=2;s=T", "pending")]);

        var failure = await Assert.ThrowsAsync<HttpRequestException>(() => client.ReportAsync(report, default));

        Assert.Equal("/api/business/v1/iiot/connector-tag-manifests", handler.Request!.RequestUri!.AbsolutePath);
        Assert.Equal("Bearer", handler.Request.Headers.Authorization!.Scheme);
        Assert.Equal("internal-token", handler.Request.Headers.Authorization.Parameter);
        Assert.Contains("\"collectionConnectorId\":\"line-a\"", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive downstream stack", failure.Message, StringComparison.Ordinal);
    }

    private static ConnectorManifestReporter CreateReporter(ScriptedManifestClient client, TimeProvider clock) =>
        new(client, ConnectorHostRuntimeContext.DefaultLocal, clock);

    private static ConnectorTagManifestSnapshot Snapshot(params ConnectorTagManifestEntrySnapshot[] entries) =>
        new("line-a", "opcua", entries);

    private static ConnectorTagManifestEntrySnapshot Entry(
        string deviceAssetId,
        string tagKey,
        bool enabled,
        string? protocolAddress,
        string activationStatus,
        string? errorCode = null,
        string? errorMessage = null,
        DateTimeOffset? observedAtUtc = null) =>
        new(deviceAssetId, tagKey, enabled, protocolAddress, activationStatus, observedAtUtc ?? ActivationObservedAtUtc, errorCode, errorMessage);

    private static ConnectorTagManifestAcknowledgement Accepted() =>
        new("accepted", new string('a', 64), ActivationObservedAtUtc);

    private sealed class ScriptedManifestClient(params object[] results) : IConnectorTagManifestClient
    {
        private readonly Queue<object> _results = new(results);
        public List<ConnectorTagManifestReport> Requests { get; } = [];

        public Task<ConnectorTagManifestAcknowledgement> ReportAsync(ConnectorTagManifestReport report, CancellationToken cancellationToken)
        {
            Requests.Add(report);
            var result = _results.Dequeue();
            return result is Exception exception
                ? Task.FromException<ConnectorTagManifestAcknowledgement>(exception)
                : Task.FromResult((ConnectorTagManifestAcknowledgement)result);
        }
    }

    private sealed class RecordingHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody),
                RequestMessage = request,
            };
        }
    }
}
