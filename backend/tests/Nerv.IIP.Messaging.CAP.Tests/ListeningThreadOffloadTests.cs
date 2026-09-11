using DotNetCore.CAP.Messages;
using DotNetCore.CAP.Transport;
using Xunit;

namespace Nerv.IIP.Messaging.CAP.Tests;

/// <summary>
/// #3352（#3236 拆解 4/5）：<see cref="DecoratedConsumerClient.ListeningAsync"/> 必须把 inner 的<b>永久阻塞</b>
/// 挪到专用线程，让调用它的那条线程池 worker 被立刻交还。
///
/// <para><b>被复刻的上游形状</b>（对 10.0.1 反编译实读）：<c>RedisConsumerClient.ListeningAsync</c>
/// <b>不是 <c>async</c></b>，它 fire-and-forget 掉轮询之后进入
/// <c>while (true) { ThrowIfCancellationRequested(); WaitHandle.WaitOne(timeout); }</c>——永不正常返回、
/// 同步阻塞调用线程，取消时<b>同步抛出</b> <see cref="OperationCanceledException"/>。
/// <see cref="BlockingConsumerClient"/> 逐条复刻这四点；夹具只要有一点不同（比如写成 <c>async</c>），
/// 这组用例就不再测的是真实缺陷。</para>
///
/// <para>⚠️ <b>为什么每次都从线程池线程发起调用</b>：这组断言的核心是「inner 不在池线程上」。
/// 如果发起调用的那条线程本身就不是池线程，那么<b>去掉专用线程包装之后这句话照样成立</b>——断言就失去
/// 全部鉴别力。所以每个用例都先经 <see cref="ThreadPool.QueueUserWorkItem(WaitCallback)"/> 落到池线程，
/// 并且把「调用方确实是池线程」本身也断言出来。</para>
///
/// <para>⚠️ <b>为什么不直接 <c>await client.ListeningAsync(...)</c></b>：去掉包装的变异会让这个调用<b>永不返回</b>，
/// 直接调用会把用例挂死而不是判红。所有发起都走 <see cref="StartListeningAsync"/> 的有界等待，
/// 变异下表现为<b>超时判红</b>而不是挂住。</para>
/// </summary>
public sealed class ListeningThreadOffloadTests
{
    /// <summary>有界等待上限：正常路径是亚毫秒级；只有变异体才会走到这个上限。</summary>
    private static readonly TimeSpan Bounded = TimeSpan.FromSeconds(10);

    /// <summary>传给 inner 的 poll timeout，等价于 CAP 的 <c>_pollingDelay</c>。</summary>
    private static readonly TimeSpan PollTimeout = TimeSpan.FromMilliseconds(20);

    /// <summary>
    /// ⭐ 验收 1 + 缺陷本身：调用方（线程池线程）拿回控制权，而 inner 的阻塞跑在<b>非线程池</b>线程上。
    /// </summary>
    [Fact]
    public async Task Listening_releases_the_calling_pool_thread_and_blocks_on_a_dedicated_thread()
    {
        var inner = new BlockingConsumerClient();
        var client = new DecoratedConsumerClient(inner);
        using var cancellation = new CancellationTokenSource();

        var (callerWasPoolThread, listening) = await StartListeningAsync(client, cancellation.Token);

        // 前提断言：没有这一条，下面那句「inner 不在池线程」就没有鉴别力。
        Assert.True(callerWasPoolThread, "用例必须从线程池线程发起，否则本组断言不成立。");

        await inner.Entered.Task.WaitAsync(Bounded);

        Assert.False(inner.EnteredOnThreadPoolThread);          // 验收 1
        Assert.NotEqual(inner.ExecutingThreadId, Environment.CurrentManagedThreadId);
        Assert.False(listening.IsCompleted);                    // inner 仍卡在死循环里，而调用方早已返回

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => listening);
    }

    /// <summary>验收 3：取消后装饰器返回的 Task 以 <see cref="OperationCanceledException"/> 结束。</summary>
    [Fact]
    public async Task Cancellation_surfaces_as_operation_canceled_on_the_returned_task()
    {
        var inner = new BlockingConsumerClient();
        var client = new DecoratedConsumerClient(inner);
        using var cancellation = new CancellationTokenSource();

        var (_, listening) = await StartListeningAsync(client, cancellation.Token);
        await inner.Entered.Task.WaitAsync(Bounded);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => listening);
    }

    /// <summary>
    /// 验收 4：专用线程**不泄漏**——取消后它必须真的退出。
    /// <para>用 <see cref="Thread.Join(TimeSpan)"/> 直接盯<b>那一条</b>线程对象，不是数进程线程总数
    /// （总数会被别的用例和运行时自身的线程淹没，没有归因力）。</para>
    /// </summary>
    [Fact]
    public async Task Dedicated_thread_exits_after_cancellation_and_does_not_leak()
    {
        var inner = new BlockingConsumerClient();
        var client = new DecoratedConsumerClient(inner);
        using var cancellation = new CancellationTokenSource();

        var (_, listening) = await StartListeningAsync(client, cancellation.Token);
        await inner.Entered.Task.WaitAsync(Bounded);

        var dedicated = inner.ExecutingThread!;
        Assert.False(dedicated.IsThreadPoolThread);
        Assert.True(dedicated.IsAlive);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => listening);

        Assert.True(dedicated.Join(Bounded), "取消之后专用线程必须退出，否则每个消费组泄漏一条线程。");
        Assert.False(dedicated.IsAlive);
    }

    /// <summary>
    /// 验收 4 的另一面：token <b>已取消</b>时，连专用线程都不该起。
    ///
    /// <para>把 <c>cancellationToken</c> 交给 <c>StartNew</c> 的承重之处就在这里——token 已取消时任务直接转
    /// <c>Canceled</c>、<b>委托根本不被调度</b>；若改传 <c>CancellationToken.None</c>，委托会被调度、
    /// 专用线程会被起起来、进 inner 再立刻抛出，等于为一个已经取消的订阅白付一条线程。
    /// 宿主关闭与消费组注册重叠时这条路径是可达的（CAP 传的是它自己的 <c>_cts.Token</c>）。</para>
    ///
    /// <para>⚠️ 仅断言「await 抛 <see cref="OperationCanceledException"/>」区分不了这两种写法：
    /// <c>Faulted</c>(内含 OCE) 与 <c>Canceled</c> 在 <c>await</c> 处都抛 OCE。所以这里断的是
    /// <b>线程有没有被起</b>与 <c>IsCanceled</c>。</para>
    /// </summary>
    [Fact]
    public async Task Already_cancelled_token_starts_no_dedicated_thread()
    {
        var inner = new BlockingConsumerClient();
        var client = new DecoratedConsumerClient(inner);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var (_, listening) = await StartListeningAsync(client, cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => listening);
        Assert.True(listening.IsCanceled);
        Assert.Null(inner.ExecutingThread);
    }

    /// <summary>
    /// 边界：inner 若<b>正常返回</b>一个已完成的 Task（不是上游那种永不返回的形态），装饰器必须原样透传完成，
    /// 不能因为多了一层 <c>StartNew</c>/<c>Unwrap</c> 就把结果吞掉或改变完成语义。
    /// </summary>
    [Fact]
    public async Task Inner_that_completes_normally_is_passed_through_unchanged()
    {
        var inner = new CompletingConsumerClient();
        var client = new DecoratedConsumerClient(inner);

        await client.ListeningAsync(PollTimeout, CancellationToken.None).WaitAsync(Bounded);

        Assert.Equal(1, inner.ListeningCalls);
        Assert.Equal(PollTimeout, inner.ObservedTimeout);
    }

    /// <summary>
    /// 从<b>线程池线程</b>发起 <c>ListeningAsync</c>，回传「调用方是否池线程」与它返回的 Task。
    /// 去掉专用线程包装的变异会让调用永不返回 ⇒ 这里<b>有界超时判红</b>，不会把用例挂死。
    /// </summary>
    private static async Task<(bool CallerWasPoolThread, Task Listening)> StartListeningAsync(
        DecoratedConsumerClient client,
        CancellationToken cancellationToken)
    {
        var caller = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var returned = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);

        ThreadPool.QueueUserWorkItem(_ =>
        {
            caller.TrySetResult(Thread.CurrentThread.IsThreadPoolThread);
            try
            {
                returned.TrySetResult(client.ListeningAsync(PollTimeout, cancellationToken));
            }
            catch (Exception exception)
            {
                returned.TrySetException(exception);
            }
        });

        var callerWasPoolThread = await caller.Task.WaitAsync(Bounded);
        var listening = await returned.Task.WaitAsync(Bounded);
        return (callerWasPoolThread, listening);
    }

    /// <summary>逐条复刻上游 <c>RedisConsumerClient.ListeningAsync</c>：非 async、永不正常返回、同步阻塞、取消时同步抛。</summary>
    private sealed class BlockingConsumerClient : StubConsumerClient
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool EnteredOnThreadPoolThread { get; private set; }

        public Thread? ExecutingThread { get; private set; }

        public int ExecutingThreadId { get; private set; }

        public override Task ListeningAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            ExecutingThread = Thread.CurrentThread;
            ExecutingThreadId = Environment.CurrentManagedThreadId;
            EnteredOnThreadPoolThread = Thread.CurrentThread.IsThreadPoolThread;
            Entered.TrySetResult();

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                cancellationToken.WaitHandle.WaitOne(timeout);
            }
        }
    }

    private sealed class CompletingConsumerClient : StubConsumerClient
    {
        public int ListeningCalls { get; private set; }

        public TimeSpan ObservedTimeout { get; private set; }

        public override Task ListeningAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            ListeningCalls++;
            ObservedTimeout = timeout;
            return Task.CompletedTask;
        }
    }

    private abstract class StubConsumerClient : IConsumerClient
    {
        public BrokerAddress BrokerAddress => new("stub", "endpoint-3352");

        public Func<TransportMessage, object?, Task>? OnMessageCallback { get; set; }

        public Action<LogMessageEventArgs>? OnLogCallback { get; set; }

        public Task<ICollection<string>> FetchTopicsAsync(IEnumerable<string> topicNames) =>
            Task.FromResult<ICollection<string>>(topicNames.ToArray());

        public Task SubscribeAsync(IEnumerable<string> topics) => Task.CompletedTask;

        public abstract Task ListeningAsync(TimeSpan timeout, CancellationToken cancellationToken);

        public Task CommitAsync(object? sender) => Task.CompletedTask;

        public Task RejectAsync(object? sender) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
