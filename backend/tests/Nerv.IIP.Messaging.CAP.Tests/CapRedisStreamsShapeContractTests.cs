using System.Reflection;
using DotNetCore.CAP;
using DotNetCore.CAP.RedisStreams;
using DotNetCore.CAP.Transport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Xunit;

namespace Nerv.IIP.Messaging.CAP.Tests;

/// <summary>
/// #3365：连接池预热垫片（<see cref="RedisConnectionPoolWarmup"/>）依赖 <c>DotNetCore.CAP.RedisStreams</c>
/// 的三条<b>上游形状</b>。上游形状变了而垫片还在，是「断言还在跑还在绿、但它要证的事已不存在」那类静默失效，
/// 所以这里把依赖写成断言而不是注释。
///
/// <para>版本钉在 <c>backend/Directory.Packages.props</c> 的集中版本上 ⇒ 升级 CAP 必走 PR，本文件必在那个 PR 上红。
/// <b>不给 CAP 加版本上界</b>（会挡安全更新），靠这里报红。</para>
///
/// <para><b>覆盖边界（不自称完备）</b>：本文件断言的是垫片<b>实际依赖的那几条</b>形状，不是 RedisStreams 的
/// 全部公开面。特别地，<see cref="AsyncLazyRedisConnection.CreatedConnection"/> 的「阻塞」只由
/// <see cref="ReferencesTaskAwaiterGetResult"/> 的<b>存在性</b>扫描证明（扫到即成立），它<b>不能</b>用来证明
/// 「不阻塞」；「不阻塞」那一面由 <see cref="AsyncLazyRedisConnection_still_derives_from_a_blocking_lazy_task"/>
/// 的基类断言承担。</para>
/// </summary>
public sealed class CapRedisStreamsShapeContractTests
{
    /// <summary>形状 ①：两个挂载点的服务都以 <see cref="ServiceLifetime.Singleton"/> 登记，且后注册者赢。</summary>
    [Fact]
    public void Redis_extension_registers_both_mount_points_as_singletons_and_the_later_registration_wins()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCap(options => options.UseRedis(redis => redis.Configuration =
            ConfigurationOptions.Parse("localhost:6379,abortConnect=false")));

        foreach (var serviceType in new[] { typeof(ITransport), typeof(IConsumerClientFactory) })
        {
            var descriptor = Assert.Single(services, x => x.ServiceType == serviceType);
            Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
            Assert.NotNull(descriptor.ImplementationType);
        }

        // decoy 探针：后注册者赢——这正是 LastOrDefault + Remove + 重新 AddSingleton 那套装饰配方成立的前提。
        services.AddSingleton<ITransport, DecoyTransport>();
        using var provider = services.BuildServiceProvider();
        Assert.IsType<DecoyTransport>(provider.GetRequiredService<ITransport>());
    }

    /// <summary>
    /// 形状 ②：池持有一个可赋值给 <c>IEnumerable&lt;AsyncLazyRedisConnection&gt;</c> 的字段，<b>且构造期就把 N 个
    /// 槽位放满、但一条连接都不建</b>。后半句是预热之所以必要的全部理由——如果上游改成构造期就建连接，预热就该退役。
    /// </summary>
    [Fact]
    public void Connection_pool_fills_every_slot_at_construction_but_creates_no_connection()
    {
        using var provider = BuildRedisProvider();
        var slots = provider.GetRequiredService<RedisConnectionPoolSlots>();
        var pool = provider.GetRequiredService(slots.PoolServiceType);

        var connections = slots.Read(pool).ToArray();

        Assert.Equal((int)DefaultConnectionPoolSize, connections.Length);
        Assert.All(connections, connection => Assert.False(connection.IsValueCreated));
    }

    /// <summary>
    /// 形状 ③ + ⭐<b>退役判据</b>：<see cref="AsyncLazyRedisConnection"/> 仍然继承
    /// <c>Lazy&lt;Task&lt;RedisConnection&gt;&gt;</c>，<c>GetAwaiter()</c> 仍是 public，且<b>没有</b>
    /// <c>TryGetCreatedConnection</c> 这类非阻塞取值入口。
    ///
    /// <para>⭐ <b>这条红了意味着什么</b>：上游 <c>dotnetcore/CAP#1808</c>（或等价改造）已落地，
    /// 「在途槽位被同步解引用」的窗口由上游自己关掉了 ⇒ <b>本仓的预热垫片应当退役</b>
    /// （删掉 <see cref="RedisConnectionPoolWarmup"/>、两个挂载点与那一处反射），而不是把这条断言改绿。
    /// 封闭豁免集拦得住绿化但<b>不会自动退役</b>，所以退役条件必须是断言，不能是注释。</para>
    /// </summary>
    [Fact]
    public void AsyncLazyRedisConnection_still_derives_from_a_blocking_lazy_task()
    {
        Assert.Equal(typeof(Lazy<Task<RedisConnection>>), typeof(AsyncLazyRedisConnection).BaseType);

        var getAwaiter = typeof(AsyncLazyRedisConnection).GetMethod(
            nameof(AsyncLazyRedisConnection.GetAwaiter), BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(getAwaiter);
        Assert.Equal(typeof(System.Runtime.CompilerServices.TaskAwaiter<RedisConnection>), getAwaiter.ReturnType);

        var createdConnection = typeof(AsyncLazyRedisConnection).GetProperty(
            nameof(AsyncLazyRedisConnection.CreatedConnection), BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(createdConnection);
        // 同步返回 RedisConnection（而不是 Task<RedisConnection>）的属性，架在 Lazy<Task<RedisConnection>> 之上，
        // 取值只能靠阻塞——这就是垫片要绕开的那个形状。
        Assert.Equal(typeof(RedisConnection), createdConnection.PropertyType);
        Assert.True(
            ReferencesTaskAwaiterGetResult(createdConnection.GetMethod!),
            "CreatedConnection 不再同步 GetResult ⇒ 上游已改掉阻塞形态，预热垫片应退役。");

        // #1808 落地后上游会给出非阻塞取值入口；它一旦出现，本垫片的豁免即到期。
        Assert.DoesNotContain(
            typeof(AsyncLazyRedisConnection).GetMembers(BindingFlags.Public | BindingFlags.Instance),
            member => member.Name.StartsWith("TryGet", StringComparison.Ordinal));
    }

    internal const uint DefaultConnectionPoolSize = 10;

    internal static ServiceProvider BuildRedisProvider(string? connectionString = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Messaging:Provider"] = "Redis",
            // 指向一个没人监听的回环端口：本文件与预热用例都不需要真实 Redis——池在构造期只放槽位、不建连接，
            // 而预热用例断言的是「槽位是否被触发」，与连接能否建成无关。
            //
            // connectTimeout/connectRetry 收到最小值只是为了让「连不上」这件事快速定论：上游
            // AsyncLazyRedisConnection.ConnectAsync 在未连通时固定重试 5 轮、每轮之间 Task.Delay(2s)，
            // 这 10 秒是上游硬编码的下界，调不掉；不收这两个值的话每轮还要再额外付一次 connectTimeout。
            ["Messaging:Redis:ConnectionString"] =
                connectionString ?? "127.0.0.1:16390,abortConnect=false,connectTimeout=50,connectRetry=1,syncTimeout=50",
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCap(options => options.UseConfiguredTransport(configuration));
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// 在方法体 IL 里找对 <c>TaskAwaiter&lt;RedisConnection&gt;.GetResult</c> 的引用。
    /// <b>这是存在性扫描</b>：逐字节试解 <c>call</c>/<c>callvirt</c> 的 token，真实指令偏移必然在被试集合内，
    /// 所以「扫到」是可靠的；「没扫到」不可靠，因此本方法只用于断言<b>存在</b>，不用于断言<b>不存在</b>。
    /// </summary>
    private static bool ReferencesTaskAwaiterGetResult(MethodInfo method)
    {
        var il = method.GetMethodBody()?.GetILAsByteArray();
        if (il is null)
        {
            return false;
        }

        var module = method.Module;
        for (var offset = 0; offset + 5 <= il.Length; offset++)
        {
            if (il[offset] is not (0x28 or 0x6F))
            {
                continue;
            }

            try
            {
                var resolved = module.ResolveMethod(BitConverter.ToInt32(il, offset + 1));
                if (resolved is { Name: "GetResult" }
                    && resolved.DeclaringType == typeof(System.Runtime.CompilerServices.TaskAwaiter<RedisConnection>))
                {
                    return true;
                }
            }
            catch (ArgumentException)
            {
                // 这个偏移不是真实指令边界，试解出来的不是合法 token；继续扫下一个偏移。
            }
        }

        return false;
    }

    private sealed class DecoyTransport : ITransport
    {
        public BrokerAddress BrokerAddress => new("decoy", "decoy");

        public Task<OperateResult> SendAsync(DotNetCore.CAP.Messages.TransportMessage message) =>
            Task.FromResult(OperateResult.Success);
    }
}
