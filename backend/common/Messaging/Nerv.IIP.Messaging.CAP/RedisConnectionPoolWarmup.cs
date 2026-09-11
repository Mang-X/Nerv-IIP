using System.Reflection;
using DotNetCore.CAP;
using DotNetCore.CAP.Messages;
using DotNetCore.CAP.RedisStreams;
using DotNetCore.CAP.Transport;
using Microsoft.Extensions.DependencyInjection;

namespace Nerv.IIP.Messaging.CAP;

/// <summary>
/// #3365（#3236 根因干预）：在 CAP 的 Redis 连接池<b>被任何人使用之前</b>把它的全部槽位预热完成，构造性关死
/// 「在途槽位被同步解引用」窗口。
///
/// <para><b>窗口的成因</b>（对 <c>DotNetCore.CAP.RedisStreams</c> 10.0.1 反编译实读）：
/// <c>AsyncLazyRedisConnection</c> 继承 <c>Lazy&lt;Task&lt;RedisConnection&gt;&gt;</c>，而
/// <c>CreatedConnection</c> 是 <c>IsValueCreated ? Value.GetAwaiter().GetResult() : null</c>——<b>同步阻塞属性</b>。
/// <c>RedisConnectionPool.ConnectAsync()</c> 的 foreach 对每个 <c>IsValueCreated</c> 的槽位读
/// <c>CreatedConnection.ConnectionCapacity</c>；只要那个槽位的 Task 还在途，读它就把一条线程池线程同步钉住。
/// 这不是启动期一次性现象：<c>Poll</c>/<c>Ack</c>/<c>PublishAsync</c> 每一轮都经 <c>RedisStreamManager</c>
/// 调 <c>ConnectAsync()</c>。</para>
///
/// <para><b>为什么预热是构造性的、不是白名单</b>：<c>_connections</c> 在池构造期（<c>Init()</c>）一次性放进
/// N 个槽位、<b>此后永不新增</b>，<c>Lazy</c> <b>永不重置</b>。所以 N 个槽位<b>全部 completed</b> 之后，
/// 「在途」状态在构造上不可能再出现，<c>CreatedConnection</c> 的每一次解引用都立即返回，窗口永久关闭。
/// <b>池大小一动不动</b>。</para>
///
/// <para><b>为什么不走 <c>pool.ConnectAsync()</c> 预热</b>，以及<b>为什么「并发上来池自然会建满」是错的</b>
/// ——这两件事是<b>同一段上游代码决定的</b>，不是观察到的巧合。<c>RedisConnectionPool.ConnectAsync()</c>
/// 的主循环（10.0.1 反编译逐字）：
/// <code>
/// foreach (var connection in _connections)
/// {
///     if (!connection.IsValueCreated) return (await connection).Connection;   // 建第 1 个就 return
///     if (connection.CreatedConnection.ConnectionCapacity == 0L)              // ← 同步阻塞点
///         return connection.CreatedConnection.Connection;                     // 容量没顶上去就一直复用它
/// }
/// </code>
/// ⇒ ① 拿它当预热用，<b>一次只会建 1 个槽位</b>，而且它自己就解引用 <c>CreatedConnection</c>（预热自身开窗口）；
/// ② <b>并发再高也不会把池填满</b>：首个槽位建成后，只要 <c>ConnectionCapacity == 0</c> 就一直被复用，
/// 后续槽位要等负载把容量顶上去才逐个增长——这正是 #3236 链条上观察到的
/// <c>6→8→13→16→18→20→22</c> 逐级爬升（历时数十秒），以及「40 路并发下不预热时池只建了 1/10 个槽位」的成因。
/// 预热改为<b>直接 await 每个 <c>AsyncLazyRedisConnection</c></b>（其 <c>GetAwaiter()</c> 是 public），
/// 全程不碰 <c>CreatedConnection</c>。</para>
///
/// <para><b>挂载点是两个我们已经拥有的 public seam，都是 <c>async</c></b>（不引入 sync-over-async）：
/// <see cref="DecoratedConsumerClientFactory.CreateAsync"/> 与 <see cref="WarmedTransport.SendAsync"/>。
/// 触到池的流量入口只有这两条——见 <see cref="RedisConnectionPoolWarmupExtension"/> 的闭集说明。</para>
///
/// <para><b>挂在「第一次使用」而不是 host 启动</b>：预热把连接建立成本从「按需付」挪到「第一次使用时一次付」，
/// 并行度 ×N。Redis 未就绪时 <c>AbortOnConnectFail=false</c>（<c>CapMessagingConfiguration</c> 显式设的）
/// 下会有一次性停顿；挂在第一次 <c>CreateAsync</c>/<c>SendAsync</c> 可以避开 Aspire 里 Redis 晚于服务就绪的窗口。</para>
///
/// <para><b>范围收窄声明</b>：本垫片只关「在途槽位被同步解引用」这一个窗口。上游 #1808 的另两条修复
/// （faulted 槽位永久中毒、失败尝试泄漏 multiplexer）<b>不在覆盖范围内</b>——这是范围收窄，不是等价。</para>
/// </summary>
internal sealed class RedisConnectionPoolWarmup
{
    private readonly Lazy<Task> warmup;

    /// <summary>DI 走这个构造函数：<c>ActivatorUtilities</c> 只看 public 构造函数，所以不会误选下面那个。</summary>
    public RedisConnectionPoolWarmup(IServiceProvider provider, RedisConnectionPoolSlots slots)
        : this(() => WarmEverySlotAsync(provider, slots))
    {
    }

    /// <summary>让挂载点的用例可以用一个完全受控的 core 验「是否真的 await 了预热」，不必牵扯真实 Redis。</summary>
    internal RedisConnectionPoolWarmup(Func<Task> core) =>
        warmup = new Lazy<Task>(core, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// 幂等：无论被哪个挂载点调用多少次，池只预热一次，所有调用方 await 的是同一个 Task。
    /// </summary>
    public Task WarmAsync() => warmup.Value;

    private static async Task WarmEverySlotAsync(IServiceProvider provider, RedisConnectionPoolSlots slots)
    {
        var pool = provider.GetRequiredService(slots.PoolServiceType);

        // 直接 await 每个槽位；不经 pool.ConnectAsync()，因此全程不解引用 CreatedConnection。
        // Task.WhenAll 的实参在 await 之前求值 ⇒ 枚举时就把 N 个槽位的 Lazy 全部触发（IsValueCreated 立刻为 true），
        // N 条连接并行建立，而不是串行。
        await Task.WhenAll(slots.Read(pool).Select(async slot => await slot)).ConfigureAwait(false);
    }
}

/// <summary>
/// 反射垫片的<b>唯一一处锚点</b>，且<b>按类型锚、不按名字锚</b>：在池的实现类型上找「字段类型可赋值给
/// <see cref="IEnumerable{T}"/> of <see cref="AsyncLazyRedisConnection"/>」的那个字段（元素类型是 public）。
/// 上游 #1808 落地后该字段仍是 <c>ConcurrentBag&lt;AsyncLazyRedisConnection&gt;</c> ⇒ 向前兼容。
///
/// <para>池的服务类型本身也是按这个字段反查出来的，因此本类型<b>不出现任何上游字段名或接口名字面量</b>
/// （<c>IRedisConnectionPool</c> 是 internal，写成字符串反而会在改名时静默失配）。</para>
/// </summary>
internal sealed class RedisConnectionPoolSlots(Type poolServiceType, FieldInfo slotsField)
{
    public Type PoolServiceType { get; } = poolServiceType;

    internal FieldInfo SlotsField { get; } = slotsField;

    public IEnumerable<AsyncLazyRedisConnection> Read(object pool) =>
        (IEnumerable<AsyncLazyRedisConnection>)SlotsField.GetValue(pool)!;

    /// <summary>
    /// 组装期解析，fail closed。找不到池描述符 / 找不到字段 / 元素类型不符 ⇒ 抛异常，每个宿主启动即失败。
    /// 记日志等于静默（CI 不读警告），静默的后果是预热不生效而所有门禁照绿。
    /// </summary>
    public static RedisConnectionPoolSlots Resolve(IServiceCollection services)
    {
        var candidates = services
            .Select(descriptor => (descriptor, field: SlotsFieldOf(descriptor.ImplementationType)))
            .Where(candidate => candidate.field is not null)
            .ToArray();

        if (candidates.Length != 1)
        {
            throw new InvalidOperationException(
                $"Expected exactly one registered service whose implementation type holds an "
                + $"{nameof(IEnumerable<AsyncLazyRedisConnection>)} of {nameof(AsyncLazyRedisConnection)} "
                + $"(the CAP Redis connection pool), but found {candidates.Length}. "
                + $"The transport package shape changed; revisit {nameof(RedisConnectionPoolWarmup)} (#3365).");
        }

        var (poolDescriptor, slotsField) = candidates[0];
        return new RedisConnectionPoolSlots(poolDescriptor.ServiceType, slotsField!);
    }

    private static FieldInfo? SlotsFieldOf(Type? implementationType)
    {
        if (implementationType is null)
        {
            return null;
        }

        var fields = implementationType
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(field => typeof(IEnumerable<AsyncLazyRedisConnection>).IsAssignableFrom(field.FieldType))
            .ToArray();

        // 出现第二个同型字段时不猜：交给 Resolve 的计数分支 fail closed。
        return fields.Length == 1 ? fields[0] : null;
    }
}

/// <summary>
/// 把预热挂到发布侧。<see cref="ITransport.SendAsync"/> 是触到连接池的两条流量入口之一
/// （另一条是 <see cref="IConsumerClientFactory"/> 造出的 <see cref="IConsumerClient"/>）。
///
/// <para><b>只挂消费侧是不够的</b>：<c>RedisTransport.SendAsync</c> → <c>IRedisStreamManager.PublishAsync</c>
/// → <c>RedisStreamManager.ConnectAsync</c> → 池，这条路完全不经过 <see cref="IConsumerClientFactory"/>。
/// 纯发布型宿主（不订阅任何 topic）连 <c>CreateAsync</c> 都不会被调用一次。</para>
/// </summary>
internal sealed class WarmedTransport(WarmedTransportInner transport, RedisConnectionPoolWarmup warmup) : ITransport
{
    internal ITransport Inner => transport.Inner;

    public BrokerAddress BrokerAddress => transport.Inner.BrokerAddress;

    public async Task<OperateResult> SendAsync(TransportMessage message)
    {
        await warmup.WarmAsync().ConfigureAwait(false);
        return await transport.Inner.SendAsync(message).ConfigureAwait(false);
    }
}

/// <summary>Holds the transport's own <see cref="ITransport"/> so the decorator can take it as a constructor dependency.</summary>
internal sealed class WarmedTransportInner(ITransport inner)
{
    public ITransport Inner { get; } = inner;
}

/// <summary>
/// 组装：解析池槽位（fail closed）、登记预热组件、并把 <see cref="ITransport"/> 换成
/// <see cref="WarmedTransport"/>。消费侧的挂载点在 <see cref="DecoratedConsumerClientFactory"/>，
/// 由 <see cref="ConsumerClientDecorationExtension"/> 登记。
///
/// <para><b>登记必须保留 <c>ImplementationType</c> 非 null</b>（照 <see cref="ConsumerClientDecorationExtension"/>
/// 成例用 <c>LastOrDefault</c> + <c>Remove</c> + <c>AddSingleton&lt;TService, TDecorator&gt;</c>，不用工厂委托）：
/// 仓库自有测试设施按 <c>descriptor.ImplementationType</c> 经 <c>ActivatorUtilities.CreateInstance</c> 重建描述符，
/// 注册成工厂委托会让那套设施拿到 null。</para>
///
/// <para><b>必须在 <c>UseRedis</c> 之后 <c>RegisterExtension</c></b>（不是 <c>AddCap</c> 之后——那时扩展已经跑完了）：
/// <c>AddCap</c> 按登记顺序执行 <see cref="ICapOptionsExtension.AddServices"/>，本扩展要捕获 Redis 扩展登记的
/// <see cref="ITransport"/> 与连接池描述符。</para>
/// </summary>
internal sealed class RedisConnectionPoolWarmupExtension : ICapOptionsExtension
{
    public void AddServices(IServiceCollection services)
    {
        services.AddSingleton(RedisConnectionPoolSlots.Resolve(services));
        services.AddSingleton<RedisConnectionPoolWarmup>();

        var transportDescriptor = services.LastOrDefault(
            descriptor => descriptor.ServiceType == typeof(ITransport));
        if (transportDescriptor?.ImplementationType is null)
        {
            // Fail closed。静默跳过会让发布侧不预热而所有门禁照绿——这正是 M3 那格要拦的返工模式。
            throw new InvalidOperationException(
                $"The CAP transport did not register an {nameof(ITransport)} with a concrete implementation type, "
                + $"so {nameof(RedisConnectionPoolWarmupExtension)} cannot wrap it. "
                + "Registration order or the transport package shape changed; revisit the warmup seam (#3365).");
        }

        services.Remove(transportDescriptor);
        services.AddSingleton(provider => new WarmedTransportInner(
            (ITransport)ActivatorUtilities.CreateInstance(provider, transportDescriptor.ImplementationType)));
        services.AddSingleton<ITransport, WarmedTransport>();
    }
}
