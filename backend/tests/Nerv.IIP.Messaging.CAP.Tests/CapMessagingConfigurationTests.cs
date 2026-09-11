using DotNetCore.CAP;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nerv.IIP.Messaging.CAP;
using System.Reflection;
using Xunit;

namespace Nerv.IIP.Messaging.CAP.Tests;

public sealed class CapMessagingConfigurationTests
{
    [Fact]
    public void UseConfiguredRecovery_OverridesAcceptanceRetryScanSettings()
    {
        var options = new CapOptions();

        options.UseConfiguredRecovery(CreateConfiguration(new Dictionary<string, string?>
        {
            ["Cap:FailedRetryInterval"] = "2",
            ["Cap:FallbackWindowLookbackSeconds"] = "30",
        }));

        Assert.Equal(2, options.FailedRetryInterval);
        Assert.Equal(30, options.FallbackWindowLookbackSeconds);
    }

    [Theory]
    [InlineData("Cap:FailedRetryInterval", "0")]
    [InlineData("Cap:FallbackWindowLookbackSeconds", "29")]
    public void UseConfiguredRecovery_RejectsUnsafeExplicitSettings(string key, string value)
    {
        var options = new CapOptions();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            options.UseConfiguredRecovery(CreateConfiguration(new Dictionary<string, string?>
            {
                [key] = value,
            })));

        Assert.Contains(key, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UseConfiguredTransport_DefaultProvider_RegistersInMemoryMessageQueue()
    {
        var options = new CapOptions();

        options.UseConfiguredTransport(CreateConfiguration(), "Development");

        var extensionTypeNames = GetExtensionTypeNames(options);
        Assert.Contains(extensionTypeNames, name => name.Contains("InMemory", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(extensionTypeNames, name => name.Contains("RabbitMQ", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("InMemory")]
    public void UseConfiguredTransport_InMemoryProviderOutsideDevelopment_FailsFast(string? provider)
    {
        var options = new CapOptions();
        var values = new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Production",
        };

        if (provider is not null)
        {
            values["Messaging:Provider"] = provider;
        }

        var exception = Assert.Throws<InvalidOperationException>(() =>
            options.UseConfiguredTransport(CreateConfiguration(values)));

        Assert.Contains("CAP InMemory transport is only allowed in Development", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Messaging:Provider=RabbitMQ", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UseConfiguredTransport_RabbitMqProvider_RegistersRabbitMqTransport()
    {
        var options = new CapOptions();

        options.UseConfiguredTransport(CreateConfiguration(new Dictionary<string, string?>
        {
            ["Messaging:Provider"] = "RabbitMQ",
            ["RabbitMQ:HostName"] = "rabbitmq.local",
            ["RabbitMQ:Port"] = "5673",
            ["RabbitMQ:UserName"] = "nerv",
            ["RabbitMQ:Password"] = "secret",
        }));

        var extensionTypeNames = GetExtensionTypeNames(options);
        Assert.Contains(extensionTypeNames, name => name.Contains("RabbitMQ", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(extensionTypeNames, name => name.Contains("InMemory", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ApplyRabbitMqConnection_ParsesAspireAmqpConnectionString()
    {
        // The orchestrator (Aspire) injects the broker endpoint as ConnectionStrings:rabbitmq (AMQP URI).
        // Without parsing it, CAP falls back to localhost:5672 -> Broker Unreachable for every service.
        var options = new RabbitMQOptions();

        CapMessagingConfiguration.ApplyRabbitMqConnection(
            options,
            CreateConfiguration(new Dictionary<string, string?>
            {
                ["ConnectionStrings:rabbitmq"] = "amqp://nerv:s3cr3t%21@broker.host:5699/vh1",
            }));

        Assert.Equal("broker.host", options.HostName);
        Assert.Equal(5699, options.Port);
        Assert.Equal("nerv", options.UserName);
        Assert.Equal("s3cr3t!", options.Password); // URL-decoded
        Assert.Equal("vh1", options.VirtualHost);
    }

    [Fact]
    public void ApplyRabbitMqConnection_ExplicitKeysOverrideConnectionString()
    {
        var options = new RabbitMQOptions();

        CapMessagingConfiguration.ApplyRabbitMqConnection(
            options,
            CreateConfiguration(new Dictionary<string, string?>
            {
                ["ConnectionStrings:rabbitmq"] = "amqp://nerv:s3cr3t@broker.host:5699/vh1",
                ["RabbitMQ:HostName"] = "override.host",
                ["RabbitMQ:Port"] = "5673",
                ["RabbitMQ:UserName"] = "override-user",
            }));

        Assert.Equal("override.host", options.HostName);
        Assert.Equal(5673, options.Port);
        Assert.Equal("override-user", options.UserName);
        Assert.Equal("s3cr3t", options.Password); // not overridden -> from connection string
    }

    [Fact]
    public void ApplyRabbitMqConnection_NoConfiguration_UsesLocalhostGuestDefaults()
    {
        var options = new RabbitMQOptions();

        CapMessagingConfiguration.ApplyRabbitMqConnection(options, CreateConfiguration());

        Assert.Equal("localhost", options.HostName);
        Assert.Equal(5672, options.Port);
        Assert.Equal("guest", options.UserName);
        Assert.Equal("guest", options.Password);
    }

    [Fact]
    public void UseConfiguredTransport_RedisProvider_RegistersRedisStreamsTransport()
    {
        var options = new CapOptions();

        options.UseConfiguredTransport(CreateConfiguration(new Dictionary<string, string?>
        {
            ["Messaging:Provider"] = "Redis",
            ["ConnectionStrings:Redis"] = "redis.local:6379",
        }));

        var extensionTypeNames = GetExtensionTypeNames(options);
        Assert.Contains(extensionTypeNames, name => name.Contains("Redis", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(extensionTypeNames, name => name.Contains("RabbitMQ", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(extensionTypeNames, name => name.Contains("InMemory", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UseConfiguredTransport_RedisProvider_AppliesSessionVersionAndTopicPrefix()
    {
        var options = new CapOptions();

        options.UseConfiguredTransport(CreateConfiguration(new Dictionary<string, string?>
        {
            ["Messaging:Provider"] = "Redis",
            ["ConnectionStrings:Redis"] = "redis.local:6379",
            ["Cap:Version"] = "n822-a-019c1234",
            ["Cap:TopicNamePrefix"] = "nerv:n822:019c123456787abc8def0123456789ab:transport:",
        }));

        Assert.Equal("n822-a-019c1234", options.Version);
        Assert.Equal(
            "nerv:n822:019c123456787abc8def0123456789ab:transport:",
            options.TopicNamePrefix);
    }

    /// <summary>
    /// 钉住取值：Redis 传输的连接池大小必须是 1。
    /// 上游默认是 10（<c>CapRedisOptionsPostConfigure</c> 对 <c>default</c> 回填），而 10 会让
    /// <c>RedisConnectionPool.ConnectAsync()</c> 的第二个及以后的调用者在首轮建连期间同步阻塞池线程
    /// （<c>AsyncLazyRedisConnection.CreatedConnection</c> 对在途 Task 做 <c>GetAwaiter().GetResult()</c>）。
    /// 期望值在这里写成字面量而不是引用生产侧的常量：引用常量会被 C# 常量内联成「两边同步改动」，
    /// 把「默认值被改掉」这个变异变成假绿。
    /// </summary>
    [Fact]
    public void UseConfiguredTransport_RedisProvider_PinsConnectionPoolSizeToOne()
    {
        var options = new CapOptions();

        options.UseConfiguredTransport(CreateConfiguration(new Dictionary<string, string?>
        {
            ["Messaging:Provider"] = "Redis",
            ["ConnectionStrings:Redis"] = "redis.local:6379",
        }));

        Assert.Equal(1u, ResolveEffectiveRedisOptions(options).ConnectionPoolSize);
    }

    /// <summary>
    /// 默认 1 是取舍不是铁律（单条多路复用连接的吞吐上界更低），所以必须按宿主可配。
    /// 取值刻意避开 10：10 恰好是上游 <c>CapRedisOptionsPostConfigure</c> 的回填值，
    /// 「配 10 读到 10」对「这行赋值根本没接上」是等价输入，写进来只会虚增格数、不增鉴别力。
    /// </summary>
    [Theory]
    [InlineData("1", 1u)]
    [InlineData("4", 4u)]
    [InlineData("8", 8u)]
    public void UseConfiguredTransport_RedisProvider_HonoursExplicitConnectionPoolSize(
        string configured,
        uint expected)
    {
        var options = new CapOptions();

        options.UseConfiguredTransport(CreateConfiguration(new Dictionary<string, string?>
        {
            ["Messaging:Provider"] = "Redis",
            ["ConnectionStrings:Redis"] = "redis.local:6379",
            ["Messaging:Redis:ConnectionPoolSize"] = configured,
        }));

        Assert.Equal(expected, ResolveEffectiveRedisOptions(options).ConnectionPoolSize);
    }

    /// <summary>
    /// 下界必须在本仓这一侧失败得响：上游对 <c>ConnectionPoolSize == default</c>（即 0）
    /// 会静默回填 10，配成 0 时既不报错也不生效，只会悄悄退回被本票判定为缺陷成因的那个取值。
    /// </summary>
    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("not-a-number")]
    public void UseConfiguredTransport_RedisProviderWithUnusableConnectionPoolSize_FailsFast(string configured)
    {
        var options = new CapOptions();
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Messaging:Provider"] = "Redis",
            ["ConnectionStrings:Redis"] = "redis.local:6379",
            ["Messaging:Redis:ConnectionPoolSize"] = configured,
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            options.UseConfiguredTransport(configuration));

        Assert.Contains(
            "Messaging:Redis:ConnectionPoolSize must be an integer greater than or equal to 1",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void UseConfiguredTransport_RedisProviderWithoutConnectionString_FailsFastWithDiagnosticKeys()
    {
        var options = new CapOptions();
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Messaging:Provider"] = "Redis",
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            options.UseConfiguredTransport(configuration));

        Assert.Contains("Redis CAP transport requires a Redis connection string", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Messaging:Redis:ConnectionString", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ConnectionStrings:Redis", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Caching:Redis", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UseConfiguredTransport_UnsupportedProvider_FailsFast()
    {
        var options = new CapOptions();
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Messaging:Provider"] = "Kafka",
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            options.UseConfiguredTransport(configuration));

        Assert.Contains("Unsupported Messaging:Provider 'Kafka'", exception.Message, StringComparison.Ordinal);
    }

    private static IConfiguration CreateConfiguration(Dictionary<string, string?>? values = null)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values ?? new Dictionary<string, string?>())
            .Build();
    }

    /// <summary>
    /// 取「生效值」而不是「我们写进去的值」：把 CAP 的 Redis 扩展真实注册进一个容器，
    /// 让上游 <c>CapRedisOptionsPostConfigure</c> 也跑一遍。只有这样才能区分「显式配了 1」
    /// 与「留空被上游回填成 10」——这两种在只读我们自己的 lambda 时长得一样。
    /// 这里不连接任何 Redis：只解析 <c>IOptions&lt;CapRedisOptions&gt;</c>，不解析传输/消费者服务。
    /// </summary>
    private static CapRedisOptions ResolveEffectiveRedisOptions(CapOptions options)
    {
        var extensionsProperty = typeof(CapOptions).GetProperty(
            "Extensions",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(extensionsProperty);

        var extensions = (System.Collections.IEnumerable?)extensionsProperty.GetValue(options);
        Assert.NotNull(extensions);

        var services = new ServiceCollection();
        services.AddLogging();
        foreach (var extension in extensions.Cast<ICapOptionsExtension>())
        {
            extension.AddServices(services);
        }

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<CapRedisOptions>>().Value;
    }

    private static string[] GetExtensionTypeNames(CapOptions options)
    {
        // CAP exposes transport selection only through registered extensions; use reflection narrowly
        // here so the tests can assert provider wiring without starting a broker or service provider.
        var extensionsProperty = typeof(CapOptions).GetProperty(
            "Extensions",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(extensionsProperty);

        var extensions = (System.Collections.IEnumerable?)extensionsProperty.GetValue(options);
        Assert.NotNull(extensions);

        return extensions.Cast<object>().Select(x => x.GetType().FullName ?? x.GetType().Name).ToArray();
    }
}
