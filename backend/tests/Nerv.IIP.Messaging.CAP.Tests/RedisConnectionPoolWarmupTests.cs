using DotNetCore.CAP;
using DotNetCore.CAP.Messages;
using DotNetCore.CAP.Transport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Nerv.IIP.Messaging.CAP.Tests;

/// <summary>
/// #3365：连接池预热的行为与装配。用例分三层——
/// <list type="number">
/// <item>预热<b>触发了哪些槽位</b>（对着真实 <c>RedisConnectionPool</c> 跑，不需要真实 Redis）；</item>
/// <item>两个挂载点<b>是否真的 await 了预热</b>（用受控 core，不牵扯 Redis 也不牵扯时序）；</item>
/// <item>组装形状与 fail closed。</item>
/// </list>
///
/// <para><b>为什么不需要真实 Redis</b>：本文件断言的是「槽位是否被触发 / 是否被等待」，
/// 这两件事都由 <c>Lazy</c> 的 <c>IsValueCreated</c> 与 Task 完成状态决定，和连接能不能建成无关。
/// 「在真实 CAP 宿主里第一次使用时池已 N/N 建满」那条是另一层证据，住在
/// <c>Nerv.IIP.Business.Mes.Web.Tests.RedisConnectionPoolWarmupRealHostTests</c>（redis-cap lane）。</para>
/// </summary>
public sealed class RedisConnectionPoolWarmupTests
{
    /// <summary>
    /// ⭐ 预热必须触发<b>每一个</b>槽位，并且在预热 Task 完成时<b>每一个</b>槽位的 Task 都已完成。
    ///
    /// <para>前半句拦住两种已经发生过的返工：<b>只热一个槽位</b>，以及<b>改走 <c>pool.ConnectAsync()</c></b>
    /// ——后者的 foreach 命中第一个 <c>!IsValueCreated</c> 的槽位就 return，退化成只热 1 个。
    /// 断言发生在 <c>WarmAsync()</c> <b>返回的那一刻</b>、<b>不等待</b>：<c>Task.WhenAll</c> 的实参在第一个
    /// await 之前求值，枚举时就把 N 个 <c>Lazy</c> 全部触发，所以这里没有任何时序竞争。</para>
    ///
    /// <para>后半句是「在途状态在构造上不可能再出现」的后置条件：槽位集合永不新增、<c>Lazy</c> 永不重置，
    /// 所以 N 个全部完成之后窗口永久关闭。</para>
    ///
    /// <para>⚠️ 两个时刻合在一条用例里是<b>为了只付一次等待成本</b>：端点不可连通时上游固定重试 5 轮、
    /// 轮间 <c>Task.Delay(2s)</c>，这 10 秒是上游硬编码的下界。拆成两条用例会把它付两遍。</para>
    /// </summary>
    [Fact]
    public async Task Warmup_triggers_every_slot_and_completes_every_one_of_them()
    {
        using var provider = CapRedisStreamsShapeContractTests.BuildRedisProvider();
        var slots = provider.GetRequiredService<RedisConnectionPoolSlots>();
        var connections = slots.Read(provider.GetRequiredService(slots.PoolServiceType)).ToArray();
        Assert.All(connections, connection => Assert.False(connection.IsValueCreated));

        var warming = provider.GetRequiredService<RedisConnectionPoolWarmup>().WarmAsync();

        // 不等待：这一刻起全部 N 个槽位就必须已经被触发。只热 1 个 / 改走 pool.ConnectAsync() 都在这里红。
        Assert.Equal((int)CapRedisStreamsShapeContractTests.DefaultConnectionPoolSize, connections.Length);
        Assert.All(connections, connection => Assert.True(connection.IsValueCreated));

        await warming;

        Assert.All(connections, connection => Assert.True(connection.Value.IsCompletedSuccessfully));
    }

    /// <summary>
    /// ⭐ 验收 A（机制断言）：<b>直接把缺陷本身量出来</b>——有槽位在途时，
    /// <c>RedisConnectionPool.ConnectAsync()</c> 的<b>同步段</b>会把调用线程钉住；
    /// 槽位全部完成之后，同一个调用的同步段 ≈ 0。
    ///
    /// <para><b>阻塞发生在哪一行</b>（对 10.0.1 反编译实读）：全部槽位 <c>IsValueCreated</c> 之后
    /// <c>_poolAlreadyConfigured</c> 变真，<c>QuietConnection</c> 于是走
    /// <c>OrderBy(c =&gt; c.CreatedConnection?.ConnectionCapacity ...)</c>，对<b>每个</b>槽位解引用
    /// <c>CreatedConnection</c>，而它是 <c>Value.GetAwaiter().GetResult()</c>。
    /// 所以这条与 <c>ConcurrentBag</c> 的枚举顺序无关，是构造性的。</para>
    ///
    /// <para>⚠️ <b>这条用例对本仓实现没有鉴别力</b>，它一格变异都杀不掉——它断言的是<b>上游前提</b>
    /// （缺陷存在、且「全部完成」确实能关掉窗口）。它和形状契约测试一起构成退役判据的另一半：
    /// 上游哪天把阻塞形态改掉，这条会红，届时该退役的是<b>整层垫片</b>，不是这条断言。</para>
    ///
    /// <para>阈值说明：不可连通端点下，上游 <c>ConnectAsync</c> 固定重试 5 轮、轮间 <c>Task.Delay(2s)</c>
    /// ⇒ 在途时长有 <b>10 秒级的硬下界</b>，与 1000 ms 的判据之间有一个数量级的余量，
    /// 不是一个贴边的时序阈值。</para>
    /// </summary>
    [Fact]
    public async Task Connect_blocks_the_caller_while_a_slot_is_in_flight_and_stops_blocking_once_every_slot_completed()
    {
        using var provider = CapRedisStreamsShapeContractTests.BuildRedisProvider();
        var slots = provider.GetRequiredService<RedisConnectionPoolSlots>();
        var pool = provider.GetRequiredService(slots.PoolServiceType);
        var connections = slots.Read(pool).ToArray();
        var connectAsync = slots.PoolServiceType.GetMethod("ConnectAsync")!;

        // 制造在途槽位：触发全部 Lazy 但不等待它们完成。
        foreach (var connection in connections)
        {
            _ = connection.Value;
        }

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var inFlightCall = (Task)connectAsync.Invoke(pool, null)!;
        var blockedMilliseconds = clock.ElapsedMilliseconds;

        // 走到「全部完成」——这正是预热结束后池所处的状态。
        await Task.WhenAll(connections.Select(async connection => await connection));
        await inFlightCall;

        clock.Restart();
        _ = (Task)connectAsync.Invoke(pool, null)!;
        var warmMilliseconds = clock.ElapsedMilliseconds;

        Assert.True(
            blockedMilliseconds >= 1000,
            $"在途槽位应当把 ConnectAsync 的同步段钉住，实测同步段 {blockedMilliseconds} ms。");
        Assert.True(
            warmMilliseconds < 100,
            $"槽位全部完成后同步段应当 ≈0，实测 {warmMilliseconds} ms（在途窗口没有被关掉）。");
    }

    /// <summary>两个挂载点都会调 <c>WarmAsync()</c>；池只能预热一次，否则第二个挂载点会重建 N 条连接。</summary>
    [Fact]
    public void Warmup_runs_once_and_every_caller_awaits_the_same_task()
    {
        var invocations = 0;
        var warmup = new RedisConnectionPoolWarmup(() =>
        {
            Interlocked.Increment(ref invocations);
            return Task.CompletedTask;
        });

        var first = warmup.WarmAsync();
        var second = warmup.WarmAsync();

        Assert.Same(first, second);
        Assert.Equal(1, invocations);
    }

    /// <summary>
    /// ⭐ 发布侧挂载点：<see cref="ITransport.SendAsync"/> 必须 <b>await</b> 预热，而且必须在碰 inner <b>之前</b>。
    ///
    /// <para>这条拦住两种返工：<b>漏发布侧</b>（纯发布型宿主一次 <c>CreateAsync</c> 都不会调，
    /// 只挂消费侧等于完全没挂）与 <b>fire-and-forget</b>（<c>_ = WarmAsync()</c>：池还没热就把消息发出去，
    /// 窗口原样存在）。预热未完成时 <c>SendAsync</c> 返回的 Task 必须仍未完成——受控 core，无时序竞争。</para>
    /// </summary>
    [Fact]
    public async Task Transport_awaits_the_warmup_before_it_touches_the_inner_transport()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inner = new RecordingTransport();
        var transport = new WarmedTransport(new WarmedTransportInner(inner), new RedisConnectionPoolWarmup(() => gate.Task));

        var sending = transport.SendAsync(NewMessage());

        Assert.False(sending.IsCompleted);
        Assert.Empty(inner.Sent);

        gate.SetResult();
        var result = await sending;

        Assert.True(result.Succeeded);
        Assert.Equal(["message-3365"], inner.Sent);
    }

    /// <summary>消费侧挂载点：<see cref="IConsumerClientFactory.CreateAsync"/> 同样必须先 await 预热再造 client。</summary>
    [Fact]
    public async Task ConsumerClientFactory_awaits_the_warmup_before_it_creates_a_client()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inner = new RecordingConsumerClientFactory();
        var factory = new DecoratedConsumerClientFactory(
            new TransportConsumerClientFactory(inner),
            new RedisConnectionPoolWarmup(() => gate.Task));

        var creating = factory.CreateAsync("group-3365", 1);

        Assert.False(creating.IsCompleted);
        Assert.Empty(inner.Created);

        gate.SetResult();
        var client = await creating;

        Assert.IsType<DecoratedConsumerClient>(client);
        Assert.Equal(["group-3365"], inner.Created);
    }

    /// <summary>
    /// 装配：<see cref="ITransport"/> 的唯一描述符被换成 <see cref="WarmedTransport"/>，
    /// 且 <c>ImplementationType</c> 保持非 null——仓库自有测试设施按它经 <c>ActivatorUtilities</c> 重建描述符，
    /// 注册成工厂委托会让那套设施拿到 null。
    /// </summary>
    [Fact]
    public void Redis_transport_descriptor_is_replaced_by_the_warmed_decorator_and_stays_rebuildable()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Messaging:Provider"] = "Redis",
            ["Messaging:Redis:ConnectionString"] = "127.0.0.1:16390,abortConnect=false",
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCap(options => options.UseConfiguredTransport(configuration));

        var descriptor = Assert.Single(services, x => x.ServiceType == typeof(ITransport));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.Equal(typeof(WarmedTransport), descriptor.ImplementationType);

        services.Remove(descriptor);
        using var provider = services.BuildServiceProvider();
        var rebuilt = (ITransport)ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!);

        var warmed = Assert.IsType<WarmedTransport>(rebuilt);
        Assert.Equal("DotNetCore.CAP.RedisStreams.RedisTransport", warmed.Inner.GetType().FullName);
    }

    [Theory]
    [InlineData("InMemory")]
    [InlineData("RabbitMQ")]
    public void Non_redis_transports_get_neither_the_warmup_nor_the_transport_decorator(string provider)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Messaging:Provider"] = provider,
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["Messaging:RabbitMQ:ConnectionString"] = "amqp://guest:guest@localhost:5672/",
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCap(options => options.UseConfiguredTransport(configuration));

        Assert.NotEqual(typeof(WarmedTransport), services.Single(x => x.ServiceType == typeof(ITransport)).ImplementationType);
        Assert.DoesNotContain(services, x => x.ServiceType == typeof(RedisConnectionPoolWarmup));
        Assert.DoesNotContain(services, x => x.ServiceType == typeof(RedisConnectionPoolSlots));
    }

    /// <summary>
    /// fail closed ①：找不到「持有 <c>IEnumerable&lt;AsyncLazyRedisConnection&gt;</c> 字段」的登记 ⇒ 抛异常。
    /// 记日志等于静默：静默的后果是预热不生效而所有门禁照绿。
    /// </summary>
    [Fact]
    public void Slot_resolution_fails_closed_when_no_registration_holds_the_pool_slots()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITransport, RecordingTransport>();

        var exception = Assert.Throws<InvalidOperationException>(() => RedisConnectionPoolSlots.Resolve(services));

        Assert.Contains(nameof(RedisConnectionPoolWarmup), exception.Message, StringComparison.Ordinal);
    }

    /// <summary>fail closed ②：<see cref="ITransport"/> 描述符不可按 <c>ImplementationType</c> 重建 ⇒ 抛异常。</summary>
    [Fact]
    public void Transport_decoration_fails_closed_when_the_transport_descriptor_cannot_be_rebuilt()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCap(options => options.UseRedis(redis => redis.Configuration =
            StackExchange.Redis.ConfigurationOptions.Parse("127.0.0.1:16390,abortConnect=false")));
        services.Remove(services.Single(x => x.ServiceType == typeof(ITransport)));
        services.AddSingleton<ITransport>(_ => new RecordingTransport());

        var exception = Assert.Throws<InvalidOperationException>(
            () => new RedisConnectionPoolWarmupExtension().AddServices(services));

        Assert.Contains(nameof(ITransport), exception.Message, StringComparison.Ordinal);
    }

    private static TransportMessage NewMessage() =>
        new(new Dictionary<string, string?> { [Headers.MessageId] = "message-3365", [Headers.MessageName] = "message-3365" }, ReadOnlyMemory<byte>.Empty);

    private sealed class RecordingTransport : ITransport
    {
        public List<string> Sent { get; } = [];

        public BrokerAddress BrokerAddress => new("recording", "endpoint-3365");

        public Task<OperateResult> SendAsync(TransportMessage message)
        {
            Sent.Add(message.GetName());
            return Task.FromResult(OperateResult.Success);
        }
    }

    private sealed class RecordingConsumerClientFactory : IConsumerClientFactory
    {
        public List<string> Created { get; } = [];

        public Task<IConsumerClient> CreateAsync(string groupName, byte groupConcurrent)
        {
            Created.Add(groupName);
            return Task.FromResult<IConsumerClient>(new StubConsumerClient());
        }
    }

    private sealed class StubConsumerClient : IConsumerClient
    {
        public BrokerAddress BrokerAddress => new("recording", "endpoint-3365");
        public Func<TransportMessage, object?, Task>? OnMessageCallback { get; set; }
        public Action<LogMessageEventArgs>? OnLogCallback { get; set; }
        public Task<ICollection<string>> FetchTopicsAsync(IEnumerable<string> topicNames) =>
            Task.FromResult<ICollection<string>>(topicNames.ToArray());
        public Task SubscribeAsync(IEnumerable<string> topics) => Task.CompletedTask;
        public Task ListeningAsync(TimeSpan timeout, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CommitAsync(object? sender) => Task.CompletedTask;
        public Task RejectAsync(object? sender) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
