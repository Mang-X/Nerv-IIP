using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Nerv.IIP.ConnectorHost.Application;
using Nerv.IIP.ConnectorHost.Connectors.Abstractions;
using Nerv.IIP.ConnectorHost.TestUtilities;
using Nerv.IIP.Contracts.Ops;

namespace Nerv.IIP.ConnectorHost.Connectors.Simulated.Tests;

public sealed class SimulatedLongRunningTests
{
    private const int CycleCount = 1_000;

    [Fact]
    public async Task Thousand_controlled_cycles_are_repeatable_isolated_and_bounded()
    {
        var firstClock = AtEpoch();
        var secondClock = AtEpoch();
        var firstSamples = new DigestingSamplesClient();
        var secondSamples = new DigestingSamplesClient();
        var options = FastOptions();
        var first = CreateConnector(options, firstSamples, firstClock);
        var second = CreateConnector(options, secondSamples, secondClock);

        for (var cycle = 0; cycle < CycleCount; cycle++)
        {
            await first.RunCollectionCycleAsync(CancellationToken.None);
            await second.RunCollectionCycleAsync(CancellationToken.None);
            firstClock.Advance(TimeSpan.FromSeconds(1));
            secondClock.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.Equal(firstSamples.CompleteDigest(), secondSamples.CompleteDigest());
        Assert.Equal(
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["opcua"] = 44 * CycleCount,
                ["mqtt"] = 28 * CycleCount,
                ["modbus"] = 24 * CycleCount
            },
            firstSamples.Counts);
        Assert.All(first.PendingSampleCounts, pair => Assert.Equal(0, pair.Value));

        var targets = await first.DiscoverAsync(CancellationToken.None);
        Assert.Equal(3, targets.Count);
        Assert.Equal([44_000L, 28_000L, 24_000L], targets.Select(target => target.CollectionHealth!.ReceivedCount));
        Assert.All(targets, target => Assert.Equal("alive", target.CollectionHealth!.Connection!.Status));
        Assert.Equal(3, targets.Select(target => target.CollectionHealth!.CounterEpoch).Distinct().Count());

        for (var index = 0; index < 40; index++)
        {
            await first.ExecuteAsync(
                StartTask($"long-run-operation-{index:D2}", $"DEV-CNC-{(index % 10) + 1:D2}"),
                CancellationToken.None);
        }

        Assert.Equal(options.CommandReceiptCacheCapacity, first.CommandReceiptCount);
        Assert.Equal(
            Enumerable.Range(24, 16).Select(index => $"long-run-operation-{index:D2}"),
            first.CachedOperationTaskIds);

        var failingClock = AtEpoch();
        var failing = CreateConnector(
            FastOptions(values =>
            {
                values["Simulated:MaxPendingSamples"] = "32";
                values["Simulated:RetryBaseMilliseconds"] = "86400000";
            }),
            new AlwaysFailingSamplesClient(),
            failingClock);
        for (var cycle = 0; cycle < CycleCount; cycle++)
        {
            await failing.RunCollectionCycleAsync(CancellationToken.None);
            Assert.All(failing.PendingSampleCounts, pair => Assert.InRange(pair.Value, 0, 32));
            failingClock.Advance(TimeSpan.FromSeconds(1));
        }

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => failing.RunCollectionCycleAsync(cancelled.Token));
        Assert.All(failing.PendingSampleCounts, pair => Assert.InRange(pair.Value, 0, 32));
    }

    private static SimulatedConnectorOptions FastOptions(
        Action<Dictionary<string, string?>>? mutate = null) =>
        SimulatedTestConfiguration.Bind(mutate: values =>
        {
            values["Simulated:SampleIntervalMilliseconds"] = "1000";
            values["Simulated:Phases:Normal"] = "00:00:01";
            values["Simulated:Phases:Degrading"] = "00:00:01";
            values["Simulated:Phases:Alarm"] = "00:00:01";
            values["Simulated:Phases:Recovered"] = "00:00:01";
            mutate?.Invoke(values);
        });

    private static ControllableTimeProvider AtEpoch()
    {
        var clock = new ControllableTimeProvider();
        clock.Advance(DateTimeOffset.Parse("2026-07-26T00:00:00Z") - clock.GetUtcNow());
        return clock;
    }

    private static SimulatedConnector CreateConnector(
        SimulatedConnectorOptions options,
        IIndustrialTelemetrySamplesClient samplesClient,
        TimeProvider timeProvider) =>
        new(
            options,
            new ConnectorHostRuntimeContext(
                "1.0",
                "1.0",
                "org-001",
                "env-dev",
                "connector-host-001",
                timeProvider.GetUtcNow()),
            samplesClient,
            timeProvider);

    private static OperationTaskDispatchItem StartTask(
        string operationTaskId,
        string deviceAssetId) =>
        new(
            operationTaskId,
            $"attempt-{operationTaskId}",
            "org-001",
            "env-dev",
            "connector-host-001",
            "CONN-OPCUA-01",
            "device.control.command",
            $"correlation-{operationTaskId}",
            new Dictionary<string, string>
            {
                ["commandType"] = "start-stop",
                ["deviceAssetId"] = deviceAssetId,
                ["value"] = "stop"
            },
            $"lease-{operationTaskId}",
            DateTimeOffset.Parse("2026-07-26T00:00:00Z"),
            DateTimeOffset.Parse("2026-07-26T00:05:00Z"),
            1,
            300,
            3);

    private sealed class DigestingSamplesClient : IIndustrialTelemetrySamplesClient
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        public Dictionary<string, int> Counts { get; } = new(StringComparer.Ordinal);

        public Task RecordSampleAsync(
            RecordIndustrialTelemetrySampleRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceSystem = Assert.IsType<string>(request.SourceSystem);
            Counts[sourceSystem] = Counts.GetValueOrDefault(sourceSystem) + 1;
            var canonical = string.Join(
                '\u001f',
                request.DeviceAssetId,
                request.TagKey,
                request.BucketStartUtc.ToString("O", CultureInfo.InvariantCulture),
                request.AverageValue.ToString(CultureInfo.InvariantCulture),
                request.SourceSequence,
                request.DeviceState,
                request.CollectionConnectorId);
            _hash.AppendData(Encoding.UTF8.GetBytes(canonical));
            return Task.CompletedTask;
        }

        public string CompleteDigest() =>
            Convert.ToHexString(_hash.GetHashAndReset());
    }

    private sealed class AlwaysFailingSamplesClient : IIndustrialTelemetrySamplesClient
    {
        public Task RecordSampleAsync(
            RecordIndustrialTelemetrySampleRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromException(new HttpRequestException("controlled long-run failure"));
        }
    }
}
