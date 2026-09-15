using DotNetCore.CAP;
using DotNetCore.CAP.Messages;
using DotNetCore.CAP.Transport;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Maintenance;
using Nerv.IIP.Testing;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Nerv.IIP.Business.Mes.Web.Tests;

// Regression：#3219，实际 Subscribe 失败必须到达首发前等待者。
public sealed class MesAssetUnavailableSubscriptionTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(5);
    private static readonly string[] Topics =
    [
        nameof(AssetUnavailableIntegrationEvent),
        "nerv-iip.issue2966acceptance.business-maintenance.maintenance.asset-unavailable.v2"
    ];

    // NERV-2126：取证必须区分门闩、真实 inner 完成和目标监听，不能提前报告成功。
    [Fact]
    public async Task Observations_follow_actual_edges_and_keep_fixture_identity_isolated()
    {
        var lines = new ConcurrentQueue<string>();
        var boundary = new MesAssetUnavailableSubscription(lines.Enqueue);
        var inner = new ControlledConsumer();
        await using var consumer = await CreateAsync(inner, boundary);
        var subscribing = consumer.SubscribeAsync(Topics);
        await AwaitAsync(boundary.Entered.Task);
        Assert.Equal(["entered"], Phases(lines));
        boundary.Release();
        await AwaitAsync(inner.Entered.Task);
        Assert.Equal(["entered", "release_completed", "inner_subscribe_started"], Phases(lines));
        Assert.Equal(Topics, inner.Topics);
        Assert.False(subscribing.IsCompleted);
        inner.Completion.SetResult();
        await AwaitAsync(subscribing);
        await boundary.WaitAsync(Budget);
        using var cancellation = new CancellationTokenSource();
        await consumer.ListeningAsync(Budget, cancellation.Token);
        Assert.Equal(Budget, inner.ListeningTimeout);
        Assert.Equal(cancellation.Token, inner.ListeningToken);
        Assert.Equal(["entered", "release_completed", "inner_subscribe_started", "inner_subscribe_succeeded", "listening_entered"], Phases(lines));
        var first = Read(lines.First());
        Assert.Equal(Environment.ProcessId, first.GetProperty("processId").GetInt32());
        Assert.Equal(Environment.ProcessorCount, first.GetProperty("processorCount").GetInt32());
        Assert.Equal(System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, first.GetProperty("runtime").GetString());
        Assert.Equal(System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(), first.GetProperty("architecture").GetString());
        Assert.Equal(System.Diagnostics.Stopwatch.Frequency, first.GetProperty("timestampFrequency").GetInt64());
        var observations = lines.Select(Read).ToArray();
        Assert.All(observations, row =>
        {
            Assert.Equal(first.GetProperty("fixtureId").GetString(), row.GetProperty("fixtureId").GetString());
            Assert.Equal(first.GetProperty("consumerId").GetInt32(), row.GetProperty("consumerId").GetInt32());
            Assert.Equal(TimeSpan.Zero, row.GetProperty("utc").GetDateTimeOffset().Offset);
        });
        Assert.True(observations.Zip(observations.Skip(1), (a, b) =>
            a.GetProperty("timestamp").GetInt64() <= b.GetProperty("timestamp").GetInt64()).All(value => value));

        var otherLines = new ConcurrentQueue<string>();
        var other = new MesAssetUnavailableSubscription(otherLines.Enqueue);
        var otherInner = new ControlledConsumer();
        await using var otherConsumer = await CreateAsync(otherInner, other);
        other.Release();
        otherInner.Completion.SetResult();
        await otherConsumer.SubscribeAsync(Topics);
        Assert.NotEqual(first.GetProperty("fixtureId").GetString(), Read(otherLines.First()).GetProperty("fixtureId").GetString());
        Assert.Equal(5, lines.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Inner_failure_or_cancellation_is_observed_without_exposing_exception_text(bool cancel)
    {
        var lines = new ConcurrentQueue<string>();
        var boundary = new MesAssetUnavailableSubscription(lines.Enqueue);
        var inner = new ControlledConsumer();
        await using var consumer = await CreateAsync(inner, boundary);
        boundary.Release();
        var subscribing = consumer.SubscribeAsync(Topics);
        await AwaitAsync(inner.Entered.Task);
        var waiting = boundary.WaitAsync(Budget).AsTask();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Exception failure = cancel
            ? new OperationCanceledException("private-payload-and-connection", cancellation.Token)
            : new InvalidOperationException("private-payload-and-connection");
        inner.Completion.SetException(failure);
        Assert.Same(failure, await Record.ExceptionAsync(() => subscribing));
        Assert.Same(failure, await Record.ExceptionAsync(() => waiting));
        Assert.Equal(["entered", "release_completed", "inner_subscribe_started", "inner_subscribe_failed"], Phases(lines));
        Assert.DoesNotContain(lines, line => line.Contains("private-payload-and-connection", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unavailable_observation_does_not_replace_inner_result(bool fail)
    {
        var boundary = new MesAssetUnavailableSubscription(_ => throw new IOException("unavailable output"));
        var inner = new ControlledConsumer();
        await using var consumer = await CreateAsync(inner, boundary);
        boundary.Release();
        var subscribing = consumer.SubscribeAsync(Topics);
        await AwaitAsync(inner.Entered.Task);
        var waiting = boundary.WaitAsync(Budget).AsTask();
        Assert.False(waiting.IsCompleted);
        var failure = new InvalidOperationException("original failure");
        if (fail)
            inner.Completion.SetException(failure);
        else
            inner.Completion.SetResult();
        Assert.Same(fail ? failure : null, await Record.ExceptionAsync(() => subscribing));
        Assert.Same(fail ? failure : null, await Record.ExceptionAsync(() => waiting));
    }

    private static JsonElement Read(string line)
    {
        using var document = JsonDocument.Parse(line);
        return document.RootElement.Clone();
    }
    private static string[] Phases(IEnumerable<string> lines) => lines.Select(line => Read(line).GetProperty("phase").GetString()!).ToArray();

    [Fact]
    public async Task Observation_limit_does_not_stop_consumers_and_each_wrapper_has_its_own_identity()
    {
        var lines = new ConcurrentQueue<string>();
        var boundary = new MesAssetUnavailableSubscription(lines.Enqueue);
        boundary.Release();
        for (var index = 0; index < 10; index++)
        {
            var inner = new ControlledConsumer();
            inner.Completion.SetResult();
            await using var consumer = await CreateAsync(inner, boundary);
            await consumer.SubscribeAsync(Topics);
            await consumer.ListeningAsync(Budget, CancellationToken.None);
            Assert.Equal(Topics, inner.Topics);
            Assert.Equal(Budget, inner.ListeningTimeout);
        }
        Assert.Equal(32, lines.Count);
        Assert.Equal("observation_limit_reached", Phases(lines)[^1]);
        var entered = lines.Select(Read).Where(row => row.GetProperty("phase").GetString() == "entered").ToArray();
        Assert.Equal(entered.Length, entered.Select(row => row.GetProperty("consumerId").GetInt32()).Distinct().Count());
    }

    [Fact]
    public async Task Actual_subscribe_failure_reaches_caller_and_readiness_waiter_unchanged()
    {
        var boundary = new MesAssetUnavailableSubscription();
        var inner = new ControlledConsumer();
        await using var consumer = await CreateAsync(inner, boundary);
        boundary.Release();
        var subscribing = consumer.SubscribeAsync(Topics);
        await AwaitAsync(inner.Entered.Task);
        var waiting = boundary.WaitAsync(Budget).AsTask();
        var failure = new InvalidOperationException("Synthetic Subscribe failure for issue 3219");
        inner.Completion.SetException(failure);

        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => subscribing));
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => waiting));
    }

    [Fact]
    public async Task Pending_subscribe_times_out_without_readiness_and_completes_after_release()
    {
        var boundary = new MesAssetUnavailableSubscription();
        var inner = new ControlledConsumer();
        await using var consumer = await CreateAsync(inner, boundary);
        var subscribing = consumer.SubscribeAsync(Topics);
        await AwaitAsync(boundary.Entered.Task);
        Assert.False(inner.Entered.Task.IsCompleted);
        boundary.Release();
        await AwaitAsync(inner.Entered.Task);
        try
        {
            await Assert.ThrowsAsync<TestTimeoutException>(() =>
                boundary.WaitAsync(TimeSpan.FromMilliseconds(100)).AsTask());
            Assert.False(subscribing.IsCompleted);
        }
        finally
        {
            inner.Completion.TrySetResult();
        }
        await AwaitAsync(subscribing);
        await boundary.WaitAsync(Budget);
    }

    [Fact]
    public async Task Cancelling_one_wait_does_not_cancel_shared_readiness()
    {
        var lines = new ConcurrentQueue<string>();
        var boundary = new MesAssetUnavailableSubscription(lines.Enqueue);
        var inner = new ControlledConsumer();
        await using var consumer = await CreateAsync(inner, boundary);
        boundary.Release();
        var subscribing = consumer.SubscribeAsync(Topics);
        await AwaitAsync(inner.Entered.Task);
        using var cancellation = new CancellationTokenSource();
        var cancelledWait = boundary.WaitAsync(Budget, cancellation.Token).AsTask();
        var subsequentWait = boundary.WaitAsync(Budget).AsTask();
        try
        {
            cancellation.Cancel();
            var failure = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledWait);
            Assert.Equal(cancellation.Token, failure.CancellationToken);
            Assert.False(subsequentWait.IsCompleted);
            Assert.Equal(["entered", "release_completed", "inner_subscribe_started"], Phases(lines));
        }
        finally
        {
            inner.Completion.TrySetResult();
        }
        await AwaitAsync(subscribing);
        await subsequentWait;
        Assert.Equal("inner_subscribe_succeeded", Phases(lines)[^1]);
    }

    [Fact]
    public async Task Release_timeout_reaches_readiness_waiter_without_calling_inner_subscribe()
    {
        var lines = new ConcurrentQueue<string>();
        var boundary = new MesAssetUnavailableSubscription(lines.Enqueue);
        var inner = new ControlledConsumer();
        await using var consumer = await CreateAsync(inner, boundary);
        var subscribing = consumer.SubscribeAsync(Topics);
        await AwaitAsync(boundary.Entered.Task);
        var waiting = boundary.WaitAsync(TimeSpan.FromSeconds(40)).AsTask();
        var failure = await Assert.ThrowsAsync<TestTimeoutException>(() => subscribing);
        Assert.Equal("MES controlled actual Subscribe release", failure.Operation);
        Assert.Same(failure, await Assert.ThrowsAsync<TestTimeoutException>(() => waiting));
        Assert.False(inner.Entered.Task.IsCompleted);
        Assert.Equal(["entered", "release_failed"], Phases(lines));
    }

    private static async Task<IConsumerClient> CreateAsync(ControlledConsumer inner, MesAssetUnavailableSubscription boundary)
    {
        var options = new CapOptions();
        var factory = new MesAssetUnavailableSubscription.ConsumerFactory(new ControlledFactory(inner), boundary, options);
        return await factory.CreateAsync(
            AssetUnavailableIntegrationEventHandlerForReschedule.ConsumerName + "." + options.Version, 1);
    }

    private static async Task AwaitAsync(Task task) =>
        await TestTimeout.RunAsync("Controlled Subscribe edge", async token => await task.WaitAsync(token), Budget);

    private sealed class ControlledFactory(IConsumerClient consumer) : IConsumerClientFactory
    {
        public Task<IConsumerClient> CreateAsync(string groupName, byte groupConcurrent) => Task.FromResult(consumer);
    }

    private sealed class ControlledConsumer : IConsumerClient
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string[]? Topics { get; private set; }
        public TimeSpan ListeningTimeout { get; private set; }
        public CancellationToken ListeningToken { get; private set; }
        public BrokerAddress BrokerAddress => throw new NotSupportedException();
        public Func<TransportMessage, object?, Task>? OnMessageCallback { get; set; }
        public Action<LogMessageEventArgs>? OnLogCallback { get; set; }
        public Task<ICollection<string>> FetchTopicsAsync(IEnumerable<string> topics) => throw new NotSupportedException();
        public Task SubscribeAsync(IEnumerable<string> topics)
        {
            Topics = topics.ToArray();
            Entered.TrySetResult();
            return Completion.Task;
        }
        public Task ListeningAsync(TimeSpan timeout, CancellationToken token)
        {
            ListeningTimeout = timeout;
            ListeningToken = token;
            return Task.CompletedTask;
        }
        public Task CommitAsync(object? sender) => throw new NotSupportedException();
        public Task RejectAsync(object? sender) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
