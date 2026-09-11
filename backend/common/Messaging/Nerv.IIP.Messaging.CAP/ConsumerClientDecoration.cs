using DotNetCore.CAP;
using DotNetCore.CAP.Messages;
using DotNetCore.CAP.Transport;
using Microsoft.Extensions.DependencyInjection;

namespace Nerv.IIP.Messaging.CAP;

/// <summary>
/// Wraps the transport's <see cref="IConsumerClientFactory"/> and every <see cref="IConsumerClient"/> it creates
/// so that later work (#3351 首轮订阅闸门、#3352 <see cref="IConsumerClient.ListeningAsync"/> 专用线程) has one
/// place to hook into.
///
/// <para><b>#3365 起，<see cref="DecoratedConsumerClientFactory.CreateAsync"/> 不再是纯转发</b>：它是连接池
/// 预热的消费侧挂载点（发布侧在 <see cref="WarmedTransport.SendAsync"/>）。#3350 建这层骨架时写的就是
/// 「给后续工作一个挂载点」，#3365 是第一个挂上来的。<see cref="DecoratedConsumerClient"/> 仍然<b>逐字透传</b>：
/// 它的每一个成员都原样转发 inner，没有任何自身行为——预热在工厂那一层已经完成，client 被造出来时池已经建满。</para>
///
/// <para>Registration mechanics: the transport package registers <see cref="IConsumerClientFactory"/> from its own
/// <see cref="ICapOptionsExtension.AddServices"/>, and <c>AddCap</c> runs the extensions in registration order.
/// Registering this extension immediately after the transport extension therefore guarantees the transport
/// descriptor already exists when <see cref="AddServices"/> runs.</para>
/// </summary>
internal sealed class ConsumerClientDecorationExtension : ICapOptionsExtension
{
    public void AddServices(IServiceCollection services)
    {
        var transportDescriptor = services.LastOrDefault(
            descriptor => descriptor.ServiceType == typeof(IConsumerClientFactory));
        if (transportDescriptor?.ImplementationType is null)
        {
            // Fail closed，且这是本文件唯一有自身行为的代码——它在 DI 组装期，不在消息路径上。
            // 静默跳过会让装饰器不挂载而所有门禁照绿，把 #3351/#3352 变成没人报告的空操作；
            // 只记日志等于静默（CI 不读警告）。抛异常把「没挂上」从静默变成每个宿主启动即失败。
            throw new InvalidOperationException(
                $"The CAP transport did not register an {nameof(IConsumerClientFactory)} with a concrete implementation type, "
                + $"so {nameof(ConsumerClientDecorationExtension)} cannot wrap it. "
                + "Registration order or the transport package shape changed; revisit the decoration seam.");
        }

        services.Remove(transportDescriptor);
        services.AddSingleton(provider => new TransportConsumerClientFactory(
            (IConsumerClientFactory)ActivatorUtilities.CreateInstance(provider, transportDescriptor.ImplementationType)));

        // Registered with a concrete implementation type (not a factory delegate) so that the resulting descriptor
        // keeps the shape callers already rely on: capture the single IConsumerClientFactory descriptor and rebuild
        // it through ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType).
        services.AddSingleton<IConsumerClientFactory, DecoratedConsumerClientFactory>();
    }
}

/// <summary>Holds the transport's own factory so the decorator can take it as a constructor dependency.</summary>
internal sealed class TransportConsumerClientFactory(IConsumerClientFactory inner)
{
    public IConsumerClientFactory Inner { get; } = inner;
}

internal sealed class DecoratedConsumerClientFactory(
    TransportConsumerClientFactory transport,
    RedisConnectionPoolWarmup warmup) : IConsumerClientFactory
{
    internal IConsumerClientFactory Inner => transport.Inner;

    /// <summary>
    /// #3365：<b>先预热、再创建</b>。顺序是承重的——<see cref="IConsumerClient"/> 的 SubscribeAsync /
    /// CommitAsync / 轮询都经 <c>RedisStreamManager</c> 触到连接池，而 client 只能由本方法产出，
    /// 所以在这里 await 完预热就覆盖了消费侧的全部池入口。
    /// </summary>
    public async Task<IConsumerClient> CreateAsync(string groupName, byte groupConcurrent)
    {
        await warmup.WarmAsync().ConfigureAwait(false);
        return new DecoratedConsumerClient(await transport.Inner.CreateAsync(groupName, groupConcurrent));
    }
}

internal sealed class DecoratedConsumerClient(IConsumerClient inner) : IConsumerClient
{
    internal IConsumerClient Inner => inner;

    public BrokerAddress BrokerAddress => inner.BrokerAddress;

    public Func<TransportMessage, object?, Task>? OnMessageCallback
    {
        get => inner.OnMessageCallback;
        set => inner.OnMessageCallback = value;
    }

    public Action<LogMessageEventArgs>? OnLogCallback
    {
        get => inner.OnLogCallback;
        set => inner.OnLogCallback = value;
    }

    public Task<ICollection<string>> FetchTopicsAsync(IEnumerable<string> topicNames) =>
        inner.FetchTopicsAsync(topicNames);

    public Task SubscribeAsync(IEnumerable<string> topics) => inner.SubscribeAsync(topics);

    public Task ListeningAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        inner.ListeningAsync(timeout, cancellationToken);

    public Task CommitAsync(object? sender) => inner.CommitAsync(sender);

    public Task RejectAsync(object? sender) => inner.RejectAsync(sender);

    public ValueTask DisposeAsync() => inner.DisposeAsync();
}
