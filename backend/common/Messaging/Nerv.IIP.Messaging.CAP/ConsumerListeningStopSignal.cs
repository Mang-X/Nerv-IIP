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
/// <para><b>为什么补在这里、而不是包一层 <c>IConsumerRegister</c></b>：装饰一层只能看见<b>接口面</b>——
/// <c>IConsumerRegister</c> 连同它继承的 <c>IProcessingServer</c> 恰好四个成员：
/// <c>IsHealthy()</c> / <c>ReStartAsync(bool)</c> / <c>StartAsync(CancellationToken)</c> / <c>Dispose()</c>。
/// 恢复之后，<b>在这个接口射程内</b>这四个成员没有一个能停下当前这一代 listener：<c>Dispose()</c> 早退；<c>StartAsync</c> 会直接覆写
/// <c>_cts</c> 而<b>不取消</b>上一代，把上一代变成永远停不下来的孤儿；<c>ReStartAsync</c> 的 <c>Pulse()</c> 虽然能取消
/// 当前 CTS，但它紧接着又用裸 CTS 起了<b>新的一代</b>——每调一次就多一代孤儿，而且这正是验收明令禁止的
/// 「停止后重新启动」。<c>_isHealthy</c> 又只被 <c>ReStartAsync</c> 置回 <c>true</c>，所以也不能绕开它自己实现恢复。</para>
///
/// <para>⚠️ <b>这条否定的射程限定必须带上「接口面」三个字。</b>具体类 <c>ConsumerRegister</c> 上还有两个
/// <c>public</c> 成员不在接口上：<c>Pulse()</c> 与 <c>ExecuteAsync()</c>，而 <c>Pulse()</c> 在恢复之后
/// <b>恰恰能</b>「取消当前这一代且不起新一代」。结论不变的理由<b>不是</b>「不存在这样的成员」，而是
/// <c>ConsumerRegister</c> 是 <c>internal</c>、<c>AddCap</c> 交出来的是 <c>IConsumerRegister</c>：
/// 不反射就够不着 <c>Pulse()</c>，而票面明令不反射。⇒ <b>失效方向</b>：上游哪天把 <c>Pulse()</c> 提到接口上，
/// 这段推理就不再成立，届时应重新比较两种修法。</para>
///
/// <para>⇒ 停止信号只能在<b>交给 listener 的那个 token</b> 那一层补，也就是 <c>IConsumerClient.ListeningAsync</c>。</para>
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
    /// ⭐ <b>取消之后刻意<u>不</u> <c>Dispose()</c> 这个 <see cref="CancellationTokenSource"/>。</b>
    /// 这是本类型唯一一条需要被保护的决定，下面把理由、真实失效位点和变异读数一并写清。
    ///
    /// <para><b>为什么危险</b>：<c>ConsumerRegister.ExecuteAsync</c> 可能与停机并发，每为一个消费组建 client
    /// 就会读一次 <c>DecoratedConsumerClientFactory.CreateAsync</c> 里的 <c>stopSignal.Token</c>。
    /// ⚠️ <b>抛的是 <c>CancellationTokenSource.Token</c> 这个 property getter</b>，不是随后的
    /// <c>CreateLinkedTokenSource</c>。本席位在 .NET 10.0.302 上实测：对已 <c>Cancel()+Dispose()</c> 的源读
    /// <c>.Token</c> 抛 <see cref="ObjectDisposedException"/>；而拿一个<b>在 Dispose 之前就取出来</b>的 token 去
    /// <c>CreateLinkedTokenSource(other, token)</c> <b>不抛</b>，返回一个已取消的 linked。
    /// ⇒ 归因写成「linked 那一步会抛」会让照着查的人查错位置。</para>
    ///
    /// <para><b>不 Dispose 会不会泄漏</b>：不会。已 <c>Cancel()</c> 但未释放的 CTS 不持计时器，也不持非托管句柄
    /// （只有被读过的 <c>WaitHandle</c> 才分配事件，本类型从不读自己的），随容器一起回收即可。每次监听自己建的
    /// linked CTS 由 <c>DecoratedConsumerClient</c> 的 <c>using</c> 释放，同时摘掉它登记在本 token 上的回调，
    /// 所以也不会越积越多。</para>
    ///
    /// <para><b>幂等性由 <c>Cancel()</c> 自己提供，这里不再加守卫。</b><c>Bootstrapper</c> 的停机回调、
    /// <c>StopAsync</c> 与容器释放会多次调到本方法；实测（同上环境）对一个<b>未释放</b>的 CTS 连调三次
    /// <c>Cancel()</c> 不抛且回调只触发一次，<c>CancellationTokenRegistration.Dispose()</c> 同样可重复调用。</para>
    ///
    /// <para>⭐ <b>先前这里有一个 <c>Interlocked</c> 幂等守卫，已删——理由是它和「不 Dispose」互为对方的唯一防线。</b>
    /// 变异矩阵实测：N2（把 <c>stopping.Dispose()</c> 加回来）单独 <b>GREEN 95/95</b>、N5（删守卫）单独
    /// <b>GREEN 95/95</b>，只有 N2+N5 同时才 <b>RED 7</b>（全 <see cref="ObjectDisposedException"/>）。
    /// 在「不 Dispose」成立的前提下守卫是纯冗余，留着它只会让「有人好心把 <c>Dispose()</c> 加回来」这件事
    /// <b>静默全绿</b>。删掉之后加回 <c>Dispose()</c> 会被 <c>Repeated_stop_and_dispose_stay_idempotent</c>
    /// 当场判红 —— 一条决定配一条会红的断言，而不是两条决定互相遮蔽。</para>
    /// </summary>
    public void Dispose()
    {
        registration.Dispose();
        stopping.Cancel();
    }
}
