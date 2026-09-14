using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using DotNetCore.CAP;
using DotNetCore.CAP.Internal;
using DotNetCore.CAP.Messages;
using DotNetCore.CAP.Transport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Nerv.IIP.Testing;
using StackExchange.Redis;
using Xunit;

namespace Nerv.IIP.Messaging.CAP.Tests;

/// <summary>
/// #3222 的<b>真实</b>一层：真 Redis、真 <c>EXISTS</c>/<c>XGROUP</c>、真
/// <c>DotNetCore.CAP.RedisStreams.RedisConsumerClient.SubscribeAsync</c>、真 <c>ConsumerRegister</c>、
/// 真 <c>TransportCheckProcessor</c>、真 PostgreSQL CAP 存储。
///
/// <para>⭐ <b>时间线取的是票面说的那条实际可达边沿：服务在 Redis 抖动期间重启。</b>
/// 两条用例都走同一条三段式：</para>
/// <list type="number">
/// <item><description><b>健康宿主</b>先跑起来并真的消费掉一封 —— 消费组因此<b>已经存在于 Redis</b>，
/// 这既是「通路本来是通的」的前提断言，也是稳态部署的样子；</description></item>
/// <item><description>停掉它，<b>在故障中重启</b>第二个宿主：真实 <c>SubscribeAsync</c> 抛
/// <see cref="RedisConnectionException"/> / <see cref="RedisTimeoutException"/>；</description></item>
/// <item><description>清除故障，由<b>生产的</b> <c>TransportCheckProcessor</c> 自己恢复，
/// 断言在途与新发的业务都收敛、且不重复。</description></item>
/// </list>
///
/// <para>⚠️ <b>为什么必须先有一个健康宿主，这不是绕路</b>（本席位实测踩过）：Redis Streams 的消费组按
/// <c>StreamPosition.NewMessages</c> 创建（上游 <c>TryGetOrCreateStreamConsumerGroupAsync</c>），
/// <b>组创建之前 <c>XADD</c> 进去的条目对该组永远不可见</b>。若整条用例从「组从未建成」开始，
/// 「在途消息恢复后落地」在 Redis 语义上<b>根本不可能成立</b>，断言会变成在考证一个与本票无关的性质。
/// 组先存在之后，停机期间进流的条目会留在流里等新消费者取走 —— 那才是「恢复前后不丢业务」的真现场。</para>
///
/// <para><b>故障怎么造的</b>：<see cref="RedisFaultProxy"/> 是<b>本用例自有</b>的一个 loopback TCP 转发器，
/// 摆在本用例自己那条 CAP 连接与真实 Redis 之间。⛔ 它<b>不暂停、不重启、不改配置</b>共享 Redis——
/// lane 里其它成员和本用例的<b>核查连接</b>都直连真实端点，不经过它。两种故障都只作用在
/// 「经过这个转发器的那些 socket」上：</para>
/// <list type="bullet">
/// <item><description><b>连接失败</b>：接受连接后立刻带 <c>SO_LINGER(0)</c> 关闭 ⇒ 对端收到 RST。
/// 用 accept+reset 而不是「关掉监听端口」，是因为后者要在清除故障时重新绑定同一个端口
/// （CAP 的 <c>ConfigurationOptions</c> 在启动时就定死了 endpoint），而重绑定会踩 <c>TIME_WAIT</c>；
/// 也不用 bind-without-listen —— 本仓 <c>NetworkFailureFixture</c> 已记过 macOS/BSD 会静默丢 SYN
/// 而不回 RST，那会把「立刻拒绝」变成「等满预算」。</description></item>
/// <item><description><b>真实超时</b>：先把握手逐字节转发过去（多路复用器因此<b>确实连上了</b>），
/// 嗅到本连接上第一条 <c>EXISTS</c> 命令后<b>停止转发两个方向</b>并保持 socket 不关 ⇒
/// 该命令永远等不到答复 ⇒ <c>StackExchange.Redis</c> 抛 <see cref="RedisTimeoutException"/>。
/// 嗅 <c>EXISTS</c> 而不是 <c>XGROUP</c>：上游
/// <c>RedisStreamManagerExtensions.TryGetOrCreateStreamConsumerGroupAsync</c> 的<b>第一条</b>命令是
/// <c>database.KeyExistsAsync(stream)</c>，<c>XGROUP</c> 在它之后；嗅后者会漏掉真正的首触点。
/// ⓘ 实测顺带记一条：黑洞只作用在<b>发过 <c>EXISTS</c> 的那条连接</b>上，发布端那条连接（只发
/// <c>XADD</c>）不受影响 —— 所以超时用例里的在途消息是「进了流但没有消费者」，不是「发不出去」。</description></item>
/// </list>
///
/// <para>⭐ <b>「进入生产恢复分支」是怎么证的，别读成同义反复</b>：<c>ConsumerRegister</c> 在 <b>Redis</b>
/// transport 上把 <c>_isHealthy</c> 置 <c>false</c> 的位点<b>只有</b>两处 <c>catch (BrokerConnectionException)</c>
/// （<c>ExecuteAsync:124-128</c> 与 <c>:152-156</c>）——它的 <c>WriteLog</c> 分支里唯一与 Redis 有关的那格
/// （<c>MqLogType.RedisConsumeError</c>）置的是 <c>true</c>，其余置 <c>false</c> 的格全是
/// RabbitMQ / Kafka / NATS 专属。⇒ 在一个 Redis 宿主上观察到 <c>IsHealthy() == false</c>，
/// 等价于「一个 <see cref="BrokerConnectionException"/> 被那两支 catch 之一接住了」。
/// 本文件另从两个<b>独立入口</b>交叉取证：① 装饰器<b>内侧</b>的观察者记下 inner 真实抛出的异常类型与栈
/// （证明它来自真 <c>SubscribeAsync</c>）；② 日志捕获拿到 <c>ConsumerRegister</c> 记下的那条异常
/// （证明到达 CAP 的是转换后的 <see cref="BrokerConnectionException"/> 且 inner 原样保留）。</para>
///
/// <para>⛔ <b>恢复不是用例发起的</b>：全文件<b>零处</b>调用 <c>ReStartAsync</c> / <c>Pulse</c>，也不 force。
/// 清除故障之后只是有界地等，由生产的 <c>TransportCheckProcessor</c>（30 秒一轮，
/// <c>Processor/IProcessor.TransportCheck.cs:32-36</c>）自己扣扳机。等待上界取本仓既有的 90 秒
/// <see cref="EventuallyOptions"/> 口径，⛔ 没有调大 CAP 的 30 秒或任何 90 秒预算。</para>
/// </summary>
public sealed class RedisSubscriptionRecoveryRedisCapTests
{
    private static readonly EventuallyOptions Bounded =
        new(TimeSpan.FromSeconds(90), TimeSpan.FromMilliseconds(200), []);

    /// <summary>
    /// ⭐ <b>连接失败这一半</b>：真实 <see cref="RedisConnectionException"/> 发生在实际 <c>SubscribeAsync</c>、
    /// 进入生产恢复分支、故障期间<b>不虚报订阅成功</b>、故障清除后由既有 <c>TransportCheckProcessor</c> 恢复，
    /// 且<b>旁组与在途消息</b>都收敛、不重复。
    ///
    /// <para>写成一条而不是拆若干条，是因为它们共享同一条真实时间线：拆开就得把同一个真栈时间线跑若干遍，
    /// 而后面每一件在构造上都必须发生在前一件之后。</para>
    /// </summary>
    [RedisSubscriptionRecoveryRedisCapFact]
    public async Task Redis_cap_connection_failure_at_subscribe_recovers_through_the_existing_transport_check_without_losing_or_duplicating_work()
    {
        await using var probe = await RecoveryProbe.StartAsync(
            schema: "cap_issue3222_connection",
            topicSuffix: "i3222conn");

        // ⓪ 前提断言：健康宿主上通路本来是通的，两个消费组都真的建起来了。
        //    没有这条，后面「恢复后能消费」可能只是因为它从来就不需要恢复。
        await probe.AssertSteadyStateDeliversAsync("evt-main-steady", "evt-side-steady");
        var groupsBeforeFault = await probe.ReadConsumerGroupsAsync();
        Assert.Equal(2, groupsBeforeFault.Count);

        // ① 在故障中重启宿主：真实 Redis 连接失败确实发生在实际 SubscribeAsync 上。
        //    读数来自装饰器**内侧**的观察者，它拿到的是 inner 原样抛出的异常，转换还没发生。
        await probe.RestartUnderFaultAsync(RedisFaultProxy.FaultKind.ResetOnConnect);
        await probe.WaitAsync("每个消费组的真实 SubscribeAsync 都以 Redis 连接异常失败", () =>
        {
            var failures = probe.SubscribeFailures;
            Assert.NotEmpty(failures);
            Assert.All(failures, failure => Assert.Equal(nameof(RedisConnectionException), failure.ExceptionType));
            // 栈里必须出现真 transport 的那两帧，否则「发生于实际 Subscribe」只是推断。
            Assert.All(failures, failure => Assert.Contains(
                "DotNetCore.CAP.RedisStreams.RedisStreamManager.CreateStreamWithConsumerGroupAsync",
                failure.Stack,
                StringComparison.Ordinal));
            Assert.All(failures, failure => Assert.Contains(
                "DotNetCore.CAP.RedisStreams.RedisConsumerClient.SubscribeAsync",
                failure.Stack,
                StringComparison.Ordinal));
        });
        // 夹具自身的哨兵：RST 确实发生过。没有这条，「连不上」也可能是端口配错之类的别的原因。
        Assert.True(probe.ResetConnections > 0);

        // ② 进入生产恢复分支：Redis 宿主上 IsHealthy()==false 只可能来自 catch (BrokerConnectionException)。
        await probe.WaitAsync("ConsumerRegister 的健康位被翻成 false", () => Assert.False(probe.IsHealthy));
        await probe.AssertConvertedOneForOneAsync(nameof(RedisConnectionException));

        // ③ 不虚报订阅成功：故障期间新宿主一个消费组都没订阅上。
        Assert.Empty(probe.SubscribeSuccesses);

        // ④ 在途消息：故障仍未解除时发布两封（主组一封、旁组一封）。它们进 CAP outbox，
        //    发送失败后由 CAP 自己的重试留在途。
        await probe.PublishMainAsync("evt-main-inflight");
        await probe.PublishSideAsync("evt-side-inflight");

        probe.ClearFault();

        // ⑤ 恢复由**生产的** TransportCheckProcessor 扣扳机，用例只是等。
        await probe.WaitAsync("既有 TransportCheck 把消费者恢复成健康", () => Assert.True(probe.IsHealthy));

        // ⑥ 恢复前后不丢业务：在途的两封都落地；再发一封证明恢复后的通路是活的。
        await probe.PublishMainAsync("evt-main-after");
        await probe.WaitAsync("在途与恢复后的事件全部落到持久副作用上", async () =>
        {
            var effects = await probe.ReadEffectsAsync();
            var capState = await probe.DescribeCapStateAsync();
            Assert.True(effects.Any(effect => effect.EventId == "evt-main-inflight"), capState);
            Assert.True(effects.Any(effect => effect.EventId == "evt-side-inflight"), capState);
            Assert.True(effects.Any(effect => effect.EventId == "evt-main-after"), capState);
        });

        // ⑦ 重投不重复持久副作用：每个 eventId 恰好一行，稳态那两封也没有被重投第二次。
        var settled = await probe.ReadEffectsAsync();
        Assert.Equal(
            ["evt-main-after", "evt-main-inflight", "evt-main-steady", "evt-side-inflight", "evt-side-steady"],
            settled.Select(effect => effect.EventId).OrderBy(id => id, StringComparer.Ordinal));
        // 旁组确实是**另一个**消费组在处理，不是主组顺手吃掉的。
        Assert.Equal(
            RecoveryProbe.SideGroup,
            Assert.Single(settled, effect => effect.EventId == "evt-side-inflight").ConsumerGroup);
        Assert.Equal(
            RecoveryProbe.MainGroup,
            Assert.Single(settled, effect => effect.EventId == "evt-main-inflight").ConsumerGroup);

        // ⑧ 旧监听器/连接正确取消释放，不留下并行旧消费者。
        await probe.AssertNoParallelStaleConsumerAsync();
    }

    /// <summary>
    /// ⭐ <b>真实超时这一半</b>。与上一条的区别只在故障形态：这里多路复用器<b>真的连上了</b>，
    /// 是那条 <c>EXISTS</c> 命令等不到答复 ⇒ <see cref="RedisTimeoutException"/>。
    ///
    /// <para>这一条单独存在的理由：<see cref="RedisTimeoutException"/> 与
    /// <see cref="RedisConnectionException"/> <b>没有公共基类</b>（前者派生自 <see cref="TimeoutException"/>，
    /// 后者派生自 <see cref="RedisException"/>），生产侧是两条独立的 <c>catch</c>。
    /// 只跑连接失败那条，删掉超时那条 <c>catch</c> 会<b>全绿</b>。</para>
    /// </summary>
    [RedisSubscriptionRecoveryRedisCapFact]
    public async Task Redis_cap_timeout_at_subscribe_never_reports_a_subscription_and_recovers_through_the_existing_transport_check()
    {
        await using var probe = await RecoveryProbe.StartAsync(
            schema: "cap_issue3222_timeout",
            topicSuffix: "i3222time");

        await probe.AssertSteadyStateDeliversAsync("evt-main-steady", "evt-side-steady");

        await probe.RestartUnderFaultAsync(RedisFaultProxy.FaultKind.BlackHoleAfterCommand);
        await probe.WaitAsync("每个消费组的真实 SubscribeAsync 都以 Redis 超时失败", () =>
        {
            var failures = probe.SubscribeFailures;
            Assert.NotEmpty(failures);
            Assert.All(failures, failure => Assert.Equal(nameof(RedisTimeoutException), failure.ExceptionType));
            Assert.All(failures, failure => Assert.Contains(
                "DotNetCore.CAP.RedisStreams.RedisStreamManager.CreateStreamWithConsumerGroupAsync",
                failure.Stack,
                StringComparison.Ordinal));
        });
        // 夹具自身的哨兵：黑洞确实生效过。没有这条，「超时了」也可能只是机器慢。
        Assert.True(probe.BlackHoledConnections > 0);

        await probe.WaitAsync("ConsumerRegister 的健康位被翻成 false", () => Assert.False(probe.IsHealthy));
        await probe.AssertConvertedOneForOneAsync(nameof(RedisTimeoutException));

        Assert.Empty(probe.SubscribeSuccesses);

        await probe.PublishMainAsync("evt-timeout-inflight");
        probe.ClearFault();

        await probe.WaitAsync("既有 TransportCheck 把消费者恢复成健康", () => Assert.True(probe.IsHealthy));
        await probe.WaitAsync("超时期间的在途事件恢复后落地", async () => Assert.True(
            (await probe.ReadEffectsAsync()).Any(effect => effect.EventId == "evt-timeout-inflight"),
            await probe.DescribeCapStateAsync()));

        Assert.Equal(
            ["evt-main-steady", "evt-side-steady", "evt-timeout-inflight"],
            (await probe.ReadEffectsAsync())
                .Select(effect => effect.EventId)
                .OrderBy(id => id, StringComparer.Ordinal));
        await probe.AssertNoParallelStaleConsumerAsync();
    }

    // ----------------------------------------------------------------------------------------
    // 夹具
    // ----------------------------------------------------------------------------------------

    /// <summary>
    /// 一次完整现场：一个自有的故障转发器、一份真实 PostgreSQL 存储与副作用表，
    /// 以及<b>先后两个</b>真实 CAP 宿主（健康那个、故障中重启的那个）。
    /// </summary>
    private sealed class RecoveryProbe : IAsyncDisposable
    {
        public const string MainGroup = "issue3222.main";
        public const string SideGroup = "issue3222.side";

        private readonly RedisFaultProxy proxy;
        private readonly EffectStore effects;
        private readonly string topicPrefix;
        private readonly string capVersion;
        private readonly string redisEndpoint;
        private readonly string postgres;
        private CapHost host;
        private int disposed;

        private RecoveryProbe(
            RedisFaultProxy proxy,
            EffectStore effects,
            CapHost host,
            string topicPrefix,
            string capVersion,
            string redisEndpoint,
            string postgres)
        {
            this.proxy = proxy;
            this.effects = effects;
            this.host = host;
            this.topicPrefix = topicPrefix;
            this.capVersion = capVersion;
            this.redisEndpoint = redisEndpoint;
            this.postgres = postgres;
        }

        public bool IsHealthy => host.IsHealthy;

        public IReadOnlyList<ObservedFailure> SubscribeFailures => host.Observer.SubscribeFailures;

        public IReadOnlyList<string> SubscribeSuccesses => host.Observer.SubscribeSuccesses;

        public IReadOnlyList<LoggedBrokerFailure> BrokerConnectionFailuresLoggedByCap => host.FailureLog.Failures;

        public int BlackHoledConnections => proxy.BlackHoledConnections;

        public int ResetConnections => proxy.ResetConnections;

        public static async Task<RecoveryProbe> StartAsync(string schema, string topicSuffix)
        {
            var redisEndpoint = RequiredEnvironment("NERV_IIP_TEST_REDIS");
            var postgres = RequiredEnvironment("NERV_IIP_TEST_POSTGRES");
            // lane runner 把本成员的 Redis 命名空间交给 NERV_IIP_TEST_CAP_TOPIC_PREFIX；所有键必须落在
            // 它下面，否则 lane 的清理契约会判「留下外部键」。本地直跑时退回一个一次性前缀，
            // 保证连跑两轮不会踩到上一轮留下的消费组。
            var laneNamespace = Environment.GetEnvironmentVariable("NERV_IIP_TEST_CAP_TOPIC_PREFIX");
            var topicPrefix = string.IsNullOrWhiteSpace(laneNamespace)
                ? $"nerv-local-3222-{Guid.NewGuid():N}-{topicSuffix}"
                : laneNamespace + topicSuffix;

            var effects = new EffectStore(postgres, schema);
            await effects.ResetAsync();

            var proxy = await RedisFaultProxy.StartAsync(redisEndpoint, RedisFaultProxy.FaultKind.None);
            var host = await CapHost.StartAsync(proxy.Endpoint, postgres, schema, topicPrefix, topicSuffix, effects);
            return new RecoveryProbe(proxy, effects, host, topicPrefix, topicSuffix, redisEndpoint, postgres);
        }

        /// <summary>
        /// 前提断言：健康宿主上通路本来是通的。它同时把两个消费组<b>真的建到 Redis 上</b>——
        /// 这是后面「在途消息恢复后仍然可见」在 Redis Streams 语义下成立的前提。
        /// </summary>
        public async Task AssertSteadyStateDeliversAsync(string mainEventId, string sideEventId)
        {
            // ⚠️ 必须先等两个消费组**真的建到 Redis 上**才能发布。ConsumerRegister.ExecuteAsync 的消费线程是
            // fire-and-forget 起的，CapTestHost.WaitForCapBootstrapAsync 返回时订阅未必已完成；而 Redis Streams
            // 的组按 StreamPosition.NewMessages 创建 ⇒ **组创建之前 XADD 的条目对该组永远不可见**。
            // 本席位实测踩过这一格：少了这条等待时主组的稳态事件有时根本不会被消费（received 里只有旁组）。
            await WaitAsync("健康宿主上两个消费组都已经建到 Redis 上", async () =>
                Assert.Equal(2, (await ReadConsumerGroupsAsync()).Count));
            await PublishMainAsync(mainEventId);
            await PublishSideAsync(sideEventId);
            await WaitAsync("健康宿主上两个消费组都消费掉了各自的事件", async () =>
            {
                var settled = await ReadEffectsAsync();
                var capState = await DescribeCapStateAsync();
                Assert.True(
                    settled.Any(effect => effect.EventId == mainEventId && effect.ConsumerGroup == MainGroup),
                    capState);
                Assert.True(
                    settled.Any(effect => effect.EventId == sideEventId && effect.ConsumerGroup == SideGroup),
                    capState);
            });
        }

        /// <summary>
        /// 停掉健康宿主、开上故障、再起一个新宿主 —— 票面说的「服务在 Redis 抖动期间重启」。
        /// 旧宿主的收尾在这里就地断言：它的每个 client 都必须被释放、进过监听的都必须退出。
        /// </summary>
        public async Task RestartUnderFaultAsync(RedisFaultProxy.FaultKind fault)
        {
            var previous = host;
            await previous.StopAsync();
            await WaitAsync("旧宿主的每个 consumer client 都已释放、进过监听的都已退出", () =>
            {
                var clients = previous.Observer.Clients;
                Assert.NotEmpty(clients);
                Assert.All(clients, client => Assert.True(client.Disposed, previous.Observer.Dump()));
                Assert.All(
                    clients.Where(client => client.ListeningEntered),
                    client => Assert.True(client.ListeningExited, previous.Observer.Dump()));
            });
            await previous.DisposeAsync();

            proxy.SetFault(fault);
            host = await CapHost.StartAsync(
                proxy.Endpoint, postgres, effects.Schema, topicPrefix, capVersion, effects);
        }

        public void ClearFault() => proxy.ClearFault();

        public Task PublishMainAsync(string eventId) => host.PublishAsync(MainSubscriber.Topic, eventId);

        public Task PublishSideAsync(string eventId) => host.PublishAsync(SideSubscriber.Topic, eventId);

        public Task<IReadOnlyList<RecordedEffect>> ReadEffectsAsync() => effects.ReadAsync();

        public Task<string> DescribeCapStateAsync() => effects.DescribeCapStateAsync();

        /// <summary>
        /// 装饰器<b>内侧</b>观察到的每一次真实 <c>SubscribeAsync</c> 失败，都必须在 CAP 侧对应<b>恰好一条</b>
        /// <see cref="BrokerConnectionException"/>，且 inner 是 <paramref name="expectedInnerType"/>。
        ///
        /// <para>⭐ 用一一对应而不是「至少一条」：两个消费组各失败一次，只断言 <c>NotEmpty</c> 时
        /// 「只有一个组的异常被转换」会全绿。另外钉住失败确实覆盖了<b>两个</b>消费组（主组 + 旁组）。</para>
        /// </summary>
        public ValueTask AssertConvertedOneForOneAsync(string expectedInnerType) =>
            WaitAsync($"每次真实 SubscribeAsync 失败都恰好给 CAP 送去一条 BrokerConnectionException({expectedInnerType})", () =>
            {
                var failures = SubscribeFailures;
                var logged = BrokerConnectionFailuresLoggedByCap;
                Assert.NotEmpty(logged);
                Assert.Equal(failures.Count, logged.Count);
                Assert.Equal(2, failures.Select(failure => failure.Group).Distinct(StringComparer.Ordinal).Count());
                Assert.All(logged, entry => Assert.Equal("Broker Unreachable", entry.Message));
                Assert.All(logged, entry => Assert.Equal(expectedInnerType, entry.InnerExceptionType));
            });

        /// <summary>
        /// 真实 Redis 上这些 topic 当前有哪些消费组。⭐ 走<b>直连</b>真实端点的核查连接，
        /// 不经过故障转发器——否则「查不到组」可能只是因为查询本身也被故障挡住了。
        /// </summary>
        public async Task<IReadOnlyList<string>> ReadConsumerGroupsAsync()
        {
            var options = ConfigurationOptions.Parse(redisEndpoint);
            options.AbortOnConnectFail = false;
            await using var inspector = await ConnectionMultiplexer.ConnectAsync(options);
            var database = inspector.GetDatabase();
            var groups = new List<string>();
            foreach (var topic in new[] { MainSubscriber.Topic, SideSubscriber.Topic })
            {
                var key = (RedisKey)$"{topicPrefix}.{topic}";
                if (!await database.KeyExistsAsync(key)) continue;
                foreach (var group in await database.StreamGroupInfoAsync(key))
                {
                    groups.Add($"{key}/{group.Name}");
                }
            }

            return groups;
        }

        /// <summary>
        /// 「不留下并行旧消费者」的读数。
        ///
        /// <para>⚠️ <b>只能在 client 这一层观察</b>：上游 <c>RedisStreamManager.TryReadConsumerGroupAsync</c>
        /// 读组时把<b>消费者名写成组名本身</b>（<c>StreamReadGroupAsync(positions, consumerGroup, consumerGroup, …)</c>），
        /// 所以 Redis 侧的 <c>XINFO CONSUMERS</c> 对每个组<b>恒为 1</b>，无论进程里有几代 listener。
        /// ⛔ 拿它当读数会得到一条恒真的假断言。</para>
        ///
        /// <para>判据不依赖「一轮创建几个 client」这种会漂的宽度，只用可直接观察的三条：
        /// ① 每个消费组<b>至多一个</b>仍在监听的 client；② 仍在监听的 client 覆盖<b>全部</b>消费组
        /// （恢复之后不是只活了一个组）；③ 其余每一个 client 都已释放。</para>
        /// </summary>
        public async Task AssertNoParallelStaleConsumerAsync() =>
            await WaitAsync("恢复之后每个消费组恰好一个活 listener，其余 client 全部释放", () =>
            {
                var clients = host.Observer.Clients;
                var dump = host.Observer.Dump();
                Assert.NotEmpty(clients);

                var listening = clients.Where(client => client.ListeningEntered && !client.ListeningExited).ToArray();
                var groups = listening.Select(client => client.Group).ToArray();
                Assert.Equal(groups.Length, groups.Distinct(StringComparer.Ordinal).Count());
                Assert.Equal(2, groups.Length);

                Assert.All(
                    clients.Where(client => !listening.Contains(client)),
                    client => Assert.True(client.Disposed, dump));
            });

        public ValueTask WaitAsync(string condition, Action assertion) =>
            WaitAsync(condition, () =>
            {
                assertion();
                return Task.CompletedTask;
            });

        public ValueTask WaitAsync(string condition, Func<Task> assertion) =>
            Eventually.AssertAsync(
                condition: condition,
                assertion: async _ => await assertion(),
                options: Bounded);

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 1) return;

            try
            {
                proxy.ClearFault();
                await host.StopAsync();
            }
            catch (Exception)
            {
                // 收尾路径：宿主可能正停在故障现场，停机本身的失败不承担任何断言。
            }
            finally
            {
                await host.DisposeAsync();
                await proxy.DisposeAsync();
                await effects.DropAsync();
            }
        }

        private static string RequiredEnvironment(string name)
        {
            var value = Environment.GetEnvironmentVariable(name);
            Assert.False(string.IsNullOrWhiteSpace(value), $"{name} must be set for the Redis/CAP lane.");
            return value!;
        }
    }

    /// <summary>
    /// 一个真实 CAP 宿主：真 <c>AddCap</c>、真 <c>UseConfiguredTransport</c>（Redis）、真 PostgreSQL 存储、
    /// 真 <c>Bootstrapper</c>（以 hosted service 形态启停）、CAP 自带的全套 processor（
    /// <c>TransportCheckProcessor</c> 就在里面）。
    /// </summary>
    private sealed class CapHost : IAsyncDisposable
    {
        private readonly ServiceProvider provider;
        private readonly IHostedService bootstrapper;

        private CapHost(
            ServiceProvider provider,
            IHostedService bootstrapper,
            ObservingConsumerClientFactory observer,
            CapFailureLog failureLog)
        {
            this.provider = provider;
            this.bootstrapper = bootstrapper;
            Observer = observer;
            FailureLog = failureLog;
        }

        public ObservingConsumerClientFactory Observer { get; }

        public CapFailureLog FailureLog { get; }

        public bool IsHealthy => provider.GetRequiredService<IConsumerRegister>().IsHealthy();

        public static async Task<CapHost> StartAsync(
            string redisEndpoint,
            string postgresConnectionString,
            string schema,
            string topicPrefix,
            string capVersion,
            EffectStore effects)
        {
            var failureLog = new CapFailureLog();
            var settings = new Dictionary<string, string?>
            {
                ["Messaging:Provider"] = "Redis",
                // 超时预算写在**本用例自己的连接串**上，不碰任何共享配置：故障形态是「命令没有答复」，
                // 没有一个有界的 syncTimeout/asyncTimeout 就没有可判别的失败时刻。
                ["Messaging:Redis:ConnectionString"] =
                    $"{redisEndpoint},abortConnect=false,connectTimeout=2000,syncTimeout=2000,asyncTimeout=2000,connectRetry=1",
            };
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

            var services = new ServiceCollection();
            services.AddLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Warning);
                logging.AddProvider(failureLog);
            });
            services.AddSingleton(effects);
            services.AddScoped<MainSubscriber>();
            services.AddScoped<SideSubscriber>();
            services.AddCap(options =>
            {
                options.UseConfiguredTransport(configuration);
                options.UsePostgreSql(storage =>
                {
                    storage.ConnectionString = postgresConnectionString;
                    storage.Schema = schema;
                });
                options.Version = capVersion;
                options.TopicNamePrefix = topicPrefix;
                options.ConsumerThreadCount = 1;
                // CAP 自带的失败重投参数，调**小**是为了让「在途消息恢复后落地」在有界时间内可判；
                // ⛔ 这不是新增重试机制，也没有调大任何预算，两个值都只往小调。
                options.FailedRetryInterval = 1;
                // ⭐ 少了这一行，「在途消息恢复后落地」在 90 秒内**不可能**成立（本席位实测：两封在途消息
                // 停在 published/Failed/retries=3）。CAP 的回补查询只取 "Added" 早于
                // now - FallbackWindowLookbackSeconds 的失败消息，而该值**默认 240 秒**。
                // 取 30 是本仓 CapMessagingConfiguration.MinimumFallbackWindowLookbackSeconds 的下界，
                // 不是随手取的数。
                options.FallbackWindowLookbackSeconds = 30;
                options.SucceedMessageExpiredAfter = 3600;
                options.CollectorCleaningInterval = 3600;
            });

            // 观察者插在生产装饰器**内侧**（DecoratedConsumerClientFactory → 观察者 → 真 RedisConsumerClientFactory）。
            // ⇒ 它看到的是 inner 原样抛出的 Redis 异常，转换仍然只由生产代码做。
            var transportDescriptor = services.Last(
                descriptor => descriptor.ServiceType == typeof(TransportConsumerClientFactory));
            services.Remove(transportDescriptor);
            var observer = new ObservingConsumerClientFactory();
            services.AddSingleton(serviceProvider => new TransportConsumerClientFactory(
                observer.Wrap(((TransportConsumerClientFactory)transportDescriptor.ImplementationFactory!(serviceProvider)).Inner)));

            var provider = services.BuildServiceProvider();
            var bootstrapper = provider.GetServices<IHostedService>().OfType<BackgroundService>().Single();
            await bootstrapper.StartAsync(CancellationToken.None);
            // BackgroundService.StartAsync 只把 ExecuteAsync 起起来，不等 BootstrapAsync 跑完；
            // 用仓库既有的共享 helper 等它，避免撞上 CapTestHost 记录的那条上游释放期竞态。
            await CapTestHost.WaitForCapBootstrapAsync(provider);
            return new CapHost(provider, bootstrapper, observer, failureLog);
        }

        public async Task PublishAsync(string topic, string eventId)
        {
            using var scope = provider.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ICapPublisher>()
                .PublishAsync(topic, new RecoveryProbeMessage(eventId));
        }

        /// <summary>唯一的停止动作：CAP 的 <c>Bootstrapper</c> 就是 hosted service，宿主停机走的正是这里。</summary>
        public Task StopAsync() => bootstrapper.StopAsync(CancellationToken.None);

        public ValueTask DisposeAsync() => provider.DisposeAsync();
    }

    public sealed record RecoveryProbeMessage(string EventId);

    public sealed record RecordedEffect(string ConsumerGroup, string EventId);

    public sealed record ObservedFailure(string Group, string ExceptionType, string Stack);

    public sealed record LoggedBrokerFailure(string Message, string InnerExceptionType);

    private sealed class MainSubscriber(EffectStore effects) : ICapSubscribe
    {
        public const string Topic = "Issue3222RecoveryMain";

        [CapSubscribe(Topic, Group = RecoveryProbe.MainGroup)]
        public Task HandleAsync(RecoveryProbeMessage message) =>
            effects.RecordAsync(RecoveryProbe.MainGroup, message.EventId);
    }

    private sealed class SideSubscriber(EffectStore effects) : ICapSubscribe
    {
        public const string Topic = "Issue3222RecoverySide";

        [CapSubscribe(Topic, Group = RecoveryProbe.SideGroup)]
        public Task HandleAsync(RecoveryProbeMessage message) =>
            effects.RecordAsync(RecoveryProbe.SideGroup, message.EventId);
    }

    /// <summary>
    /// 真实 PostgreSQL 上的<b>持久</b>副作用。⚠️ 刻意<b>没有</b>唯一约束：去重若由表结构完成，
    /// 「重投不重复」就变成夹具自己保证的，断言等于同义反复。这里每次 handler 执行插一行，
    /// 由断言去数行数。
    /// </summary>
    private sealed class EffectStore(string connectionString, string schema)
    {
        public string Schema => schema;

        private string Table => $"\"{schema}\".\"recovery_effect\"";

        public async Task ResetAsync()
        {
            await ExecuteAsync($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE;");
            await ExecuteAsync($"CREATE SCHEMA \"{schema}\";");
            await ExecuteAsync(
                $"CREATE TABLE {Table} (ordinal bigserial PRIMARY KEY, consumer_group text NOT NULL, event_id text NOT NULL, observed_at timestamptz NOT NULL DEFAULT now());");
        }

        public Task DropAsync() => ExecuteAsync($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE;");

        public async Task RecordAsync(string consumerGroup, string eventId)
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                $"INSERT INTO {Table} (consumer_group, event_id) VALUES (@g, @e);", connection);
            command.Parameters.AddWithValue("g", consumerGroup);
            command.Parameters.AddWithValue("e", eventId);
            await command.ExecuteNonQueryAsync();
        }

        public async Task<IReadOnlyList<RecordedEffect>> ReadAsync()
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                $"SELECT consumer_group, event_id FROM {Table} ORDER BY ordinal;", connection);
            await using var reader = await command.ExecuteReaderAsync();
            var records = new List<RecordedEffect>();
            while (await reader.ReadAsync())
            {
                records.Add(new RecordedEffect(reader.GetString(0), reader.GetString(1)));
            }

            return records;
        }

        /// <summary>
        /// 失败现场：CAP 自己的 outbox/inbox 读数。没有它，「副作用表是空的」有三种截然不同的成因
        /// （没发出去 / 发出去没消费 / 消费了没落表），失败消息里长得一模一样。
        /// </summary>
        public async Task<string> DescribeCapStateAsync()
        {
            var lines = new List<string>();
            foreach (var table in new[] { "published", "received" })
            {
                try
                {
                    await using var connection = new NpgsqlConnection(connectionString);
                    await connection.OpenAsync();
                    await using var command = new NpgsqlCommand(
                        $"SELECT \"Name\", \"StatusName\", \"Retries\", count(*) FROM \"{schema}\".\"{table}\" GROUP BY 1,2,3 ORDER BY 1,2,3;",
                        connection);
                    await using var reader = await command.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        lines.Add($"{table}[{reader.GetString(0)}/{reader.GetString(1)}/retries={reader.GetInt32(2)}]={reader.GetInt64(3)}");
                    }
                }
                catch (Exception failure)
                {
                    lines.Add($"{table}[unreadable:{failure.GetType().Name}]");
                }
            }

            return lines.Count == 0 ? "<cap tables empty>" : string.Join(" ", lines);
        }

        private async Task ExecuteAsync(string sql)
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// 捕获 <c>ConsumerRegister</c> 记下的 <see cref="BrokerConnectionException"/>。这是与「健康位翻转」
    /// 完全独立的第二个取证入口：它能看见<b>到达 CAP 的那个异常对象本身</b>，从而证明 inner 保留。
    /// </summary>
    private sealed class CapFailureLog : ILoggerProvider
    {
        private readonly ConcurrentQueue<LoggedBrokerFailure> failures = new();

        public IReadOnlyList<LoggedBrokerFailure> Failures => failures.ToArray();

        public ILogger CreateLogger(string categoryName) => new Sink(failures);

        public void Dispose() => GC.SuppressFinalize(this);

        private sealed class Sink(ConcurrentQueue<LoggedBrokerFailure> failures) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (exception is BrokerConnectionException broker)
                {
                    failures.Enqueue(new LoggedBrokerFailure(
                        broker.Message,
                        broker.InnerException?.GetType().Name ?? "<none>"));
                }
            }
        }
    }

    /// <summary>
    /// 纯转发 + 记录。⛔ 它<b>不</b> catch-and-convert 任何东西：记录之后原样上抛，
    /// 转换仍然只发生在生产的 <see cref="DecoratedConsumerClient"/> 里。
    /// </summary>
    private sealed class ObservingConsumerClientFactory
    {
        private readonly List<ObservedConsumerClient> clients = [];
        private readonly Lock gate = new();

        public IReadOnlyList<ObservedConsumerClient> Clients
        {
            get { lock (gate) { return clients.ToArray(); } }
        }

        public IReadOnlyList<ObservedFailure> SubscribeFailures =>
            Clients.Where(client => client.SubscribeFailure is not null)
                .Select(client => client.SubscribeFailure!)
                .ToArray();

        public IReadOnlyList<string> SubscribeSuccesses =>
            Clients.Where(client => client.SubscribeSucceeded).Select(client => client.Group).ToArray();

        public IConsumerClientFactory Wrap(IConsumerClientFactory inner) => new Factory(this, inner);

        public string Dump() => string.Join(" | ", Clients.Select(client => client.ToString()));

        private ObservedConsumerClient Register(IConsumerClient inner, string group)
        {
            var client = new ObservedConsumerClient(inner, group);
            lock (gate)
            {
                client.Ordinal = clients.Count + 1;
                clients.Add(client);
            }

            return client;
        }

        private sealed class Factory(ObservingConsumerClientFactory owner, IConsumerClientFactory inner)
            : IConsumerClientFactory
        {
            public async Task<IConsumerClient> CreateAsync(string groupName, byte groupConcurrent) =>
                owner.Register(await inner.CreateAsync(groupName, groupConcurrent), groupName);
        }
    }

    private sealed class ObservedConsumerClient(IConsumerClient inner, string group) : IConsumerClient
    {
        private readonly TaskCompletionSource listeningExited =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Group { get; } = group;

        public int Ordinal { get; set; }

        public ObservedFailure? SubscribeFailure { get; private set; }

        public bool SubscribeSucceeded { get; private set; }

        public bool ListeningEntered { get; private set; }

        public bool ListeningExited => listeningExited.Task.IsCompleted;

        public bool Disposed { get; private set; }

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

        public async Task SubscribeAsync(IEnumerable<string> topics)
        {
            try
            {
                await inner.SubscribeAsync(topics).ConfigureAwait(false);
                SubscribeSucceeded = true;
            }
            catch (Exception failure)
            {
                SubscribeFailure = new ObservedFailure(
                    Group, failure.GetType().Name, failure.StackTrace ?? string.Empty);
                throw;
            }
        }

        public Task ListeningAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            ListeningEntered = true;
            try
            {
                return ObserveAsync(inner.ListeningAsync(timeout, cancellationToken));
            }
            catch (Exception)
            {
                // 真 RedisConsumerClient 的 ListeningAsync 不是 async，取消时**同步**抛。
                listeningExited.TrySetResult();
                throw;
            }
        }

        public Task CommitAsync(object? sender) => inner.CommitAsync(sender);

        public Task RejectAsync(object? sender) => inner.RejectAsync(sender);

        public async ValueTask DisposeAsync()
        {
            Disposed = true;
            await inner.DisposeAsync().ConfigureAwait(false);
        }

        public override string ToString() =>
            $"#{Ordinal}/{Group} subscribed={SubscribeSucceeded} failure={SubscribeFailure?.ExceptionType} "
            + $"entered={ListeningEntered} exited={ListeningExited} disposed={Disposed}";

        private async Task ObserveAsync(Task listening)
        {
            try
            {
                await listening.ConfigureAwait(false);
            }
            finally
            {
                listeningExited.TrySetResult();
            }
        }
    }

    /// <summary>
    /// 本用例自有的 loopback 转发器。⛔ 它只作用在经过它的那些 socket 上：共享 Redis 从不被暂停、
    /// 重启或改配置，lane 里其它成员与本用例的核查连接都直连真实端点。
    /// </summary>
    private sealed class RedisFaultProxy : IAsyncDisposable
    {
        public enum FaultKind
        {
            None,
            ResetOnConnect,
            BlackHoleAfterCommand,
        }

        /// <summary>
        /// 嗅这条命令：上游 <c>TryGetOrCreateStreamConsumerGroupAsync</c> 的<b>第一条</b>命令是
        /// <c>KeyExistsAsync</c>，<c>XGROUP</c> 在它之后。
        /// </summary>
        private static readonly byte[] BlackHoleTrigger = Encoding.ASCII.GetBytes("EXISTS");

        private readonly TcpListener listener;
        private readonly IPEndPoint target;
        private readonly CancellationTokenSource shutdown = new();
        private readonly ConcurrentDictionary<int, Socket> live = new();
        private Task acceptLoop = Task.CompletedTask;
        private volatile FaultKind fault;
        private int nextConnectionId;
        private int blackHoled;
        private int resets;

        private RedisFaultProxy(TcpListener listener, IPEndPoint target, FaultKind fault)
        {
            this.listener = listener;
            this.target = target;
            this.fault = fault;
        }

        public string Endpoint => $"127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";

        public int BlackHoledConnections => Volatile.Read(ref blackHoled);

        public int ResetConnections => Volatile.Read(ref resets);

        public static Task<RedisFaultProxy> StartAsync(string redisEndpoint, FaultKind fault) =>
            StartCoreAsync(redisEndpoint, fault);

        public void SetFault(FaultKind kind)
        {
            fault = kind;
            // 开故障时把现有连接一并断掉：否则多路复用器会继续用一条**已经建立好的**健康连接，
            // 故障就只对「之后新建的连接」生效，而 CAP 的连接池在重启时未必新建。
            CloseLiveConnections();
        }

        /// <summary>
        /// 清除故障。黑洞过的 socket 必须<b>关掉</b>而不是恢复转发：那条连接上已经有一条永远等不到答复的
        /// 命令，恢复转发只会让服务端收到一条迟到的 <c>EXISTS</c>，客户端那边早已放弃。关掉之后
        /// <c>StackExchange.Redis</c> 自己重连。
        /// </summary>
        public void ClearFault()
        {
            fault = FaultKind.None;
            CloseLiveConnections();
        }

        private static async Task<RedisFaultProxy> StartCoreAsync(string redisEndpoint, FaultKind fault)
        {
            var first = redisEndpoint.Split(',')[0];
            var separator = first.LastIndexOf(':');
            var host = first[..separator].Trim('[', ']');
            var port = int.Parse(first[(separator + 1)..]);
            var addresses = await Dns.GetHostAddressesAsync(host);
            var target = new IPEndPoint(addresses[0], port);
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var proxy = new RedisFaultProxy(listener, target, fault);
            proxy.acceptLoop = Task.Run(proxy.AcceptAsync);
            return proxy;
        }

        private void CloseLiveConnections()
        {
            foreach (var key in live.Keys)
            {
                if (live.TryRemove(key, out var socket)) Close(socket);
            }
        }

        private async Task AcceptAsync()
        {
            while (!shutdown.IsCancellationRequested)
            {
                Socket client;
                try
                {
                    client = await listener.AcceptSocketAsync(shutdown.Token);
                }
                catch (Exception)
                {
                    return;
                }

                _ = Task.Run(() => HandleAsync(client));
            }
        }

        private async Task HandleAsync(Socket client)
        {
            if (fault == FaultKind.ResetOnConnect)
            {
                Interlocked.Increment(ref resets);
                Close(client);
                return;
            }

            var id = Interlocked.Increment(ref nextConnectionId);
            Socket? server = null;
            var blackHole = new Flag();
            try
            {
                server = new Socket(target.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                await server.ConnectAsync(target, shutdown.Token);
                live[id] = client;
                live[-id] = server;
                await Task.WhenAny(
                    PumpAsync(client, server, blackHole, sniff: true),
                    PumpAsync(server, client, blackHole, sniff: false));
            }
            catch (Exception)
            {
                // 关闭/取消路径，读数由调用方的断言承担。
            }
            finally
            {
                live.TryRemove(id, out _);
                live.TryRemove(-id, out _);
                Close(client);
                if (server is not null) Close(server);
            }
        }

        private async Task PumpAsync(Socket from, Socket to, Flag blackHole, bool sniff)
        {
            var buffer = new byte[16 * 1024];
            while (!shutdown.IsCancellationRequested)
            {
                var read = await from.ReceiveAsync(buffer, SocketFlags.None, shutdown.Token);
                if (read == 0) return;

                if (sniff
                    && fault == FaultKind.BlackHoleAfterCommand
                    && !blackHole.Value
                    && buffer.AsSpan(0, read).IndexOf(BlackHoleTrigger) >= 0)
                {
                    blackHole.Value = true;
                    Interlocked.Increment(ref blackHoled);
                }

                if (blackHole.Value) continue;

                await to.SendAsync(buffer.AsMemory(0, read), SocketFlags.None, shutdown.Token);
            }
        }

        private static void Close(Socket socket)
        {
            try
            {
                // SO_LINGER(0)：连接以 RST 收场，对端得到「连接被重置」而不是一个正常的 FIN。
                socket.LingerState = new LingerOption(true, 0);
            }
            catch (Exception)
            {
                // 已释放的 socket 设置 LingerState 会抛；关闭动作本身仍要执行。
            }

            try
            {
                socket.Dispose();
            }
            catch (Exception)
            {
                // 同上。
            }
        }

        public async ValueTask DisposeAsync()
        {
            await shutdown.CancelAsync();
            listener.Stop();
            CloseLiveConnections();
            try
            {
                await acceptLoop.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception)
            {
                // 收尾路径。
            }

            shutdown.Dispose();
        }

        private sealed class Flag
        {
            public volatile bool Value;
        }
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class RedisSubscriptionRecoveryRedisCapFactAttribute : FactAttribute
{
    public RedisSubscriptionRecoveryRedisCapFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES"))
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_REDIS")))
            Skip = "Set NERV_IIP_TEST_POSTGRES and NERV_IIP_TEST_REDIS to run the real PostgreSQL + Redis CAP subscribe-recovery proof.";
    }
}
