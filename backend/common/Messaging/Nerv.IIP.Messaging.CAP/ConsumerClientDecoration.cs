using DotNetCore.CAP;
using DotNetCore.CAP.Messages;
using DotNetCore.CAP.Transport;
using Microsoft.Extensions.DependencyInjection;

namespace Nerv.IIP.Messaging.CAP;

/// <summary>
/// Wraps the transport's <see cref="IConsumerClientFactory"/> and every <see cref="IConsumerClient"/> it creates
/// so that later work has one place to hook into. #3350 建这层骨架时留的挂载点，#3352 是第一个挂上来的
/// （<see cref="DecoratedConsumerClient.ListeningAsync"/> 的专用线程）。#3351 的首轮订阅闸门已随连接池方向
/// 一并关闭，不再是本骨架的下游。
///
/// <para><b>#3352 起，<see cref="DecoratedConsumerClient.ListeningAsync"/> 不再是纯转发</b>：它把 inner 的
/// 永久阻塞挪到专用线程上。<b>其余每一个成员仍然逐字转发 inner</b>，没有任何自身行为；另一处有自身行为的代码
/// 在 <b>DI 组装期</b>——<see cref="AddServices"/> 的 fail closed。</para>
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
    internal IConsumerClientFactory Inner => transport.Inner;

    public async Task<IConsumerClient> CreateAsync(string groupName, byte groupConcurrent) =>
        new DecoratedConsumerClient(await transport.Inner.CreateAsync(groupName, groupConcurrent));
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

    /// <summary>
    /// #3352：把 inner 的<b>永久阻塞</b>挪到专用线程，让它不再常驻占用一条线程池 worker。
    ///
    /// <para><b>缺陷形态</b>（对 <c>DotNetCore.CAP.RedisStreams</c> 10.0.1 反编译实读）：上游
    /// <c>RedisConsumerClient.ListeningAsync</c> <b>不是 <c>async</c></b>，它 fire-and-forget 掉轮询任务之后进入
    /// <c>while (true) { ThrowIfCancellationRequested(); WaitHandle.WaitOne(timeout); }</c>——<b>永不返回、
    /// 同步阻塞调用线程</b>。
    /// </para>
    ///
    /// <para><b>为什么落在线程池上</b>：调用点 <c>ConsumerRegister.ExecuteAsync</c> 确实用了
    /// <c>Task.Factory.StartNew(…, TaskCreationOptions.LongRunning, …)</c>，但它的委托是 <c>async</c>，
    /// <b>专用线程在第一个真 await（<c>CreateAsync</c>）处就已经交还</b>；等执行到 <c>ListeningAsync</c> 时
    /// 续体早已跑在线程池线程上。⇒ 上游那个 <c>LongRunning</c> <b>完全白给</b>，每个消费组常驻钉住一条 worker
    /// 且永不释放（线程数 = 消费组数 × <c>ConsumerThreadCount</c>）。
    /// </para>
    ///
    /// <para>⚠️ <b>常驻占线程的不是轮询循环</b>：<c>PollStreamsLatestMessagesAsync</c> 是 await 链、不常驻。
    /// 归因写错会让人去调 poll delay，那治不到这里。
    /// </para>
    ///
    /// <para><b>为什么用 <c>Func&lt;Task&gt;</c> + <c>Unwrap()</c> 而不是 <c>Action</c></b>：真实 inner 永不正常返回
    /// （取消时<b>同步抛出</b> <see cref="OperationCanceledException"/>，因为它不是 <c>async</c>，异常不会变成
    /// faulted Task）；但装饰器不能只对这一种 inner 成立——inner 若返回一个真的 <c>Task</c>，
    /// <c>Unwrap()</c> 会接着等它，并在它完成后<b>立刻交还专用线程</b>。两种形态都正确传播。
    /// </para>
    ///
    /// <para>把 <paramref name="cancellationToken"/> 传给 <c>StartNew</c>：委托抛出的 OCE 与该 token 匹配时，
    /// 任务转为 <c>Canceled</c> 而不是 <c>Faulted</c>，<c>await</c> 它照样抛 <see cref="OperationCanceledException"/>
    /// ——调用方 <c>ConsumerRegister</c> 正是用 <c>catch (OperationCanceledException)</c> 收尾的。
    /// <c>DenyChildAttach</c> 防止 inner 内部起的任务把专用线程的生命周期拖长。
    /// </para>
    /// </summary>
    public Task ListeningAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        Task.Factory.StartNew(
            () => inner.ListeningAsync(timeout, cancellationToken),
            cancellationToken,
            TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
            TaskScheduler.Default)
            .Unwrap();

    public Task CommitAsync(object? sender) => inner.CommitAsync(sender);

    public Task RejectAsync(object? sender) => inner.RejectAsync(sender);

    public ValueTask DisposeAsync() => inner.DisposeAsync();
}
