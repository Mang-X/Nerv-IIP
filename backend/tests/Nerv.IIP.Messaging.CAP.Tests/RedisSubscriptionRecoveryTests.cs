using DotNetCore.CAP;
using DotNetCore.CAP.Messages;
using DotNetCore.CAP.Transport;
using StackExchange.Redis;
using Xunit;

namespace Nerv.IIP.Messaging.CAP.Tests;

/// <summary>
/// #3222 的<b>边界</b>一层：转换只在 <see cref="IConsumerClient.SubscribeAsync"/>、只对具名的两个类型发生。
///
/// <para>⚠️ <b>这一层证明不了「真实 Redis 会抛这两个类型、且抛在 Subscribe 上」</b>——那是
/// <see cref="RedisSubscriptionRecoveryRedisCapTests"/>（真 Redis、真 <c>XGROUP</c>、真 <c>ConsumerRegister</c>）
/// 的事。本文件的被测命题是<b>反面</b>的：哪些东西<b>不</b>被转换。这两层合起来才是验收的那一条；
/// ⛔ 单看本文件全绿不构成「缺陷修了」。</para>
///
/// <para>本文件<b>不</b>自造 catch：被测路径是生产的 <see cref="DecoratedConsumerClient"/> →
/// <see cref="RedisSubscriptionRecovery"/>，夹具只负责在指定成员上抛出指定异常实例。</para>
/// </summary>
public sealed class RedisSubscriptionRecoveryTests
{
    /// <summary>
    /// 被转换的两个类型 —— 这是转换存在的全部理由：上游 <c>ConsumerRegister</c> 只认
    /// <see cref="BrokerConnectionException"/> 才置健康位。
    /// </summary>
    public static TheoryData<string, Exception> ConvertedSubscribeFailures() => new()
    {
        {
            nameof(RedisConnectionException),
            new RedisConnectionException(ConnectionFailureType.UnableToConnect, "connect failed")
        },
        {
            nameof(RedisTimeoutException),
            new RedisTimeoutException("timed out", CommandStatus.WaitingInBacklog)
        },
    };

    /// <summary>
    /// ⛔ <b>不</b>被转换的类型。逐条都有具体理由，不是凑数：
    /// <list type="bullet">
    /// <item><description><see cref="RedisServerException"/>：broker <b>答复过</b>的协议层错误
    /// （<c>BUSYGROUP</c> / <c>ERR no such key</c> 正是这一类，见上游 <c>RedisErrorExtensions.cs</c>）。
    /// 转换它等于把配置/用法错误伪装成掉线，每 30 秒重启一次且永不收敛。</description></item>
    /// <item><description><see cref="RedisCommandException"/>：客户端侧用法错误，同理。</description></item>
    /// <item><description>⭐ <b>两个基类格各守一支 catch，不是同一件事说两遍。</b>
    /// <see cref="RedisConnectionException"/> 与 <see cref="RedisTimeoutException"/> <b>没有公共基类</b>：
    /// 前者派生自 <see cref="RedisException"/>，后者派生自 <see cref="TimeoutException"/>
    /// （⚠️ 先前这里写的「<c>RedisException</c> 是二者的共同基类」是<b>错的</b>，已更正）。
    /// ⇒ <see cref="RedisException"/> 这一格钉住「连接那支 catch 没有放宽成 <c>RedisException</c>」，
    /// <see cref="TimeoutException"/> 那一格钉住「超时那支 catch 没有放宽成 <c>TimeoutException</c>」，
    /// 两格各自只对一支有鉴别力。放宽任一支都会把非目标异常（另一个 Redis 客户端错误族 / 非 Redis 的超时）
    /// 一起吞成「掉线」。</description></item>
    /// <item><description><see cref="OperationCanceledException"/> / <see cref="TaskCanceledException"/>：
    /// 取消必须原样上抛，交给上游 <c>ConsumerRegister.ExecuteAsync</c> 的第一支 catch。</description></item>
    /// <item><description><see cref="InvalidOperationException"/>：泛化的「其它异常」对照。</description></item>
    /// </list>
    /// </summary>
    public static TheoryData<string, Exception> UnconvertedSubscribeFailures() => new()
    {
        { nameof(RedisServerException), new RedisServerException("BUSYGROUP Consumer Group name already exists") },
        { nameof(RedisCommandException), new RedisCommandException("command is not available") },
        { nameof(TimeoutException), new TimeoutException("a plain timeout that is not Redis") },
        { nameof(RedisException), new RedisException("a plain Redis exception") },
        { nameof(OperationCanceledException), new OperationCanceledException("caller cancelled") },
        { nameof(TaskCanceledException), new TaskCanceledException("caller cancelled") },
        { nameof(InvalidOperationException), new InvalidOperationException("something else entirely") },
    };

    [Theory]
    [MemberData(nameof(ConvertedSubscribeFailures))]
    public async Task Subscribe_converts_redis_connection_and_timeout_failures_and_keeps_the_inner_exception(
        string name,
        Exception failure)
    {
        var inner = new ThrowingConsumerClient(nameof(IConsumerClient.SubscribeAsync), failure);
        var client = new DecoratedConsumerClient(inner);

        var thrown = await Assert.ThrowsAsync<BrokerConnectionException>(() => client.SubscribeAsync(["topic-a"]));

        // 真实 inner 保留：不是「同类型的另一个实例」，是<b>同一个对象</b>，诊断信息一个字都没丢。
        Assert.Same(failure, thrown.InnerException);
        Assert.Equal(failure.Message, thrown.InnerException!.Message);
        Assert.Equal(name, thrown.InnerException.GetType().Name);
        // 转换发生在 inner 真的被调用之后，而不是替换掉这次调用。
        Assert.Equal([$"{nameof(IConsumerClient.SubscribeAsync)}:topic-a"], inner.Calls);
    }

    [Theory]
    [MemberData(nameof(UnconvertedSubscribeFailures))]
    public async Task Subscribe_forwards_every_other_failure_untouched(string name, Exception failure)
    {
        var inner = new ThrowingConsumerClient(nameof(IConsumerClient.SubscribeAsync), failure);
        var client = new DecoratedConsumerClient(inner);

        var thrown = await Record.ExceptionAsync(() => client.SubscribeAsync(["topic-a"]));

        // 同一个实例上抛：既不是 BrokerConnectionException，也不是被重新包过一层的同类型副本。
        Assert.Same(failure, thrown);
        Assert.Equal(name, thrown!.GetType().Name);
        Assert.IsNotType<BrokerConnectionException>(thrown);
    }

    /// <summary>
    /// ⭐ <b>catch 没有扩大到别的成员</b>：把<b>同样</b>的两个 Redis 异常挂到 <c>SubscribeAsync</c> 之外的
    /// 每一个成员上，都必须原样上抛。
    ///
    /// <para>为什么这条重要：<c>CommitAsync</c>（ACK）失败若被转成「掉线」，<c>ConsumerRegister</c> 会把
    /// 一次投递失败当成 broker 故障整组重启；而 <c>DisposeAsync</c> 失败被转换会让停机路径上多出一次伪掉线。</para>
    ///
    /// <para>⚠️ 覆盖边界如实写：本用例覆盖 <see cref="IConsumerClient"/> 上<b>会抛的那几个方法</b>
    /// （成员清单由下面的断言从接口反射推导并与实际行使集合比对，不是手列的），
    /// <b>不</b>覆盖两个回调属性——那两个属性只是 setter/getter 转发，调用回调的是 inner 自己，
    /// 本转换在构造上就够不着它们。</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(ConvertedSubscribeFailures))]
    public async Task Members_other_than_subscribe_never_convert_the_same_redis_failures(string name, Exception failure)
    {
        Assert.NotNull(name);
        var observed = new List<string>();

        foreach (var member in ThrowingConsumerClient.ThrowableMembers)
        {
            if (string.Equals(member, nameof(IConsumerClient.SubscribeAsync), StringComparison.Ordinal)) continue;

            var inner = new ThrowingConsumerClient(member, failure);
            var client = new DecoratedConsumerClient(inner);
            var thrown = await Record.ExceptionAsync(() => member switch
            {
                nameof(IConsumerClient.FetchTopicsAsync) => client.FetchTopicsAsync(["topic-a"]),
                nameof(IConsumerClient.ListeningAsync) => client.ListeningAsync(TimeSpan.Zero, CancellationToken.None),
                nameof(IConsumerClient.CommitAsync) => client.CommitAsync("sender"),
                nameof(IConsumerClient.RejectAsync) => client.RejectAsync("sender"),
                _ => client.DisposeAsync().AsTask(),
            });

            observed.Add($"{member}:{(ReferenceEquals(thrown, failure) ? "same-instance" : thrown?.GetType().Name)}");
        }

        Assert.Equal(
            ThrowableMembersExceptSubscribe.Select(member => $"{member}:same-instance"),
            observed);
    }

    /// <summary>
    /// 上界闭合：上面那条「其它成员一律不转换」行使的成员集合，必须等于 <see cref="IConsumerClient"/>
    /// 上<b>会抛的方法</b>减去 <c>SubscribeAsync</c>。上游给接口加一个新方法时这条会红，
    /// 逼着新成员被明确判一次「转不转换」，而不是静默落在覆盖面之外。
    /// </summary>
    [Fact]
    public void The_unconverted_member_set_equals_every_throwable_consumer_client_method()
    {
        var declared = typeof(IConsumerClient)
            .GetInterfaces()
            .Append(typeof(IConsumerClient))
            .SelectMany(type => type.GetMethods())
            .Where(method => !method.IsSpecialName)
            .Select(method => method.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(
            declared.OrderBy(x => x, StringComparer.Ordinal),
            ThrowingConsumerClient.ThrowableMembers.OrderBy(x => x, StringComparer.Ordinal));
        Assert.Contains(nameof(IConsumerClient.SubscribeAsync), declared);
    }

    [Fact]
    public async Task Subscribe_forwards_the_topics_and_returns_when_the_broker_is_healthy()
    {
        var inner = new ThrowingConsumerClient(failingMember: null, failure: null);
        var client = new DecoratedConsumerClient(inner);

        await client.SubscribeAsync(["topic-a", "topic-b"]);

        Assert.Equal([$"{nameof(IConsumerClient.SubscribeAsync)}:topic-a,topic-b"], inner.Calls);
    }

    private static IEnumerable<string> ThrowableMembersExceptSubscribe =>
        ThrowingConsumerClient.ThrowableMembers.Where(
            member => !string.Equals(member, nameof(IConsumerClient.SubscribeAsync), StringComparison.Ordinal));

    /// <summary>纯夹具：在指定成员上抛出<b>调用方给的那个实例</b>，不自己造异常、不自己 catch。</summary>
    private sealed class ThrowingConsumerClient(string? failingMember, Exception? failure) : IConsumerClient
    {
        public static readonly string[] ThrowableMembers =
        [
            nameof(IConsumerClient.CommitAsync),
            nameof(IAsyncDisposable.DisposeAsync),
            nameof(IConsumerClient.FetchTopicsAsync),
            nameof(IConsumerClient.ListeningAsync),
            nameof(IConsumerClient.RejectAsync),
            nameof(IConsumerClient.SubscribeAsync),
        ];

        public List<string> Calls { get; } = [];

        public BrokerAddress BrokerAddress => new("redis", "fixture");

        public Func<TransportMessage, object?, Task>? OnMessageCallback { get; set; }

        public Action<LogMessageEventArgs>? OnLogCallback { get; set; }

        public Task<ICollection<string>> FetchTopicsAsync(IEnumerable<string> topicNames)
        {
            var names = topicNames.ToArray();
            Record(nameof(IConsumerClient.FetchTopicsAsync), string.Join(",", names));
            return Task.FromResult<ICollection<string>>(names);
        }

        public Task SubscribeAsync(IEnumerable<string> topics)
        {
            Record(nameof(IConsumerClient.SubscribeAsync), string.Join(",", topics));
            return Task.CompletedTask;
        }

        public Task ListeningAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            Record(nameof(IConsumerClient.ListeningAsync), timeout.ToString());
            return Task.CompletedTask;
        }

        public Task CommitAsync(object? sender)
        {
            Record(nameof(IConsumerClient.CommitAsync), $"{sender}");
            return Task.CompletedTask;
        }

        public Task RejectAsync(object? sender)
        {
            Record(nameof(IConsumerClient.RejectAsync), $"{sender}");
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Record(nameof(IAsyncDisposable.DisposeAsync), string.Empty);
            return ValueTask.CompletedTask;
        }

        private void Record(string member, string argument)
        {
            Calls.Add($"{member}:{argument}");
            if (failure is not null && string.Equals(member, failingMember, StringComparison.Ordinal))
            {
                throw failure;
            }
        }
    }
}
