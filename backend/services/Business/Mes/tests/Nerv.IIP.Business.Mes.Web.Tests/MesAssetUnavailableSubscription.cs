using DotNetCore.CAP;
using DotNetCore.CAP.Messages;
using DotNetCore.CAP.Transport;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Maintenance;
using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.Mes.Web.Tests;

// 只属于本 fixture：旧宿主遗留 group 不会完成本实例的实际 Subscribe 边沿。
internal sealed class MesAssetUnavailableSubscription
{
    private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource subscribed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Release() => released.TrySetResult();

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
            boundary.Entered.TrySetResult();
            try
            {
                await TestTimeout.RunAsync("MES controlled actual Subscribe release", async token =>
                    await boundary.released.Task.WaitAsync(token), TimeSpan.FromSeconds(30));
                // CAP Redis Subscribe awaits CreateStreamWithConsumerGroupAsync for each exact topic/group,
                // then assigns its listening topics. No group creation, cursor change or message IO here.
                await inner.SubscribeAsync(actualTopics);
                boundary.subscribed.TrySetResult();
            }
            catch (Exception failure)
            {
                boundary.subscribed.TrySetException(failure);
                throw;
            }
        }

        public Task ListeningAsync(TimeSpan timeout, CancellationToken token) => inner.ListeningAsync(timeout, token);
        public Task CommitAsync(object? sender) => inner.CommitAsync(sender);
        public Task RejectAsync(object? sender) => inner.RejectAsync(sender);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
