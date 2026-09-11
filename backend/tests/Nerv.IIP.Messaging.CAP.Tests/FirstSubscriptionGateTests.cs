using DotNetCore.CAP;
using DotNetCore.CAP.Messages;
using DotNetCore.CAP.Transport;
using Xunit;

namespace Nerv.IIP.Messaging.CAP.Tests;

/// <summary>
/// #3351（#3236 拆解 3/5）：<b>首轮订阅闸门</b>。
///
/// <para><b>被测不变量。</b> 上游 <c>IConsumerRegister.Default.ExecuteAsync</c> 的 <c>foreach</c>
/// <b>同步</b>跑完（组内只 <c>await</c> 两个同步已完成的调用），每组的
/// <see cref="IConsumerClient.SubscribeAsync"/> 由一个不被等待的
/// <c>Task.Factory.StartNew(..., LongRunning)</c> 发起 ⇒ N 个组的订阅同时起飞。闸门要保证：
/// <b>第一个订阅独占执行</b>，其余异步等它完成后<b>一次性全部放行</b>。</para>
///
/// <para><b>为什么派发循环写成同步的。</b> 这里逐个调用 <c>SubscribeAsync</c> 并只收集返回的
/// <see cref="Task"/>、不 <c>await</c>，是为了复刻上游那个同步 <c>foreach</c> 的形状；也正因为它是同步的，
/// 「派发完成时在飞数是多少」是一个<b>不含任何时序假设</b>的读数：没有闸门时派发循环自己就把 N 个订阅都推
/// 进去了，有闸门时只推得进第一个。</para>
///
/// <para><b>边界。</b> 这些用例只证明闸门的并发形状，<b>不</b>证明它在真实 Redis 上消除了在途连接阻塞——
/// 那条由两臂实测回答，读数写在 PR 正文。</para>
/// </summary>
public sealed class FirstSubscriptionGateTests
{
    /// <summary>MES 宿主的消费组数（#3351 票面两条独立口径核到 18）。这个值只影响规模，不是断言的承重处。</summary>
    private const int ConsumerGroupCount = 18;

    /// <summary>只在失败路径上兑现：任何一个有界等待走到这里都说明不变量已经破了，用它把挂死变成红。</summary>
    private static readonly TimeSpan FailureTimeout = TimeSpan.FromSeconds(30);

    /// <summary>验收 1：首个订阅独占执行 —— 派发完 18 个订阅之后，在飞的 <c>SubscribeAsync</c> 只有 1 个。</summary>
    [Fact]
    public async Task FirstSubscription_RunsExclusively_WhileEveryOtherConsumerGroupWaits()
    {
        var run = await RunFirstRoundAsync();

        Assert.Equal(1, run.InFlightAfterSynchronousDispatch);
        Assert.Equal(1, run.PeakInFlightWhileFirstSubscriptionRan);
        Assert.Equal(["enter:group-01"], run.TranscriptAfterSynchronousDispatch);

        // 首个订阅退出，必须早于其余任何一个进入。
        Assert.Equal("exit:group-01", run.Transcript[1]);
    }

    /// <summary>
    /// 验收 3：闸门<b>只拦首轮</b>。首个订阅完成后其余 17 个必须真并发跑，否则闸门就成了新的启动期性能缺陷。
    /// </summary>
    [Fact]
    public async Task AfterTheFirstSubscription_EveryRemainingConsumerGroupRunsConcurrently()
    {
        var run = await RunFirstRoundAsync();

        Assert.Equal(ConsumerGroupCount - 1, run.PeakConcurrentFollowerSubscriptions);
        Assert.Equal(ConsumerGroupCount, run.Transcript.Count(entry => entry.StartsWith("enter:", StringComparison.Ordinal)));
        Assert.Equal(ConsumerGroupCount, run.Transcript.Count(entry => entry.StartsWith("exit:", StringComparison.Ordinal)));
    }

    /// <summary>
    /// 验收：等待必须是<b>异步</b>的。上游那个 <c>foreach</c> 是同步跑的，闸门若用 <c>.Wait()</c> /
    /// <c>.Result</c> / <c>lock</c> 阻塞等待，第二个消费组就会把派发线程钉住 —— 本票就从「消除线程占用」
    /// 变成「新增线程占用源」。这里让派发循环整体在一个 <see cref="Task"/> 上跑并有界等它返回：
    /// 阻塞实现下它永远回不来，走 <see cref="FailureTimeout"/> 变红。
    /// </summary>
    [Fact]
    public async Task DispatchingEveryConsumerGroup_ReturnsWithoutBlockingTheCaller()
    {
        var probe = new SubscribeProbe(expectedFollowers: ConsumerGroupCount - 1);
        var gate = new FirstSubscriptionGate();
        var clients = CreateClients(probe, gate);

        var dispatch = Task.Run(() => clients.Select(client => client.SubscribeAsync(["topic"])).ToArray());
        var subscriptions = await dispatch.WaitAsync(FailureTimeout);

        // 派发已经返回，而首个订阅仍未完成 ⇒ 没有任何一次调用阻塞了派发方。
        Assert.All(subscriptions, subscription => Assert.False(subscription.IsCompleted));

        probe.ReleaseFirstSubscription();
        await probe.AllFollowersEntered.WaitAsync(FailureTimeout);
        probe.ReleaseFollowers();
        await Task.WhenAll(subscriptions).WaitAsync(FailureTimeout);
    }

    /// <summary>
    /// 放行写在 <c>finally</c> 里：首轮订阅失败时其余消费组仍须放行，否则整个宿主静默地再也不消费任何消息。
    /// 首个调用者自己的异常照常传播给它自己的调用方，不波及其余。
    /// </summary>
    [Fact]
    public async Task WhenTheFirstSubscriptionFails_EveryRemainingConsumerGroupIsStillReleased()
    {
        var probe = new SubscribeProbe(expectedFollowers: ConsumerGroupCount - 1) { FirstSubscriptionFailure = "first subscription failed" };
        var gate = new FirstSubscriptionGate();
        var clients = CreateClients(probe, gate);

        var subscriptions = clients.Select(client => client.SubscribeAsync(["topic"])).ToArray();
        probe.ReleaseFirstSubscription();

        var firstFailure = await Assert.ThrowsAsync<InvalidOperationException>(() => subscriptions[0].WaitAsync(FailureTimeout));
        Assert.Equal("first subscription failed", firstFailure.Message);

        await probe.AllFollowersEntered.WaitAsync(FailureTimeout);
        probe.ReleaseFollowers();
        await Task.WhenAll(subscriptions.Skip(1)).WaitAsync(FailureTimeout);

        Assert.All(subscriptions.Skip(1), subscription => Assert.Equal(TaskStatus.RanToCompletion, subscription.Status));
    }

    /// <summary>
    /// 闸门的作用域是<b>工厂</b>（= 宿主）：同一个 <see cref="DecoratedConsumerClientFactory"/> 发出的每个
    /// client 共享一个闸门，否则每个消费组各拿一个闸门 ⇒ 谁也拦不住谁，闸门等于不存在。
    /// </summary>
    [Fact]
    public async Task EveryClientFromOneFactory_SharesTheSameGate()
    {
        var probe = new SubscribeProbe(expectedFollowers: 1);
        var factory = new DecoratedConsumerClientFactory(
            new TransportConsumerClientFactory(new ProbeConsumerClientFactory(probe)));

        var first = await factory.CreateAsync("group-01", 1);
        var second = await factory.CreateAsync("group-02", 1);

        var firstSubscription = first.SubscribeAsync(["topic"]);
        var secondSubscription = second.SubscribeAsync(["topic"]);

        Assert.Equal(1, probe.InFlight);
        Assert.False(secondSubscription.IsCompleted);

        probe.ReleaseFirstSubscription();
        await probe.AllFollowersEntered.WaitAsync(FailureTimeout);
        probe.ReleaseFollowers();
        await Task.WhenAll(firstSubscription, secondSubscription).WaitAsync(FailureTimeout);
    }

    /// <summary>闸门只改并发形状，不改参数：inner 收到的 topics 必须逐字一致，顺序一致。</summary>
    [Fact]
    public async Task TheGate_ForwardsTheTopicsVerbatim()
    {
        var probe = new SubscribeProbe(expectedFollowers: 0);
        var client = new DecoratedConsumerClient(probe.CreateInner("group-01"), new FirstSubscriptionGate());

        var subscription = client.SubscribeAsync(["topic-b", "topic-a", "topic-b"]);
        probe.ReleaseFirstSubscription();
        await subscription.WaitAsync(FailureTimeout);

        Assert.Equal([("group-01", "topic-b|topic-a|topic-b")], probe.ObservedTopics);
    }

    private static IConsumerClient[] CreateClients(SubscribeProbe probe, FirstSubscriptionGate gate) =>
        [.. Enumerable
            .Range(1, ConsumerGroupCount)
            .Select(index => new DecoratedConsumerClient(probe.CreateInner($"group-{index:00}"), gate))];

    /// <summary>
    /// 一次首轮：同步派发全部消费组 → 取「派发返回那一刻」的快照 → 放行首个订阅 → 等其余全部进入 → 放行。
    /// 快照是这组用例的主读数，取得它的过程里<b>没有任何时序假设</b>。
    /// </summary>
    private static async Task<FirstRoundObservations> RunFirstRoundAsync()
    {
        var probe = new SubscribeProbe(expectedFollowers: ConsumerGroupCount - 1);
        var gate = new FirstSubscriptionGate();
        var clients = CreateClients(probe, gate);

        // 复刻上游的同步 foreach：逐个调用、只收集返回的 Task。ToArray 把整个循环在这里跑完。
        var subscriptions = clients.Select(client => client.SubscribeAsync(["topic"])).ToArray();

        var inFlightAfterDispatch = probe.InFlight;
        var transcriptAfterDispatch = probe.Transcript;

        probe.ReleaseFirstSubscription();

        // 串行化了「首轮之后」的过度修复会让这条走到超时；捕获它而不是让它炸在这里，是为了让
        // 「独占」与「只拦首轮」两条断言各自独立地红/绿，而不是被同一个超时一起带走。
        var followersEnteredConcurrently = true;
        try
        {
            await probe.AllFollowersEntered.WaitAsync(FailureTimeout);
        }
        catch (TimeoutException)
        {
            followersEnteredConcurrently = false;
        }

        probe.ReleaseFollowers();
        await Task.WhenAll(subscriptions).WaitAsync(FailureTimeout);

        return new FirstRoundObservations(
            inFlightAfterDispatch,
            transcriptAfterDispatch,
            probe.PeakInFlightWhileFirstSubscriptionRan,
            followersEnteredConcurrently ? probe.PeakConcurrentFollowerSubscriptions : 0,
            probe.Transcript);
    }

    private sealed record FirstRoundObservations(
        int InFlightAfterSynchronousDispatch,
        IReadOnlyList<string> TranscriptAfterSynchronousDispatch,
        int PeakInFlightWhileFirstSubscriptionRan,
        int PeakConcurrentFollowerSubscriptions,
        IReadOnlyList<string> Transcript);

    /// <summary>
    /// 统计 <see cref="IConsumerClient.SubscribeAsync"/> 的进入/退出与<b>同时在飞数</b>。
    /// 进入后不自行结束，等测试显式放行 —— 所以「在飞数」是测试自己决定的时刻上的读数，不靠 sleep 猜。
    /// </summary>
    private sealed class SubscribeProbe(int expectedFollowers)
    {
        private readonly object sync = new();
        private readonly List<string> transcript = [];
        private readonly List<(string Group, string Topics)> observedTopics = [];
        private readonly TaskCompletionSource firstSubscriptionRelease = new();
        private readonly TaskCompletionSource followerRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource allFollowersEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int entered;
        private int inFlight;
        private int followersInFlight;
        private bool firstSubscriptionExited;

        public string? FirstSubscriptionFailure { get; init; }

        public Task AllFollowersEntered => expectedFollowers == 0 ? Task.CompletedTask : allFollowersEntered.Task;

        public int InFlight { get { lock (sync) { return inFlight; } } }

        public int PeakInFlightWhileFirstSubscriptionRan { get; private set; }

        public int PeakConcurrentFollowerSubscriptions { get; private set; }

        public IReadOnlyList<string> Transcript { get { lock (sync) { return [.. transcript]; } } }

        public IReadOnlyList<(string Group, string Topics)> ObservedTopics
        {
            get { lock (sync) { return [.. observedTopics]; } }
        }

        public void ReleaseFirstSubscription() => firstSubscriptionRelease.TrySetResult();

        public void ReleaseFollowers() => followerRelease.TrySetResult();

        public IConsumerClient CreateInner(string group) => new ProbeConsumerClient(this, group);

        private async Task SubscribeAsync(string group, IEnumerable<string> topics)
        {
            bool isFirst;
            lock (sync)
            {
                entered++;
                isFirst = entered == 1;
                inFlight++;
                transcript.Add($"enter:{group}");
                observedTopics.Add((group, string.Join('|', topics)));
                if (!firstSubscriptionExited)
                {
                    PeakInFlightWhileFirstSubscriptionRan = Math.Max(PeakInFlightWhileFirstSubscriptionRan, inFlight);
                }

                if (!isFirst)
                {
                    followersInFlight++;
                    PeakConcurrentFollowerSubscriptions =
                        Math.Max(PeakConcurrentFollowerSubscriptions, followersInFlight);
                    if (followersInFlight >= expectedFollowers)
                    {
                        allFollowersEntered.TrySetResult();
                    }
                }
            }

            if (isFirst)
            {
                await firstSubscriptionRelease.Task.ConfigureAwait(false);
            }
            else
            {
                await followerRelease.Task.ConfigureAwait(false);
            }

            lock (sync)
            {
                inFlight--;
                if (isFirst)
                {
                    firstSubscriptionExited = true;
                }
                else
                {
                    followersInFlight--;
                }

                transcript.Add($"exit:{group}");
            }

            if (isFirst && FirstSubscriptionFailure is not null)
            {
                throw new InvalidOperationException(FirstSubscriptionFailure);
            }
        }

        private sealed class ProbeConsumerClient(SubscribeProbe probe, string group) : IConsumerClient
        {
            public Func<TransportMessage, object?, Task>? OnMessageCallback { get; set; }

            public Action<LogMessageEventArgs>? OnLogCallback { get; set; }

            public BrokerAddress BrokerAddress => new("probe", group);

            public Task<ICollection<string>> FetchTopicsAsync(IEnumerable<string> topicNames) =>
                Task.FromResult<ICollection<string>>([.. topicNames]);

            public Task SubscribeAsync(IEnumerable<string> topics) => probe.SubscribeAsync(group, topics);

            public Task ListeningAsync(TimeSpan timeout, CancellationToken cancellationToken) => Task.CompletedTask;

            public Task CommitAsync(object? sender) => Task.CompletedTask;

            public Task RejectAsync(object? sender) => Task.CompletedTask;

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class ProbeConsumerClientFactory(SubscribeProbe probe) : IConsumerClientFactory
    {
        public Task<IConsumerClient> CreateAsync(string groupName, byte groupConcurrent) =>
            Task.FromResult(probe.CreateInner(groupName));
    }
}
