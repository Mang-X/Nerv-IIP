using DotNetCore.CAP.Internal;

namespace Nerv.IIP.Messaging.CAP;

/// <summary>
/// #3249：CAP 的<b>最终停止</b>信号，供 <see cref="DecoratedConsumerClient.ListeningAsync"/> 接住。
///
/// <para><b>要补的洞</b>（对上游 <c>DotNetCore.CAP</c> 10.0.1 <c>Internal/IConsumerRegister.Default.cs</c> 原文实读，
/// v10.0.2 与 <c>master</c> 该文件与 v10.0.1 逐字节相同）：<c>ConsumerRegister.StartAsync:59-60</c> 建的是
/// <c>CreateLinkedTokenSource(stoppingToken)</c> 并 <c>Token.Register(Dispose)</c>；而<b>正常恢复</b>走的
/// <c>ReStartAsync:79</c> 建的是<b>裸</b> <c>new CancellationTokenSource()</c>——<b>既不 link <c>stoppingToken</c>，
/// 也不 <c>Token.Register(Dispose)</c></b>，且 <c>ReStartAsync:73-84</c> 体内没有 <c>_disposed = 0</c>
/// （全文件只有 <c>StartAsync:70</c> 有）。⇒ 恢复之后：</para>
/// <list type="number">
/// <item><description>宿主停机取消 <c>Bootstrapper</c> 的 CTS，<b>到不了</b>这一代 listener——新 CTS 没 link 它；</description></item>
/// <item><description><c>Bootstrapper</c> 停机回调里的 <c>item.Dispose()</c> 走进 <c>ConsumerRegister.Dispose():88-89</c>，
/// 因 <c>_disposed</c> 仍为 1 而<b>早退</b>，<c>Pulse()</c> 不会执行。</description></item>
/// </list>
/// <para>⇒ 两条路同时断掉，恢复出来的 listener <b>收不到任何最终停止信号</b>。生产可达：上游
/// <c>Processor/IProcessor.TransportCheck.cs:32-36</c> 是 CAP 自带常驻处理器，每 30 秒
/// <c>if (!_register.IsHealthy()) await _register.ReStartAsync();</c>。</para>
///
/// <para><b>为什么补在这里、而不是包一层 <c>IConsumerRegister</c></b>：<c>IConsumerRegister</c> 的公开面只有
/// <c>IsHealthy()</c> / <c>ReStartAsync(bool)</c> / <c>StartAsync(CancellationToken)</c> / <c>Dispose()</c>。
/// 恢复之后这四个成员<b>没有一个能停下当前这一代 listener</b>：<c>Dispose()</c> 早退；<c>StartAsync</c> 会直接覆写
/// <c>_cts</c> 而<b>不取消</b>上一代，把上一代变成永远停不下来的孤儿；<c>ReStartAsync</c> 的 <c>Pulse()</c> 虽然能取消
/// 当前 CTS，但它紧接着又用裸 CTS 起了<b>新的一代</b>——每调一次就多一代孤儿，而且这正是验收明令禁止的
/// 「停止后重新启动」。<c>_isHealthy</c> 又只被 <c>ReStartAsync</c> 置回 <c>true</c>，所以也不能绕开它自己实现恢复。
/// ⇒ 停止信号只能在<b>交给 listener 的那个 token</b> 那一层补，也就是 <c>IConsumerClient.ListeningAsync</c>。</para>
///
/// <para><b>信号源为什么是 <see cref="IProcessingServer"/></b>：<c>Bootstrapper</c> 把
/// <c>GetServices&lt;IProcessingServer&gt;()</c> 的每一项都 <c>StartAsync(stoppingToken)</c>、停机时又逐项
/// <c>Dispose()</c>——本类型接住的正是<b>本该</b>抵达 <c>ConsumerRegister</c> 的那一次停止，时机逐字相同。
/// 不用 <c>IHostApplicationLifetime.ApplicationStopping</c>：那条边沿<b>早于</b> hosted service 的
/// <c>StopAsync</c>，会把健康路径上的停止时机也一起提前。</para>
///
/// <para>⭐ <b>为什么在 <see cref="StartAsync"/> 里 <c>Register</c>，而不是只等自己那次 <c>Dispose()</c></b>：
/// <c>Bootstrapper</c> 的停机回调是<b>顺序</b>遍历 processor 列表的，排在本类型前面的
/// <c>CapProcessingServer.Dispose()</c> 最多会 <c>Wait(10s)</c>。只靠 <c>Dispose()</c> 就把停止信号的时机
/// 押在了注册顺序上。改成在 <c>stoppingToken</c> 上 <c>Register</c>（与 <c>ConsumerRegister.StartAsync:60</c>
/// 同一个写法）之后，取消回调按<b>后注册先执行</b>跑，本信号早于那次遍历，与 processor 顺序无关。
/// <c>Dispose()</c> 仍然保留同样的动作，作为容器释放这条路上的第二道。</para>
///
/// <para>⚠️ <b>健康路径上本类型不改变任何行为</b>：没恢复过时 <c>ConsumerRegister.Dispose()</c> 的 <c>Pulse()</c>
/// 已经取消了 listener 的 token，本信号只是随后又取消了一次同一批已取消的 linked token。</para>
/// </summary>
internal sealed class ConsumerListeningStopSignal : IProcessingServer
{
    private readonly CancellationTokenSource stopping = new();
    private CancellationTokenRegistration registration;
    private int stopped;

    /// <summary>CAP 最终停止的 token；<see cref="DecoratedConsumerClient"/> 把它 link 进 listener 的 token。</summary>
    public CancellationToken Token => stopping.Token;

    /// <summary>
    /// 把宿主停机 token 接到本信号上。<paramref name="stoppingToken"/> 就是 <c>Bootstrapper</c> 的
    /// <c>_cts.Token</c>——<c>StopAsync</c> 与 <c>Dispose</c> 都会取消它。
    /// </summary>
    public ValueTask StartAsync(CancellationToken stoppingToken)
    {
        // 重复 StartAsync（Bootstrapper 可被重复 Bootstrap）时先退掉上一次登记，避免堆积。
        registration.Dispose();
        registration = stoppingToken.Register(static state => ((ConsumerListeningStopSignal)state!).Dispose(), this);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// 幂等：<c>Bootstrapper</c> 的停机回调与容器释放都会调到这里，<c>StopAsync</c> 与 <c>Dispose</c> 也都会。
    ///
    /// <para>⚠️ <b>取消之后不 <c>Dispose()</c> 这个 CTS</b>，这是刻意的：
    /// <c>ConsumerRegister.ExecuteAsync</c> 可能与停机并发，仍会为新建的 client 调
    /// <c>CreateLinkedTokenSource(..., Token)</c>——源 CTS 若已释放，那一步会抛
    /// <see cref="ObjectDisposedException"/>。已取消但未释放的 <c>CancellationTokenSource</c> 不持有计时器或
    /// 非托管资源，随容器一起回收即可。验收里「不出现已释放 CTS 再次 Cancel」的那条同样由这里保证。</para>
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref stopped, 1) == 1)
        {
            return;
        }

        registration.Dispose();
        stopping.Cancel();
    }
}
