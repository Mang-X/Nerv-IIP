using DotNetCore.CAP;
using DotNetCore.CAP.Messages;
using DotNetCore.CAP.Transport;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Maintenance;
using Nerv.IIP.Testing;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Nerv.IIP.Business.Mes.Web.Tests;

// 只属于本 fixture：旧宿主遗留 group 不会完成本实例的实际 Subscribe 边沿。
internal sealed class MesAssetUnavailableSubscription(Action<string>? writeObservation = null)
{
    private readonly Action<string> writeObservation = writeObservation ?? Console.Error.WriteLine;
    private readonly string fixtureId = Guid.NewGuid().ToString("N");
    private int consumerCount;
    private int observationCount;
    private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource subscribed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Release() => released.TrySetResult();

    // 仅本 fixture 的目标 wrapper；不是 CAP 全部 listener 的计数器。
    // 最多 32 行，末行声明截断；不接收 topic、group、payload 或异常文本。
    private void Observe(int consumerId, string phase)
    {
        var sequence = Interlocked.Increment(ref observationCount);
        if (sequence > 32)
            return;
        try
        {
            writeObservation(JsonSerializer.Serialize(new
            {
                source = "mes_asset_unavailable_subscription",
                fixtureId,
                consumerId,
                sequence,
                phase = sequence == 32 ? "observation_limit_reached" : phase,
                timestamp = Stopwatch.GetTimestamp(),
                timestampFrequency = Stopwatch.Frequency,
                utc = DateTimeOffset.UtcNow,
                processId = Environment.ProcessId,
                runtime = RuntimeInformation.FrameworkDescription,
                architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                processorCount = Environment.ProcessorCount
            }));
        }
        catch (Exception)
        {
            // 测试输出可能已关闭；采集失败不能替换真实 Subscribe 的结果。
        }
    }

    public ValueTask WaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default) =>
        TestTimeout.RunAsync("MES before-first-publish: waiting for actual target Subscribe completion", async token =>
            await subscribed.Task.WaitAsync(token), timeout, cancellationToken);

    internal sealed class ConsumerFactory(IConsumerClientFactory inner, MesAssetUnavailableSubscription boundary, CapOptions options)
        : IConsumerClientFactory
    {
        public async Task<IConsumerClient> CreateAsync(string groupName, byte groupConcurrent)
        {
            var client = await inner.CreateAsync(groupName, groupConcurrent);
            var groupPrefix = string.IsNullOrEmpty(options.GroupNamePrefix) ? "" : options.GroupNamePrefix + ".";
            var targetGroup = groupPrefix + AssetUnavailableIntegrationEventHandlerForReschedule.ConsumerName + "." + options.Version;
            return groupName == targetGroup ? new Consumer(client, boundary, options) : client;
        }
    }

    private sealed class Consumer(IConsumerClient inner, MesAssetUnavailableSubscription boundary, CapOptions options)
        : IConsumerClient
    {
        private readonly int consumerId = Interlocked.Increment(ref boundary.consumerCount);
        public BrokerAddress BrokerAddress => inner.BrokerAddress;
        public Func<TransportMessage, object?, Task>? OnMessageCallback { get => inner.OnMessageCallback; set => inner.OnMessageCallback = value; }
        public Action<LogMessageEventArgs>? OnLogCallback { get => inner.OnLogCallback; set => inner.OnLogCallback = value; }
        public Task<ICollection<string>> FetchTopicsAsync(IEnumerable<string> topics) => inner.FetchTopicsAsync(topics);

        public async Task SubscribeAsync(IEnumerable<string> topics)
        {
            var actualTopics = topics.ToArray();
            var prefix = string.IsNullOrEmpty(options.TopicNamePrefix) ? "" : options.TopicNamePrefix + ".";
            Assert.Contains(prefix + nameof(AssetUnavailableIntegrationEvent), actualTopics);
            Assert.Contains(prefix + "nerv-iip.issue2966acceptance.business-maintenance.maintenance.asset-unavailable.v2", actualTopics);
            boundary.Observe(consumerId, "entered");
            boundary.Entered.TrySetResult();
            var innerStarted = false;
            try
            {
                await TestTimeout.RunAsync("MES controlled actual Subscribe release", async token =>
                    await boundary.released.Task.WaitAsync(token), TimeSpan.FromSeconds(30));
                boundary.Observe(consumerId, "release_completed");
                // CAP Redis Subscribe awaits CreateStreamWithConsumerGroupAsync for each exact topic/group,
                // then assigns its listening topics. No group creation, cursor change or message IO here.
                innerStarted = true;
                boundary.Observe(consumerId, "inner_subscribe_started");
                await inner.SubscribeAsync(actualTopics);
                boundary.Observe(consumerId, "inner_subscribe_succeeded");
                boundary.subscribed.TrySetResult();
            }
            catch (Exception failure)
            {
                boundary.Observe(consumerId, innerStarted ? "inner_subscribe_failed" : "release_failed");
                boundary.subscribed.TrySetException(failure);
                throw;
            }
        }

        public Task ListeningAsync(TimeSpan timeout, CancellationToken token)
        {
            boundary.Observe(consumerId, "listening_entered");
            return inner.ListeningAsync(timeout, token);
        }
        public Task CommitAsync(object? sender) => inner.CommitAsync(sender);
        public Task RejectAsync(object? sender) => inner.RejectAsync(sender);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
