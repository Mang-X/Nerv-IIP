using DotNetCore.CAP;
using DotNetCore.CAP.Messages;
using DotNetCore.CAP.Transport;
using Microsoft.Extensions.DependencyInjection;

namespace Nerv.IIP.Messaging.CAP;

/// <summary>
/// Wraps the transport's <see cref="IConsumerClientFactory"/> and every <see cref="IConsumerClient"/> it creates
/// so that the first-subscription gate (#3351) and later work (#3352 <see cref="IConsumerClient.ListeningAsync"/>
/// 专用线程) has one place to hook into.
///
/// <para><b>消息路径上目前只有一处自身行为</b>：<see cref="FirstSubscriptionGate"/>（#3351）。
/// <see cref="DecoratedConsumerClient"/> 的其余成员仍逐字转发 inner。本文件里 DI 组装期的自身行为是
/// <see cref="AddServices"/> 的 fail closed。</para>
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

internal sealed class DecoratedConsumerClientFactory(TransportConsumerClientFactory transport) : IConsumerClientFactory
{
    // One gate per factory == one gate per host: AddServices registers the factory as a singleton, and CAP resolves
    // IConsumerClientFactory once per CapConsumerRegister. Every client this factory hands out therefore shares it.
    private readonly FirstSubscriptionGate firstSubscriptionGate = new();

    internal IConsumerClientFactory Inner => transport.Inner;

    public async Task<IConsumerClient> CreateAsync(string groupName, byte groupConcurrent) =>
        new DecoratedConsumerClient(
            await transport.Inner.CreateAsync(groupName, groupConcurrent),
            firstSubscriptionGate);
}

/// <summary>
/// #3351（#3236 拆解 3/5）：<b>首轮订阅闸门</b>。
///
/// <para><b>要消除的窗口。</b> 上游 <c>IConsumerRegister.Default.ExecuteAsync</c> 的 <c>foreach</c>
/// 对每个消费组只 <c>await</c> 两个同步已完成的调用，然后 <c>Task.Factory.StartNew(..., LongRunning)</c>
/// <b>不等待就进下一组</b> ⇒ 全部消费组的 <see cref="IConsumerClient.SubscribeAsync"/> 同时起飞。
/// Redis 传输下它们共用一个 <c>RedisConnectionPool</c>，而 <c>RedisConnectionPool.ConnectAsync()</c> 的
/// <c>foreach</c> 对「已创建但仍在途」的 <c>AsyncLazyRedisConnection</c> 会解引用 <c>CreatedConnection</c>
/// （<c>Value.GetAwaiter().GetResult()</c>）⇒ <b>同步阻塞</b>。第一个调用者走 <c>await lazy</c> 不阻塞，
/// 第二个及以后在它在途时到达就全部阻塞。</para>
///
/// <para><b>闸门做什么。</b> 第一个 <see cref="RunAsync"/> 独占执行；在它完成之前到达的其余调用者
/// <b>异步</b>等待它，完成后一次性全部放行。<b>之后不再拦</b>——闸门只解决「第一条连接在途时存在第二个
/// 调用者」这一个前提，不是通用限流器。</para>
///
/// <para><b>为什么必须异步等待。</b> 阻塞等待（<c>.Wait()</c> / <c>.Result</c> / <c>lock</c>）会把本票变成
/// <b>新的线程占用源</b>，那正是 #3352 要消除的东西；而且上游那个 <c>foreach</c> 是同步跑的，阻塞会直接卡住
/// 它。<see cref="TaskCreationOptions.RunContinuationsAsynchronously"/> 另外保证放行时的续体不在首个调用者
/// 的栈上串行跑完。</para>
///
/// <para><b>首个调用者失败也要放行。</b> 放行写在 <c>finally</c> 里：否则首轮订阅一抛异常，其余消费组就永远
/// 等下去——静默地再也不消费任何消息。首个调用者自己的异常照常传播给它自己的调用方。</para>
/// </summary>
internal sealed class FirstSubscriptionGate
{
    private readonly TaskCompletionSource firstSubscriptionCompleted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int firstSubscriptionClaimed;

    public Task RunAsync(Func<Task> subscribe)
    {
        ArgumentNullException.ThrowIfNull(subscribe);

        return Interlocked.CompareExchange(ref firstSubscriptionClaimed, 1, 0) == 0
            ? RunFirstSubscriptionAsync(subscribe)
            : RunAfterFirstSubscriptionAsync(subscribe);
    }

    private async Task RunFirstSubscriptionAsync(Func<Task> subscribe)
    {
        try
        {
            await subscribe().ConfigureAwait(false);
        }
        finally
        {
            firstSubscriptionCompleted.TrySetResult();
        }
    }

    private async Task RunAfterFirstSubscriptionAsync(Func<Task> subscribe)
    {
        await firstSubscriptionCompleted.Task.ConfigureAwait(false);
        await subscribe().ConfigureAwait(false);
    }
}

internal sealed class DecoratedConsumerClient(IConsumerClient inner, FirstSubscriptionGate firstSubscriptionGate)
    : IConsumerClient
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

    public Task SubscribeAsync(IEnumerable<string> topics) =>
        firstSubscriptionGate.RunAsync(() => inner.SubscribeAsync(topics));

    public Task ListeningAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        inner.ListeningAsync(timeout, cancellationToken);

    public Task CommitAsync(object? sender) => inner.CommitAsync(sender);

    public Task RejectAsync(object? sender) => inner.RejectAsync(sender);

    public ValueTask DisposeAsync() => inner.DisposeAsync();
}
