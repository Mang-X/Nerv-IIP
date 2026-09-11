using System.Reflection;
using DotNetCore.CAP;
using DotNetCore.CAP.Messages;
using DotNetCore.CAP.RedisStreams;
using DotNetCore.CAP.Transport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Testing;
using Xunit;

namespace Nerv.IIP.Business.Mes.Web.Tests;

/// <summary>
/// #3365 验收 B（装配断言）：把连接池预热<b>装进真实 CAP 宿主</b>之后，第一次触到池的调用返回时，
/// 池的 N/N 槽位必须已经完成。
///
/// <para><b>为什么必须在真实宿主里测</b>：#3236 方向 D 的调查席位实证的是各个零件（描述符形状、装饰配方、
/// 预热机制、入口闭集），<b>没有把组装后的整体装进真实 CAP 宿主跑通</b>。单元层的装配用例只能证明
/// 「描述符被换掉了」，证不到「真实宿主启动后，CAP 自己解析出来的那个实例就是装饰器，而且它真的把池热满了」。</para>
///
/// <para>⭐ <b>本用例针对的是发布侧</b>（<see cref="ITransport.SendAsync"/>）。发布侧是本链条上
/// <b>已经漏过一次</b>的那条路：纯发布型宿主一次 <c>CreateAsync</c> 都不会调用，只挂消费侧等于完全没挂。
/// 为了让断言<b>不是空过</b>，消费侧在这里被一道门闩按住——所以「池被热满」这件事在归因上只能来自
/// <c>SendAsync</c>，不可能来自消费侧。门闩之外的前置断言（发送前池<b>没有</b>全部建好）承担这个归因。</para>
/// </summary>
[Collection(WebApplicationFactoryCollection.Name)]
public sealed class RedisConnectionPoolWarmupRealHostTests
{
    private const string DeploymentProfile = "Issue3365Acceptance";

    [MesConnectionPoolWarmupPostgresRedisFact]
    public async Task First_publish_in_a_real_cap_host_leaves_every_connection_pool_slot_completed()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        var consumerGate = new ConsumerClientGate();
        await using var factory = CreateFactory(consumerGate);
        try
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            MesPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
            await db.Database.MigrateAsync();

            var slots = ReadPoolSlots(factory.Services);

            // 归因前置：消费侧被门闩按住、也还没有人发布过，所以池此刻不可能已经建满。
            // 这条一旦失效，下面那条「发送后建满」就变成空过断言，证不到 SendAsync 的功劳。
            Assert.Equal(ExpectedConnectionPoolSize, slots.Length);
            Assert.DoesNotContain(slots, slot => slot.IsValueCreated);

            var transport = factory.Services.GetRequiredService<ITransport>();
            Assert.Equal("Nerv.IIP.Messaging.CAP.WarmedTransport", transport.GetType().FullName);

            var result = await transport.SendAsync(NewMessage());

            // 第一次 SendAsync 返回的那一刻——不轮询、不等待——池必须 N/N 全部完成。
            Assert.True(result.Succeeded);
            Assert.All(slots, slot =>
            {
                Assert.True(slot.IsValueCreated);
                Assert.True(slot.Value.IsCompletedSuccessfully);
            });
        }
        finally
        {
            consumerGate.Release();
        }
    }

    /// <summary>消费侧挂载点在真实宿主里也必须是我们的装饰器，否则 <c>CreateAsync</c> 那条入口没有被覆盖。</summary>
    [MesConnectionPoolWarmupPostgresRedisFact]
    public async Task Real_cap_host_resolves_the_decorated_consumer_client_factory()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        var consumerGate = new ConsumerClientGate();
        await using var factory = CreateFactory(consumerGate);
        try
        {
            using var scope = factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();

            var inner = Assert.IsType<ConsumerClientGate.GatedFactory>(
                factory.Services.GetRequiredService<IConsumerClientFactory>()).Inner;

            Assert.Equal("Nerv.IIP.Messaging.CAP.DecoratedConsumerClientFactory", inner.GetType().FullName);
        }
        finally
        {
            consumerGate.Release();
        }
    }

    private const int ExpectedConnectionPoolSize = 10;

    /// <summary>
    /// 按类型锚定位池：在 RedisStreams 程序集里找「持有一个可赋值给
    /// <c>IEnumerable&lt;AsyncLazyRedisConnection&gt;</c> 的字段」的那个类型，再从它实现的接口里取服务类型。
    /// 元素类型 <see cref="AsyncLazyRedisConnection"/> 是 public，所以这里不出现任何上游字段名或接口名字面量。
    /// </summary>
    private static AsyncLazyRedisConnection[] ReadPoolSlots(IServiceProvider provider)
    {
        var poolImplementation = typeof(AsyncLazyRedisConnection).Assembly
            .GetTypes()
            .Single(type => SlotsFieldOf(type) is not null);
        var poolServiceType = poolImplementation
            .GetInterfaces()
            .Single(contract => contract != typeof(IDisposable) && contract != typeof(IAsyncDisposable));
        var pool = provider.GetRequiredService(poolServiceType);

        return ((IEnumerable<AsyncLazyRedisConnection>)SlotsFieldOf(poolImplementation)!.GetValue(pool)!).ToArray();
    }

    private static FieldInfo? SlotsFieldOf(Type type)
    {
        var fields = type
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(field => typeof(IEnumerable<AsyncLazyRedisConnection>).IsAssignableFrom(field.FieldType))
            .ToArray();
        return fields.Length == 1 ? fields[0] : null;
    }

    private static TransportMessage NewMessage() => new(
        new Dictionary<string, string?>
        {
            [Headers.MessageId] = Guid.NewGuid().ToString("N"),
            [Headers.MessageName] = "nerv-iip.issue3365acceptance.warmup-probe",
        },
        ReadOnlyMemory<byte>.Empty);

    private static WebApplicationFactory<Program> CreateFactory(ConsumerClientGate consumerGate)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Persistence:Provider"] = "PostgreSQL",
            ["Persistence:AutoMigrate"] = "false",
            ["ConnectionStrings:PostgreSQL"] = MesPostgresLaneDatabase.ConnectionString,
            ["Messaging:Provider"] = "Redis",
            ["Messaging:Redis:ConnectionString"] = Environment.GetEnvironmentVariable("NERV_IIP_TEST_REDIS"),
            ["ConnectionStrings:Redis"] = Environment.GetEnvironmentVariable("NERV_IIP_TEST_REDIS"),
            ["Cap:Version"] = Environment.GetEnvironmentVariable("NERV_IIP_TEST_CAP_VERSION"),
            ["Cap:TopicNamePrefix"] = Environment.GetEnvironmentVariable("NERV_IIP_TEST_CAP_TOPIC_PREFIX"),
            ["InternalService:BearerToken"] = "test-internal-token",
            ["Approval:BaseUrl"] = "https://approval.test",
            ["MasterData:BaseUrl"] = "https://master-data.test",
            ["Quality:BaseUrl"] = "https://quality.test",
            ["ProductEngineering:BaseUrl"] = "https://product-engineering.test",
            ["Inventory:BaseUrl"] = "https://inventory.test",
        };

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(DeploymentProfile);
            foreach (var (key, value) in settings) builder.UseSetting(key, value);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
            builder.ConfigureServices(services =>
            {
                // 门闩包在最外层：CAP 解析到的是它，它在放行之前不会触碰内层的
                // DecoratedConsumerClientFactory，因此消费侧那条池入口在放行前完全没有发生。
                var descriptor = services.Single(x => x.ServiceType == typeof(IConsumerClientFactory));
                services.Remove(descriptor);
                services.AddSingleton<IConsumerClientFactory>(provider => new ConsumerClientGate.GatedFactory(
                    (IConsumerClientFactory)ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!),
                    consumerGate));
            });
        });
    }

    private sealed class ConsumerClientGate
    {
        private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => released.TrySetResult();

        internal sealed class GatedFactory(IConsumerClientFactory inner, ConsumerClientGate gate) : IConsumerClientFactory
        {
            public IConsumerClientFactory Inner => inner;

            public async Task<IConsumerClient> CreateAsync(string groupName, byte groupConcurrent)
            {
                await gate.released.Task.ConfigureAwait(false);
                return await inner.CreateAsync(groupName, groupConcurrent).ConfigureAwait(false);
            }
        }
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class MesConnectionPoolWarmupPostgresRedisFactAttribute : FactAttribute
{
    public MesConnectionPoolWarmupPostgresRedisFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_REDIS")))
        {
            Skip = "Set NERV_IIP_TEST_POSTGRES and NERV_IIP_TEST_REDIS to run the real PostgreSQL + Redis CAP connection pool warmup proof.";
        }
    }
}
