using DotNetCore.CAP;
using DotNetCore.CAP.Messages;
using DotNetCore.CAP.Transport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Reflection;
using Xunit;

namespace Nerv.IIP.Messaging.CAP.Tests;

/// <summary>
/// #3350（#3236 拆解 2/5）：装饰器骨架<b>在消息路径上 100% 透传</b>，唯一有自身行为的代码在 DI 组装期
/// （<c>AddServices</c> 的 fail closed）。这里的断言分两类——一类钉住「挂载点确实在」（S3/S4 要挂的位置），
/// 一类是反证：装饰前后可观察行为逐字相同。
/// </summary>
public sealed class ConsumerClientDecorationTests
{
    private const string RedisConnectionString = "localhost:6379,abortConnect=false";

    [Fact]
    public void RedisTransport_ReplacesTheTransportFactoryDescriptorWithTheDecorator()
    {
        var services = BuildCapServices(RedisProviderSettings());

        var descriptors = services
            .Where(descriptor => descriptor.ServiceType == typeof(IConsumerClientFactory))
            .ToArray();

        // 单个描述符：MesAssetUnavailableRedisCapTransportTests 那类 services.Single(...) 的调用方依赖这一点。
        var descriptor = Assert.Single(descriptors);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.Equal(typeof(DecoratedConsumerClientFactory), descriptor.ImplementationType);

        using var provider = services.BuildServiceProvider();
        var factory = Assert.IsType<DecoratedConsumerClientFactory>(provider.GetRequiredService<IConsumerClientFactory>());
        Assert.Equal("DotNetCore.CAP.RedisStreams.RedisConsumerClientFactory", factory.Inner.GetType().FullName);
    }

    /// <summary>
    /// 现有调用方（`MesAssetUnavailableRedisCapTransportTests.cs:460-464`）捕获唯一描述符后用
    /// <see cref="ActivatorUtilities.CreateInstance(IServiceProvider, Type, object[])"/> 按 ImplementationType 重建。
    /// 装饰器必须保留这个形状——注册成工厂委托会让 ImplementationType 变成 null 并把那条用例打死。
    /// </summary>
    [Fact]
    public void RedisTransport_KeepsTheDescriptorRebuildableThroughActivatorUtilities()
    {
        var services = BuildCapServices(RedisProviderSettings());
        var descriptor = services.Single(x => x.ServiceType == typeof(IConsumerClientFactory));
        services.Remove(descriptor);

        using var provider = services.BuildServiceProvider();
        var rebuilt = (IConsumerClientFactory)ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!);

        var decorator = Assert.IsType<DecoratedConsumerClientFactory>(rebuilt);
        Assert.Equal("DotNetCore.CAP.RedisStreams.RedisConsumerClientFactory", decorator.Inner.GetType().FullName);
    }

    [Theory]
    [InlineData("InMemory")]
    [InlineData("RabbitMQ")]
    public void NonRedisTransports_AreNotDecorated(string provider)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Messaging:Provider"] = provider,
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["Messaging:RabbitMQ:ConnectionString"] = "amqp://guest:guest@localhost:5672/",
        };

        var services = BuildCapServices(settings);

        var descriptor = services.Single(x => x.ServiceType == typeof(IConsumerClientFactory));
        Assert.NotEqual(typeof(DecoratedConsumerClientFactory), descriptor.ImplementationType);
        Assert.DoesNotContain(
            services,
            x => x.ServiceType == typeof(TransportConsumerClientFactory));
    }

    /// <summary>S3（首轮订阅闸门）与 S4（ListeningAsync 专用线程）都要挂在 client 这一层，故工厂必须包住它创建的 client。</summary>
    [Fact]
    public async Task Factory_WrapsEveryCreatedClientSoDownstreamWorkHasAMountPoint()
    {
        var inner = new RecordingConsumerClient();
        var factory = new DecoratedConsumerClientFactory(new TransportConsumerClientFactory(new StubFactory(inner)));

        var created = await factory.CreateAsync("group-a", 3);

        var decorated = Assert.IsType<DecoratedConsumerClient>(created);
        Assert.Same(inner, decorated.Inner);
        Assert.Equal([("group-a", (byte)3)], ((StubFactory)factory.Inner).Created);
    }

    /// <summary>反证：把同一份脚本分别跑在未装饰的 client 和装饰过的 client 上，两侧记录必须逐字相同。</summary>
    [Fact]
    public async Task DecoratedClient_ProducesTheSameTranscriptAsTheUndecoratedClient()
    {
        var undecorated = await ExerciseAsync(static inner => inner);
        var decorated = await ExerciseAsync(static inner => new DecoratedConsumerClient(inner, new FirstSubscriptionGate()));

        Assert.Equal(undecorated.CallerObservations, decorated.CallerObservations);
        Assert.Equal(undecorated.InnerCalls, decorated.InnerCalls);
    }

    [Fact]
    public async Task DecoratedClient_PropagatesFailuresIdenticallyToTheUndecoratedClient()
    {
        var undecorated = await ExerciseFailuresAsync(static inner => inner);
        var decorated = await ExerciseFailuresAsync(static inner => new DecoratedConsumerClient(inner, new FirstSubscriptionGate()));

        Assert.Equal(undecorated, decorated);
        Assert.NotEmpty(undecorated);
    }

    /// <summary>
    /// 上界闭合，<b>粒度是成员名</b>：脚本触及的成员名集合必须等于 <see cref="IConsumerClient"/>（经
    /// <see cref="Type.GetInterfaces"/> 递归收进 <see cref="IAsyncDisposable"/>）的全部成员名。
    ///
    /// <para>⚠️ 残留边界，别读成完全闭合：<b>上游新增成员会让这条红；给已有成员名新增第二个访问器不会</b>
    /// ——名字集合没变。今天两个可写属性的 setter 之所以被覆盖，是因为反证脚本各做了 set → 读回 → 核对
    /// inner 侧收到，不是因为这条断言拦得住。访问器那一面由编译器兜住：<see cref="IConsumerClient"/> 的成员
    /// 新增访问器会让 <c>DecoratedConsumerClient</c> 不再实现该接口而编译失败，所以它不会静默漏掉——
    /// 但那不是这条用例的功劳。</para>
    /// </summary>
    [Fact]
    public async Task TranscriptCoversEveryConsumerClientMember()
    {
        var exercised = (await ExerciseAsync(static inner => new DecoratedConsumerClient(inner, new FirstSubscriptionGate()))).ExercisedMembers;

        var declared = typeof(IConsumerClient)
            .GetInterfaces()
            .Append(typeof(IConsumerClient))
            .SelectMany(type => type.GetMembers(BindingFlags.Public | BindingFlags.Instance))
            .Where(member => member is not MethodInfo { IsSpecialName: true })
            .Select(member => member.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(declared.OrderBy(x => x, StringComparer.Ordinal), exercised.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void AddServices_FailsFastWhenTheTransportFactoryCannotBeRebuilt()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConsumerClientFactory>(_ => new StubFactory(new RecordingConsumerClient()));

        var exception = Assert.Throws<InvalidOperationException>(
            () => new ConsumerClientDecorationExtension().AddServices(services));

        Assert.Contains(nameof(IConsumerClientFactory), exception.Message, StringComparison.Ordinal);
    }

    private static Dictionary<string, string?> RedisProviderSettings() => new()
    {
        ["Messaging:Provider"] = "Redis",
        ["Messaging:Redis:ConnectionString"] = RedisConnectionString,
    };

    private static ServiceCollection BuildCapServices(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCap(options => options.UseConfiguredTransport(configuration));
        return services;
    }

    private static async Task<Transcript> ExerciseAsync(Func<RecordingConsumerClient, IConsumerClient> compose)
    {
        var inner = new RecordingConsumerClient();
        var client = compose(inner);
        var observations = new List<string>();
        var members = new HashSet<string>(StringComparer.Ordinal);

        observations.Add($"BrokerAddress={client.BrokerAddress}");
        members.Add(nameof(IConsumerClient.BrokerAddress));

        Func<TransportMessage, object?, Task> messageCallback = (_, _) => Task.CompletedTask;
        client.OnMessageCallback = messageCallback;
        observations.Add($"OnMessageCallback.roundTrip={ReferenceEquals(messageCallback, client.OnMessageCallback)}");
        observations.Add($"OnMessageCallback.reachedInner={ReferenceEquals(messageCallback, inner.StoredMessageCallback)}");
        members.Add(nameof(IConsumerClient.OnMessageCallback));

        Action<LogMessageEventArgs> logCallback = _ => { };
        client.OnLogCallback = logCallback;
        observations.Add($"OnLogCallback.roundTrip={ReferenceEquals(logCallback, client.OnLogCallback)}");
        observations.Add($"OnLogCallback.reachedInner={ReferenceEquals(logCallback, inner.StoredLogCallback)}");
        members.Add(nameof(IConsumerClient.OnLogCallback));

        var fetched = await client.FetchTopicsAsync(["topic-a", "topic-b"]);
        observations.Add($"FetchTopicsAsync={string.Join("|", fetched)}");
        members.Add(nameof(IConsumerClient.FetchTopicsAsync));

        await client.SubscribeAsync(["topic-a", "topic-b"]);
        members.Add(nameof(IConsumerClient.SubscribeAsync));

        using var cancellation = new CancellationTokenSource();
        await client.ListeningAsync(TimeSpan.FromMilliseconds(37), cancellation.Token);
        members.Add(nameof(IConsumerClient.ListeningAsync));

        await client.CommitAsync("commit-sender");
        members.Add(nameof(IConsumerClient.CommitAsync));

        await client.RejectAsync("reject-sender");
        members.Add(nameof(IConsumerClient.RejectAsync));

        await client.DisposeAsync();
        members.Add(nameof(IAsyncDisposable.DisposeAsync));

        observations.Add($"listeningTokenIsSame={inner.ObservedListeningToken.Equals(cancellation.Token)}");

        return new Transcript(inner.Calls, observations, members);
    }

    private static async Task<IReadOnlyList<string>> ExerciseFailuresAsync(Func<RecordingConsumerClient, IConsumerClient> compose)
    {
        var failures = new List<string>();

        foreach (var member in new[]
                 {
                     nameof(IConsumerClient.FetchTopicsAsync),
                     nameof(IConsumerClient.SubscribeAsync),
                     nameof(IConsumerClient.ListeningAsync),
                     nameof(IConsumerClient.CommitAsync),
                     nameof(IConsumerClient.RejectAsync),
                     nameof(IAsyncDisposable.DisposeAsync),
                 })
        {
            var inner = new RecordingConsumerClient { FailingMember = member };
            var client = compose(inner);
            var failure = await Record.ExceptionAsync(() => member switch
            {
                nameof(IConsumerClient.FetchTopicsAsync) => client.FetchTopicsAsync(["topic-a"]),
                nameof(IConsumerClient.SubscribeAsync) => client.SubscribeAsync(["topic-a"]),
                nameof(IConsumerClient.ListeningAsync) => client.ListeningAsync(TimeSpan.Zero, CancellationToken.None),
                nameof(IConsumerClient.CommitAsync) => client.CommitAsync(null),
                nameof(IConsumerClient.RejectAsync) => client.RejectAsync(null),
                _ => client.DisposeAsync().AsTask(),
            });

            failures.Add($"{member}:{failure?.GetType().Name}:{failure?.Message}");
        }

        return failures;
    }

    private sealed record Transcript(
        IReadOnlyList<string> InnerCalls,
        IReadOnlyList<string> CallerObservations,
        IReadOnlySet<string> ExercisedMembers);

    private sealed class StubFactory(IConsumerClient client) : IConsumerClientFactory
    {
        public List<(string GroupName, byte GroupConcurrent)> Created { get; } = [];

        public Task<IConsumerClient> CreateAsync(string groupName, byte groupConcurrent)
        {
            Created.Add((groupName, groupConcurrent));
            return Task.FromResult(client);
        }
    }

    private sealed class RecordingConsumerClient : IConsumerClient
    {
        private readonly List<string> calls = [];

        public IReadOnlyList<string> Calls => calls;
        public string? FailingMember { get; init; }
        public Func<TransportMessage, object?, Task>? StoredMessageCallback { get; private set; }
        public Action<LogMessageEventArgs>? StoredLogCallback { get; private set; }
        public CancellationToken ObservedListeningToken { get; private set; }

        public BrokerAddress BrokerAddress
        {
            get
            {
                calls.Add("BrokerAddress");
                return new BrokerAddress("recording", "endpoint-3350");
            }
        }

        public Func<TransportMessage, object?, Task>? OnMessageCallback
        {
            get
            {
                calls.Add("get_OnMessageCallback");
                return StoredMessageCallback;
            }
            set
            {
                calls.Add("set_OnMessageCallback");
                StoredMessageCallback = value;
            }
        }

        public Action<LogMessageEventArgs>? OnLogCallback
        {
            get
            {
                calls.Add("get_OnLogCallback");
                return StoredLogCallback;
            }
            set
            {
                calls.Add("set_OnLogCallback");
                StoredLogCallback = value;
            }
        }

        public Task<ICollection<string>> FetchTopicsAsync(IEnumerable<string> topicNames)
        {
            var names = topicNames.ToArray();
            calls.Add($"FetchTopicsAsync({string.Join("|", names)})");
            Throw(nameof(FetchTopicsAsync));
            return Task.FromResult<ICollection<string>>(names.Select(name => name + ".resolved").ToArray());
        }

        public Task SubscribeAsync(IEnumerable<string> topics)
        {
            calls.Add($"SubscribeAsync({string.Join("|", topics)})");
            Throw(nameof(SubscribeAsync));
            return Task.CompletedTask;
        }

        public Task ListeningAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            calls.Add($"ListeningAsync({timeout.TotalMilliseconds})");
            ObservedListeningToken = cancellationToken;
            Throw(nameof(ListeningAsync));
            return Task.CompletedTask;
        }

        public Task CommitAsync(object? sender)
        {
            calls.Add($"CommitAsync({sender})");
            Throw(nameof(CommitAsync));
            return Task.CompletedTask;
        }

        public Task RejectAsync(object? sender)
        {
            calls.Add($"RejectAsync({sender})");
            Throw(nameof(RejectAsync));
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            calls.Add("DisposeAsync");
            Throw(nameof(DisposeAsync));
            return ValueTask.CompletedTask;
        }

        private void Throw(string member)
        {
            if (string.Equals(FailingMember, member, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"recording client failed on {member}");
            }
        }
    }
}
