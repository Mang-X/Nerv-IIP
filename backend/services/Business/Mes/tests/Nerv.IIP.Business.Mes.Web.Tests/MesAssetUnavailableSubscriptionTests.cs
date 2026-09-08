using DotNetCore.CAP;
using DotNetCore.CAP.Messages;
using DotNetCore.CAP.Transport;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Maintenance;
using Nerv.IIP.Testing;

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
        var boundary = new MesAssetUnavailableSubscription();
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
        }
        finally
        {
            inner.Completion.TrySetResult();
        }
        await AwaitAsync(subscribing);
        await subsequentWait;
    }

    [Fact]
    public async Task Release_timeout_reaches_readiness_waiter_without_calling_inner_subscribe()
    {
        var boundary = new MesAssetUnavailableSubscription();
        var inner = new ControlledConsumer();
        await using var consumer = await CreateAsync(inner, boundary);
        var subscribing = consumer.SubscribeAsync(Topics);
        await AwaitAsync(boundary.Entered.Task);
        var waiting = boundary.WaitAsync(TimeSpan.FromSeconds(40)).AsTask();
        var failure = await Assert.ThrowsAsync<TestTimeoutException>(() => subscribing);
        Assert.Equal("MES controlled actual Subscribe release", failure.Operation);
        Assert.Same(failure, await Assert.ThrowsAsync<TestTimeoutException>(() => waiting));
        Assert.False(inner.Entered.Task.IsCompleted);
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
        public BrokerAddress BrokerAddress => throw new NotSupportedException();
        public Func<TransportMessage, object?, Task>? OnMessageCallback { get; set; }
        public Action<LogMessageEventArgs>? OnLogCallback { get; set; }
        public Task<ICollection<string>> FetchTopicsAsync(IEnumerable<string> topics) => throw new NotSupportedException();
        public Task SubscribeAsync(IEnumerable<string> topics)
        {
            Entered.TrySetResult();
            return Completion.Task;
        }
        public Task ListeningAsync(TimeSpan timeout, CancellationToken token) => throw new NotSupportedException();
        public Task CommitAsync(object? sender) => throw new NotSupportedException();
        public Task RejectAsync(object? sender) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
