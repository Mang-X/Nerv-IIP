using DotNetCore.CAP;
using DotNetCore.CAP.Transport;
using StackExchange.Redis;

namespace Nerv.IIP.Messaging.CAP;

/// <summary>
/// #3222：把<b>实际 <c>SubscribeAsync</c></b> 抛出的 Redis 连接/超时异常接进 CAP <b>既有</b>的恢复分支。
///
/// <para><b>缺陷</b>（对上游 <c>DotNetCore.CAP</c> 10.0.1 原文实读；本席位另行核对
/// <c>Internal/IConsumerRegister.Default.cs</c>、<c>RedisStreams/IConsumerClient.Redis.cs</c>、
/// <c>RedisStreams/IRedisStream.Manager.Default.cs</c>、<c>RedisStreams/IConnectionPool.Default.cs</c>、
/// <c>RedisStreams/IConsumerClientFactory.Redis.cs</c>、<c>RedisStreams/RedisErrorExtensions.cs</c>
/// 在 <c>v10.0.1</c> / <c>v10.0.2</c> / <c>master</c> 三个 ref 上<b>逐字节相同</b>，上游未修）：</para>
/// <list type="number">
/// <item><description><c>RedisConsumerClient.SubscribeAsync:33-43</c> 对每个 topic 调
/// <c>CreateStreamWithConsumerGroupAsync</c> → <c>XGROUP</c>。真实连接失败/超时时，
/// <c>StackExchange.Redis</c> 抛的是 <see cref="RedisConnectionException"/> / <see cref="RedisTimeoutException"/>，
/// <b>都不是</b> <see cref="BrokerConnectionException"/>。</description></item>
/// <item><description><c>ConsumerRegister.ExecuteAsync:137-160</c> 的消费线程有三个 catch：
/// <c>OperationCanceledException</c>（忽略）、<c>BrokerConnectionException</c>（<c>_isHealthy = false</c>）、
/// <c>Exception</c>（<b>只记日志</b>）。Redis 的那两类异常落进最后一支。</description></item>
/// <item><description>⇒ 该消费组的 listening 任务就此结束，而 <c>_isHealthy</c> <b>仍为 true</b>，
/// <c>TransportCheckProcessor</c>（<c>Processor/IProcessor.TransportCheck.cs:32-36</c>，每 30 秒
/// <c>if (!_register.IsHealthy()) await _register.ReStartAsync();</c>）永远不会重启它。
/// <b>该消费组在本进程生命周期内不再订阅、不再监听，而健康位是绿的。</b></description></item>
/// </list>
///
/// <para><b>修法就是这么窄，理由也写在这里</b>：只把这两类异常在<b>实际 <c>SubscribeAsync</c> 这一个调用点</b>
/// 包成 <see cref="BrokerConnectionException"/> 并<b>保留 inner</b>，让它落进上游<b>已经存在</b>的第二支 catch。
/// 恢复调度（30 秒的 <c>TransportCheckProcessor</c>、<c>ReStartAsync</c>）<b>一个字都没动</b>——本文件不含
/// 任何 timer / retry / force restart，也不放宽任何时间预算。</para>
///
/// <para>⛔ <b>catch 不扩大，逐条说明为什么</b>：</para>
/// <list type="bullet">
/// <item><description><b>只 catch 具名的两个类型</b>，不是 <c>RedisException</c> 基类。
/// <c>RedisServerException</c>（如 <c>BUSYGROUP</c> / <c>ERR no such key</c>，见上游
/// <c>RedisErrorExtensions.cs</c> 对这两条消息的分类）是 broker <b>答复</b>了的协议层错误，
/// 不是「连不上」；把它转成 <see cref="BrokerConnectionException"/> 会让一个配置/用法错误伪装成掉线，
/// 每 30 秒重启一次消费组且永远不收敛。</description></item>
/// <item><description><b>只包 <c>SubscribeAsync</c> 一个成员。</b><c>FetchTopicsAsync</c> /
/// <c>ListeningAsync</c> / <c>CommitAsync</c>（ACK）/ <c>RejectAsync</c> / <c>DisposeAsync</c> /
/// <c>OnMessageCallback</c> 回调一律透明转发（见 <see cref="DecoratedConsumerClient"/>，那些成员是逐字转发）。
/// 把 ACK 失败也转成「掉线」会让 <c>ConsumerRegister</c> 把一次投递失败当成 broker 故障整组重启。</description></item>
/// <item><description><b>取消透明转发。</b><see cref="IConsumerClient.SubscribeAsync"/> 本身不收
/// <see cref="CancellationToken"/>，所以这里没有「调用方取消」这条入参；inner 若抛
/// <see cref="OperationCanceledException"/>（或任何非具名类型），它<b>不匹配</b>这两个 catch，原样上抛，
/// 由上游第一支 catch 收走。</description></item>
/// </list>
///
/// <para>⭐ <b>失效方向</b>（这条决定哪天会不再成立，写清楚以免后来人读成永真）：上游若把
/// <c>RedisConsumerClient.SubscribeAsync</c> 自己改成抛 <see cref="BrokerConnectionException"/>，
/// 本转换就退化成恒不命中的死代码——彼时应当删掉本文件并升级，而不是留着两层。
/// 判据是拉 <c>RedisStreams/IConsumerClient.Redis.cs</c> 与 <c>Internal/IConsumerRegister.Default.cs</c>
/// 重新比对，不是看版本号。</para>
///
/// <para>⚠️ <b>射程只有 Redis transport</b>：本转换的唯一挂载点是
/// <see cref="DecoratedConsumerClient.SubscribeAsync"/>，而 <see cref="ConsumerClientDecorationExtension"/>
/// 只挂在 Redis 上（见 <c>CapMessagingConfiguration.UseConfiguredTransport</c>）。RabbitMQ / InMemory
/// 宿主没有装饰层，也就没有本转换；它们的 transport 包各自处理自己的连接异常。</para>
/// </summary>
internal static class RedisSubscriptionRecovery
{
    /// <summary>
    /// 转发 <paramref name="inner"/> 的 <see cref="IConsumerClient.SubscribeAsync"/>，
    /// 并且<b>只</b>把该调用抛出的 <see cref="RedisConnectionException"/> / <see cref="RedisTimeoutException"/>
    /// 换成 <see cref="BrokerConnectionException"/>（inner 原样挂上）。其余一切原样上抛。
    /// </summary>
    public static async Task SubscribeAsync(IConsumerClient inner, IEnumerable<string> topics)
    {
        try
        {
            await inner.SubscribeAsync(topics).ConfigureAwait(false);
        }
        catch (RedisConnectionException failure)
        {
            // BrokerConnectionException 的唯一构造函数就是 (Exception inner)，inner 因此必然保留；
            // 诊断信息（endpoint、命令、超时读数）全在 StackExchange.Redis 那条异常自己身上，
            // 这里不重写 message，也就不会把它抄漏或抄错。
            throw new BrokerConnectionException(failure);
        }
        catch (RedisTimeoutException failure)
        {
            throw new BrokerConnectionException(failure);
        }
    }
}
