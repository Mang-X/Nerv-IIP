using DotNetCore.CAP;
using DotNetCore.CAP.Internal;
using DotNetCore.CAP.Messages;
using DotNetCore.CAP.Monitoring;
using DotNetCore.CAP.Persistence;
using DotNetCore.CAP.Processor;
using DotNetCore.CAP.Transport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Nerv.IIP.Testing;
using Xunit;

namespace Nerv.IIP.Messaging.CAP.Tests;

/// <summary>
/// #3249：<b>正常恢复之后</b>，宿主的最终停止必须仍然抵达当前这一代 listener。
///
/// <para><b>缺陷</b>（上游 <c>DotNetCore.CAP</c> <c>Internal/IConsumerRegister.Default.cs</c> 原文实读；
/// v10.0.1 / v10.0.2 / <c>master</c> 三个 ref 的该文件<b>逐字节相同</b>，上游未修）：
/// <c>StartAsync:59-60</c> 建 <c>CreateLinkedTokenSource(stoppingToken)</c> 并 <c>Token.Register(Dispose)</c>；
/// <c>ReStartAsync:79</c> 建的却是<b>裸</b> <c>new CancellationTokenSource()</c>，两件都不做，
/// 且 <c>ReStartAsync:73-84</c> 体内没有 <c>_disposed = 0</c>（全文件只有 <c>StartAsync:70</c> 有）。
/// ⇒ 恢复之后宿主停机既<b>取消不到</b>新 listener，<c>Bootstrapper</c> 停机回调里的
/// <c>ConsumerRegister.Dispose():88-89</c> 又因 <c>_disposed == 1</c> 早退。</para>
///
/// <para><b>为什么这些用例走真 <c>AddCap</c> / 真 <c>ConsumerRegister</c> / 真 <c>Bootstrapper</c></b>：
/// 缺陷全部住在上游那三个类型的私有状态迁移里。换成自建假 register，断言就只证明夹具自洽。
/// 这里只把<b>传输端</b>换成 <see cref="ProbeConsumerClient"/>（逐条复刻上游 <c>RedisConsumerClient.ListeningAsync</c>
/// 的形状：非 <c>async</c>、永不正常返回、同步阻塞、取消时同步抛），CAP 侧全部是生产实现。</para>
///
/// <para>⭐ <b>怎么排除「测试取消伪因果」</b>——四条，逐条可核：</para>
/// <list type="number">
/// <item><description><b>用例从不取消 listener 的 token。</b>唯一的停止动作是
/// <see cref="IHostedService.StopAsync"/>（CAP 的 <c>Bootstrapper</c> 就是 hosted service，宿主停机走的正是它）。
/// 用例手上根本没有 listener 那个 token 的引用——它是 <c>ConsumerRegister</c> 的私有 <c>_cts</c>。</description></item>
/// <item><description><b>恢复也不是用例发起的。</b>是生产的 <see cref="TransportCheckProcessor"/> 看到
/// <c>!IsHealthy()</c> 之后自己调 <c>ReStartAsync()</c>；用例只负责让 broker 掉线，并让那个处理器跑一轮。
/// 全文件<b>零处</b>调用 <c>ReStartAsync</c>。</description></item>
/// <item><description><b>停止信号没有接到 ACK 上。</b><c>CommitAsync</c> / <c>RejectAsync</c> 在
/// <see cref="ProbeConsumerClient"/> 与 <see cref="DecoratedConsumerClient"/> 两侧都是纯转发，
/// 不读任何 token——首轮那种「把 listener token 绑到 ACK」的伪因果在构造上不存在。</description></item>
/// <item><description><b>收断言的边沿全部由被测代码走到。</b>
/// <see cref="ProbeConsumerClient.ListeningExited"/> 来自阻塞循环自己的 <c>finally</c>，
/// <see cref="ProbeConsumerClient.Disposed"/> 来自 <c>ConsumerRegister.ExecuteAsync</c> 的 <c>await using</c>；
/// 夹具不会主动置位它们。</description></item>
/// </list>
///
/// <para>⚠️ <b>上面四条的适用范围是走 <see cref="CapProbeHost"/> 的那组真宿主用例</b>。那组里由用例发出的取消
/// <b>共三处</b>，都与被观察对象隔开、且都不承重：
/// ① <see cref="CapProbeHost.TickTransportCheckAsync"/> 里 <c>TransportCheckProcessor</c> 自己那次 30 秒
/// <c>WaitAsync</c> 用的 <see cref="ProcessingContext"/> token——恢复完成<b>之后</b>才取消；
/// ② <see cref="CapProbeHost.TickTransportCheckAfterStopAsync"/> 的 <c>finally</c> 里同型的那一次——
/// 断言全部在它之后，且那一轮 <c>ReStartAsync</c> 在上游 <c>Pulse()</c> 就抛、根本走不到建 client；
/// ③ <see cref="ProbeConsumerClientFactory.ReleaseEverything"/> 的收尾兜底——只在
/// <see cref="CapProbeHost.DisposeAsync"/> 里、所有断言跑完之后。
/// （先前这段写的是「两处」，漏了 ②；量词按字面不成立，已更正。）</para>
///
/// <para>本文件另有两条<b>直接构造装饰器</b>的单元用例
/// （<see cref="Stop_signal_alone_ends_listening_even_when_the_caller_token_stays_live"/> 与
/// <see cref="Caller_token_still_ends_listening_when_a_stop_signal_is_present"/>），它们<b>确实</b>由用例取消 token
/// ——但那两条的被测命题就是「这次取消传不传得到 inner」，取消是<b>自变量</b>而不是伪因果；
/// 它们也完全不经过 <c>ConsumerRegister</c> / <c>Bootstrapper</c>。</para>
/// </summary>
public sealed class ConsumerRecoveryFinalStopTests
{
    /// <summary>有界等待上限：修好之后这些边沿都是毫秒级；只有缺陷体才会走到上限并判红。</summary>
    private static readonly TimeSpan Bounded = TimeSpan.FromSeconds(15);

    /// <summary>
    /// ⭐ <b>本票的 RED</b>：一次<b>正常恢复</b>之后停宿主，恢复出来的那一代 listener 必须退出并释放 client。
    ///
    /// <para>恢复既不是 <c>force</c> 出来的、也不是用例调出来的：先让 broker 掉线，
    /// <c>ConsumerRegister</c> 的消费线程捕获 <see cref="BrokerConnectionException"/> 把 <c>_isHealthy</c> 置 false；
    /// 随后由<b>生产的</b> <see cref="TransportCheckProcessor"/>（上游 <c>IProcessor.TransportCheck.cs:32-36</c>）
    /// 自己调 <c>ReStartAsync()</c>。也就是说 <c>ReStartAsync:74</c> 的 <c>!IsHealthy()</c> 这一支
    /// 是被<b>真的</b>不健康满足的，而扣扳机的是生产代码。</para>
    /// </summary>
    [Fact]
    public async Task Final_stop_reaches_the_listener_recovered_by_a_normal_restart()
    {
        await using var host = await CapProbeHost.StartAsync();

        var firstGeneration = await host.WaitForListeningClientAsync();
        await host.RecoverAsync();
        var recoveredGeneration = await host.WaitForListeningClientAsync();

        Assert.NotSame(firstGeneration, recoveredGeneration);
        // 前提断言：没有这条，下面那句「停机之后它退出了」可能只是因为它从来没跑起来。
        Assert.True(recoveredGeneration.ListeningEntered.Task.IsCompletedSuccessfully);
        Assert.False(recoveredGeneration.ListeningExited.Task.IsCompleted, host.Dump());

        await host.StopAsync();

        await recoveredGeneration.ListeningExited.Task.WaitAsync(Bounded);
        await recoveredGeneration.Disposed.Task.WaitAsync(Bounded);
        // 旧的那一代同样不许留着：它是被 broker 掉线结束的，这里钉住它确实已经收尾。
        await firstGeneration.ListeningExited.Task.WaitAsync(Bounded);
        await firstGeneration.Disposed.Task.WaitAsync(Bounded);
    }

    /// <summary>
    /// 验收：<b>再次</b>正常恢复之后停止。每一代恢复都会在上游留下一个裸 CTS，
    /// 所以「修一代」与「每一代都修」不是同一件事。
    /// </summary>
    [Fact]
    public async Task Final_stop_reaches_the_listener_recovered_by_a_second_normal_restart()
    {
        await using var host = await CapProbeHost.StartAsync();

        await host.WaitForListeningClientAsync();
        await host.RecoverAsync();
        await host.WaitForListeningClientAsync();
        await host.RecoverAsync();
        var second = await host.WaitForListeningClientAsync();

        Assert.Equal(3, host.ListeningClients.Count);
        Assert.False(second.ListeningExited.Task.IsCompleted);

        await host.StopAsync();

        await host.AssertEveryCreatedClientSettledAsync(Bounded);
    }

    /// <summary>
    /// 验收：<b>没有恢复</b>的正常停止必须照旧成立。
    ///
    /// <para>这一条同时是本组的 <b>CONTROL</b>：它走的是上游<b>没有</b>缺陷的那条路
    /// （<c>ConsumerRegister.Dispose()</c> 的 <c>Pulse()</c> 取消 <c>StartAsync</c> 建的 linked CTS），
    /// 所以把 #3249 的修复整个撤掉它也应当<b>照绿</b>。它红了说明修复弄坏了健康路径，而不是说明缺陷存在。</para>
    /// </summary>
    [Fact]
    public async Task Final_stop_reaches_the_listener_when_no_recovery_ever_happened()
    {
        // 这一条刻意把 CAP 自带的整套 processor（含 CapProcessingServer，它的 Dispose 会 Wait 到 10 秒）
        // 都留在场：停止信号的时机不能依赖 IProcessingServer 的注册顺序。
        await using var host = await CapProbeHost.StartAsync(withCapProcessors: true);

        var only = await host.WaitForListeningClientAsync();
        Assert.False(only.ListeningExited.Task.IsCompleted);

        await host.StopAsync();

        await only.ListeningExited.Task.WaitAsync(Bounded);
        await only.Disposed.Task.WaitAsync(Bounded);
    }

    /// <summary>
    /// 验收：<b>停止与恢复交错</b>。<c>TransportCheckProcessor</c> 每 30 秒一轮，与停机撞车是真实可达的时序；
    /// 停机之后再进来的那一次恢复<b>不许</b>留下活的 listener（验收明写「不出现停止后重新启动」）。
    ///
    /// <para>断言形态是「监听过的代数没有增加 + 每一个被创建的 client 都收尾了」，两条都是正向可观测量；
    /// 不写成「新的一代从未进入监听」——那要靠等一段时间什么都没发生来判定，是时间断言。</para>
    /// </summary>
    [Fact]
    public async Task A_restart_racing_in_after_the_final_stop_leaves_no_live_listener()
    {
        await using var host = await CapProbeHost.StartAsync();

        await host.WaitForListeningClientAsync();

        // 真实可达的交错：传输先掉线（register 变不健康），30 秒那一轮检查还没落地，宿主就开始停机；
        // 检查落地时停机已经完成，而 TransportCheckProcessor 照样会调 ReStartAsync()。
        await host.DropBrokerAsync();
        await host.StopAsync();
        await host.TickTransportCheckAfterStopAsync();

        // 验收明写的「不出现停止后重新启动」：停机之后那一轮检查不许产生新的一代 listener。
        Assert.Single(host.ListeningClients);
        await host.AssertEveryCreatedClientSettledAsync(Bounded);
    }

    /// <summary>
    /// ⭐ 停止信号的<b>时机</b>不许排在别的 processor 后面。
    ///
    /// <para><c>Bootstrapper</c> 的停机回调是<b>顺序</b>遍历 <see cref="IProcessingServer"/> 列表逐个
    /// <c>Dispose()</c> 的，而排在前面的 <c>CapProcessingServer.Dispose()</c> 最多会
    /// <c>_compositeTask.Wait(10s)</c>。只靠「轮到我时我取消」，恢复出来的 listener 就会在宿主已经决定停机之后
    /// 继续消费最多十秒。所以停止信号是在 <c>StartAsync</c> 里登记到 <c>stoppingToken</c> 上的
    /// （与 <c>ConsumerRegister.StartAsync:60</c> 同一个写法），取消回调<b>后注册先执行</b>，早于那次遍历。</para>
    ///
    /// <para>用例在停止信号<b>前面</b>插一个 <see cref="StallingProcessingServer"/>，它的 <c>Dispose()</c> 会一直卡住。
    /// 断言是：遍历还卡在它身上的时候，恢复出来的 listener <b>已经</b>退出了。</para>
    /// </summary>
    [Fact]
    public async Task Final_stop_reaches_the_listener_before_an_earlier_processor_finishes_disposing()
    {
        var staller = new StallingProcessingServer();
        await using var host = await CapProbeHost.StartAsync(processorBeforeStopSignal: staller);

        await host.WaitForListeningClientAsync();
        await host.RecoverAsync();
        var recovered = await host.WaitForListeningClientAsync();
        Assert.False(recovered.ListeningExited.Task.IsCompleted);

        // StopAsync 会同步卡在 staller.Dispose() 里，所以不能直接 await。
        var stopping = Task.Run(() => host.StopAsync());
        await staller.Entered.Task.WaitAsync(Bounded);

        // ⭐ 承重的一条：遍历还没轮到停止信号，listener 就必须已经退出了。
        await recovered.ListeningExited.Task.WaitAsync(Bounded);
        Assert.False(stopping.IsCompleted, host.Dump());

        staller.Release();
        await stopping.WaitAsync(Bounded);
    }

    /// <summary>
    /// 验收：重复 <c>Dispose</c> 的幂等性，且不出现「已释放 CTS 再次 <c>Cancel</c>」。
    ///
    /// <para><c>Bootstrapper</c> 的停机回调、<c>StopAsync</c>、容器释放会多次走到同一批
    /// <see cref="IProcessingServer.Dispose"/>；<see cref="ConsumerListeningStopSignal"/> 因此必须自己幂等。</para>
    /// </summary>
    [Fact]
    public async Task Repeated_stop_and_dispose_stay_idempotent()
    {
        var host = await CapProbeHost.StartAsync();

        await host.WaitForListeningClientAsync();

        await host.StopAsync();
        await host.StopAsync();

        await host.AssertEveryCreatedClientSettledAsync(Bounded);

        // 容器释放会第三次走到同一批 IProcessingServer.Dispose()。
        await host.DisposeAsync();
        await host.DisposeAsync();
    }

    /// <summary>
    /// 停止信号的注册形态：它必须真的挂进 <see cref="IProcessingServer"/> 这一列，
    /// 否则 <c>Bootstrapper</c> 停机时根本不会调到它的 <c>Dispose()</c>——而所有门禁照绿。
    /// </summary>
    [Fact]
    public void RedisTransport_registers_the_stop_signal_as_a_processing_server()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Messaging:Provider"] = "Redis",
            ["Messaging:Redis:ConnectionString"] = "localhost:6379,abortConnect=false",
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCap(options => options.UseConfiguredTransport(configuration));
        // 解析 IProcessingServer 会一并构造 Dispatcher，后者要 IDataStorage。
        services.AddSingleton(new CapStorageMarkerService("probe-3249"));
        services.AddSingleton<IStorageInitializer, IdleStorageInitializer>();
        services.AddSingleton<IDataStorage, IdleDataStorage>();

        using var provider = services.BuildServiceProvider();
        var signal = provider.GetRequiredService<ConsumerListeningStopSignal>();

        Assert.Contains(provider.GetServices<IProcessingServer>(), server => ReferenceEquals(server, signal));
        // 同一个单例，否则 client 拿到的 token 与 Bootstrapper 取消的那个不是同一个。
        Assert.Same(signal, provider.GetRequiredService<ConsumerListeningStopSignal>());
    }

    /// <summary>
    /// ⚠️ <b>射程边界，如实钉住</b>：停止信号随 <c>ConsumerClientDecorationExtension</c> 注册，而那个 extension
    /// 只挂在 <b>Redis</b> transport 上。RabbitMQ / InMemory 宿主既没有 client 装饰层、也没有这个信号，
    /// 因此<b>本票没有</b>替它们修掉同一个上游缺陷。
    ///
    /// <para>这条断言的作用是让边界显式：将来若有人给非 Redis transport 也挂上装饰层，这条会红，
    /// 逼着他同时处理停止信号，而不是让「只有 Redis 修了」这件事继续静默。</para>
    /// </summary>
    [Theory]
    [InlineData("InMemory")]
    [InlineData("RabbitMQ")]
    public void NonRedisTransports_HaveNoStopSignal(string provider)
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

        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(ConsumerListeningStopSignal));
    }

    /// <summary>
    /// 单元一层的鉴别力：停止信号（<b>不是</b>调用方 token）被取消时，listener 必须退出。
    /// 上面那些真宿主用例证明信号被接到了正确的边沿上，这一条证明信号确实传得进 inner。
    /// </summary>
    [Fact]
    public async Task Stop_signal_alone_ends_listening_even_when_the_caller_token_stays_live()
    {
        var inner = new ProbeConsumerClient();
        using var stop = new CancellationTokenSource();
        using var callerToken = new CancellationTokenSource();
        var client = new DecoratedConsumerClient(inner, stop.Token);

        var listening = await StartListeningOffCallerThreadAsync(client, callerToken.Token);
        await inner.ListeningEntered.Task.WaitAsync(Bounded);
        Assert.False(listening.IsCompleted);

        await stop.CancelAsync();

        await inner.ListeningExited.Task.WaitAsync(Bounded);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => listening.WaitAsync(Bounded));
        Assert.False(callerToken.IsCancellationRequested);
    }

    /// <summary>
    /// 反面：调用方 token 仍然独立有效——停止信号存在时不能把它吞掉。
    /// </summary>
    [Fact]
    public async Task Caller_token_still_ends_listening_when_a_stop_signal_is_present()
    {
        var inner = new ProbeConsumerClient();
        using var stop = new CancellationTokenSource();
        using var callerToken = new CancellationTokenSource();
        var client = new DecoratedConsumerClient(inner, stop.Token);

        var listening = await StartListeningOffCallerThreadAsync(client, callerToken.Token);
        await inner.ListeningEntered.Task.WaitAsync(Bounded);

        await callerToken.CancelAsync();

        await inner.ListeningExited.Task.WaitAsync(Bounded);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => listening.WaitAsync(Bounded));
        Assert.False(stop.IsCancellationRequested);
    }

    /// <summary>
    /// 从<b>另一条</b>线程发起 <c>ListeningAsync</c> 并有界地取回它返回的 Task。
    ///
    /// <para>⚠️ <b>不要在用例线程上直接 <c>client.ListeningAsync(...)</c>。</b>
    /// <see cref="ProbeConsumerClient.ListeningAsync"/> 复刻的是上游那种<b>同步阻塞、永不正常返回</b>的形态；
    /// 一旦有人把装饰器的专用线程卸载改掉，直接调用会<b>整个挂死</b>而不是判红——本席位实测过这一格，
    /// 整个 suite 挂了 9 分钟，在 CI 上表现为 job 超时并带走 <c>if:always</c> 的证据。
    /// <c>ListeningThreadOffloadTests</c> 文件开头写明的就是这条，这里照同一姿势办。</para>
    /// </summary>
    private static async Task<Task> StartListeningOffCallerThreadAsync(
        DecoratedConsumerClient client,
        CancellationToken cancellationToken)
    {
        var returned = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                returned.TrySetResult(client.ListeningAsync(TimeSpan.FromMilliseconds(20), cancellationToken));
            }
            catch (Exception exception)
            {
                returned.TrySetException(exception);
            }
        });

        return await returned.Task.WaitAsync(Bounded);
    }

    /// <summary>
    /// 真 CAP 宿主：真 <c>AddCap</c>、真 <c>ConsumerRegister</c>、真 <c>Bootstrapper</c>（以 hosted service 形态
    /// 启停），只把传输端换成 <see cref="ProbeConsumerClient"/>、存储端换成不落盘的桩。
    /// </summary>
    private sealed class CapProbeHost : IAsyncDisposable
    {
        private readonly ServiceProvider provider;
        private readonly IHostedService bootstrapper;
        private readonly ProbeConsumerClientFactory transport;
        private int disposed;

        private CapProbeHost(ServiceProvider provider, IHostedService bootstrapper, ProbeConsumerClientFactory transport)
        {
            this.provider = provider;
            this.bootstrapper = bootstrapper;
            this.transport = transport;
        }

        /// <summary>所有被创建过的 client（含 <c>ExecuteAsync</c> 顶部那个只用来 fetch topics、随即释放的）。</summary>
        public IReadOnlyList<ProbeConsumerClient> CreatedClients => transport.Created;

        /// <summary>用例失败时的现场：每个被创建过的 client 的三条边沿与结束原因。</summary>
        public string Dump() => string.Join(" | ", transport.Created.Select((c, i) =>
            $"#{i} entered={c.ListeningEntered.Task.IsCompleted} exited={c.ListeningExited.Task.IsCompleted} disposed={c.Disposed.Task.IsCompleted} outcome={c.Outcome}"));

        public IReadOnlyList<ProbeConsumerClient> ListeningClients =>
            transport.Created.Where(client => client.ListeningEntered.Task.IsCompleted).ToArray();

        /// <param name="withCapProcessors">
        /// 是否保留 CAP 自带的 <see cref="CapProcessingServer"/>（它托管 <see cref="TransportCheckProcessor"/> 等）。
        ///
        /// <para>⚠️ 默认<b>不保留</b>，理由是可确定性：<c>TransportCheckProcessor</c> 的第一轮就在宿主启动时跑，
        /// 之后每 30 秒一轮。留着它，「broker 掉线」与「哪一轮 tick 看到不健康」之间就有一个 0～30 秒的窗口，
        /// 恢复会在用例不掌握的时刻自己发生（本组第一版实测：恢复在 <c>DropBroker</c> 之后 4 毫秒就由它完成了）。
        /// 把它摘掉之后，恢复仍然由<b>同一个生产处理器</b>发起——只是由用例自己 tick 一次，见
        /// <see cref="RecoverAsync"/>。</para>
        ///
        /// <para>需要「整套 processor 都在场」的那条用例显式传 <c>true</c>。</para>
        /// </param>
        /// <param name="processorBeforeStopSignal">
        /// 插在停止信号<b>之前</b>的额外 <see cref="IProcessingServer"/>，用来观察停机遍历的时机。
        /// </param>
        public static async Task<CapProbeHost> StartAsync(
            bool withCapProcessors = false,
            IProcessingServer? processorBeforeStopSignal = null)
        {
            var transport = new ProbeConsumerClientFactory();
            var settings = new Dictionary<string, string?>
            {
                ["Messaging:Provider"] = "Redis",
                ["Messaging:Redis:ConnectionString"] = "localhost:6379,abortConnect=false",
            };
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddCap(options =>
            {
                options.UseConfiguredTransport(configuration);
                // 一轮 poll 20ms：ProbeConsumerClient 的阻塞循环用它做 WaitAny 超时，与上游把 _pollingDelay
                // 交给 RedisConsumerClient 的形态一致。这里不承担任何断言。
                options.ConsumerThreadCount = 1;
            });
            // 存储端：本票不碰持久化，桩只要让 Bootstrapper 的 CheckRequirement 与各 processor 安静空转。
            services.AddSingleton(new CapStorageMarkerService("probe-3249"));
            services.AddSingleton<IStorageInitializer, IdleStorageInitializer>();
            services.AddSingleton<IDataStorage, IdleDataStorage>();
            // 订阅者：没有它 MethodMatcherCache 会解析出零个消费组，ExecuteAsync 一个 listener 都不会起。
            services.AddScoped<ProbeSubscriber>();
            // 传输端：装饰器仍是生产那个 DecoratedConsumerClientFactory，只把它包住的 inner 换成探针。
            services.Replace(ServiceDescriptor.Singleton(new TransportConsumerClientFactory(transport)));

            if (processorBeforeStopSignal is not null)
            {
                // 停止信号是 AddCap 的 extension 阶段最后追加的那条 IProcessingServer；插在它前面即可。
                var stopSignalIndex = services
                    .Select((descriptor, index) => (descriptor, index))
                    .Last(entry => entry.descriptor.ServiceType == typeof(IProcessingServer))
                    .index;
                services.Insert(stopSignalIndex, ServiceDescriptor.Singleton(processorBeforeStopSignal));
            }

            if (!withCapProcessors)
            {
                var capProcessors = services.Single(descriptor =>
                    descriptor.ServiceType == typeof(IProcessingServer)
                    && descriptor.ImplementationType == typeof(CapProcessingServer));
                services.Remove(capProcessors);
            }

            var provider = services.BuildServiceProvider();
            var bootstrapper = provider.GetServices<IHostedService>().OfType<BackgroundService>().Single();
            var host = new CapProbeHost(provider, bootstrapper, transport);

            await bootstrapper.StartAsync(CancellationToken.None);
            // ⚠️ BackgroundService.StartAsync 只是把 ExecuteAsync 起起来，并不等 BootstrapAsync 跑完。
            // 不等的话会撞上 CapTestHost 记录的那条<b>另一个</b>上游竞态：容器释放时第二次走到
            // ConsumerRegister.Dispose()，而在飞的 StartAsync 已经把 _disposed 复位成 0，于是 Pulse() 对一个
            // 已释放的 CTS 再 Cancel，抛 ObjectDisposedException。本席位在 Release 配置下实测命中过一次
            // （Debug 下同一份代码连续多跑未命中）。那条竞态不在 #3249 射程内，用仓库既有的共享 helper 关掉。
            await CapTestHost.WaitForCapBootstrapAsync(provider);
            return host;
        }

        /// <summary>等到<b>新的一代</b> listener 真的进了 <c>ListeningAsync</c>。</summary>
        public async Task<ProbeConsumerClient> WaitForListeningClientAsync()
        {
            var client = await transport.NextListeningClient.Task.WaitAsync(Bounded);
            transport.ArmNextListeningClient();
            return client;
        }

        /// <summary>
        /// 走一次<b>真实</b>的正常恢复，而且恢复<b>不是用例发起的</b>。
        ///
        /// <para>① 让 broker 掉线：当前这一代消费线程在 <c>ConsumerRegister.ExecuteAsync</c> 里捕获
        /// <see cref="BrokerConnectionException"/> ⇒ <c>_isHealthy = false</c>；
        /// ② 让<b>生产的</b> <see cref="TransportCheckProcessor"/> 跑一轮——是它看到 <c>!IsHealthy()</c>
        /// 之后自己调 <c>ReStartAsync()</c>（上游 <c>Processor/IProcessor.TransportCheck.cs:32-36</c>）。
        /// 用例<b>一次都没有</b>调过 <c>ReStartAsync</c>。</para>
        ///
        /// <para>⚠️ 末尾取消的是<b>处理器自己那次 30 秒 <c>WaitAsync</c></b>（<c>TransportCheck:43</c>），
        /// 用的是处理器专属的 <see cref="ProcessingContext"/> token；它与任何 listener 的 token 没有关系，
        /// 而且发生在恢复<b>已经完成之后</b>。这条是本组唯一一处由用例发出的取消，刻意与被观察的对象隔开。</para>
        /// </summary>
        public async Task RecoverAsync()
        {
            await DropBrokerAsync();
            await TickTransportCheckAsync();
        }

        /// <summary>
        /// 第 ① 步：broker 掉线，等 <c>ConsumerRegister</c> 自己把 <c>_isHealthy</c> 翻成 false。
        /// 翻转来自上游 <c>ExecuteAsync</c> 里的 <c>catch (BrokerConnectionException)</c>，不是夹具置位。
        /// </summary>
        public async Task DropBrokerAsync()
        {
            var register = provider.GetRequiredService<IConsumerRegister>();

            transport.DropBroker();
            await WaitUntilAsync(() => !register.IsHealthy(), Dump);
        }

        /// <summary>
        /// 第 ② 步：让<b>生产的</b> <see cref="TransportCheckProcessor"/> 跑一轮。
        /// </summary>
        public async Task TickTransportCheckAsync()
        {
            var register = provider.GetRequiredService<IConsumerRegister>();
            var transportCheck = provider.GetRequiredService<TransportCheckProcessor>();

            Assert.False(register.IsHealthy());

            using var tick = new CancellationTokenSource();
            using var context = new ProcessingContext(provider, tick.Token);
            var processing = transportCheck.ProcessAsync(context);

            await WaitUntilAsync(register.IsHealthy, Dump);

            await tick.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);
        }

        /// <summary>
        /// 停机<b>之后</b>才落地的那一轮检查。这里<b>不</b>断言它的结果，只把它跑完——本票拥有的不变量是
        /// 「停机之后没有活 listener、也没有新的一代」，由调用方断言。
        ///
        /// <para>ⓘ 实测记录（v10.0.1，本席位本机）：这一轮会以 <see cref="ObjectDisposedException"/> 失败——
        /// <c>ConsumerRegister.Dispose()</c> 的 <c>Pulse()</c> 已经把 <c>_cts</c> 释放掉，
        /// <c>ReStartAsync:77</c> 的 <c>Pulse()</c> 再 <c>Cancel()</c> 就抛。<b>那是 #3249 射程之外的另一处上游行为</b>
        /// （与本票评论里那次独立目击同一签名），生产里由 <c>InfiniteRetryProcessor</c> 吞掉并每 2 秒重试。
        /// 这里刻意<b>不把它写成断言</b>：把上游缺陷固化成契约，会让上游修好之后这条用例反而变红。</para>
        /// </summary>
        public async Task TickTransportCheckAfterStopAsync()
        {
            var transportCheck = provider.GetRequiredService<TransportCheckProcessor>();

            using var tick = new CancellationTokenSource();
            using var context = new ProcessingContext(provider, tick.Token);
            var processing = transportCheck.ProcessAsync(context);

            try
            {
                // 两种可能都放行：这一轮直接失败，或者走到 TransportCheck:43 的 30 秒等待（届时超时）。
                await processing.WaitAsync(Bounded);
            }
            catch (Exception)
            {
                // 结果不承重，见上文。
            }
            finally
            {
                await tick.CancelAsync();
            }
        }

        /// <summary>唯一的停止动作：CAP 的 <c>Bootstrapper</c> 就是 hosted service，宿主停机走的正是这里。</summary>
        public Task StopAsync() => bootstrapper.StopAsync(CancellationToken.None);

        /// <summary>
        /// 收尾合同，两个边沿分开断：<b>监听退出</b>（<c>ListeningAsync</c> 的 <c>finally</c> 跑到）与
        /// <b>连接释放</b>（<c>ExecuteAsync</c> 的 <c>await using</c> 释放了 client）。
        /// </summary>
        public async Task AssertEveryCreatedClientSettledAsync(TimeSpan budget)
        {
            var clients = CreatedClients.ToArray();
            Assert.NotEmpty(clients);

            foreach (var client in clients)
            {
                await client.Disposed.Task.WaitAsync(budget);
                if (client.ListeningEntered.Task.IsCompleted)
                {
                    await client.ListeningExited.Task.WaitAsync(budget);
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 1)
            {
                return;
            }

            try
            {
                await bootstrapper.StopAsync(CancellationToken.None);
            }
            finally
            {
                await provider.DisposeAsync();
                transport.ReleaseEverything();
            }
        }

        /// <summary>
        /// 有界地等一个由<b>被测代码</b>驱动的谓词翻转。翻转本身来自 <c>ConsumerRegister</c> 自己的
        /// <c>_isHealthy</c>（上游 <c>ExecuteAsync</c> 的 <c>catch (BrokerConnectionException)</c> 与
        /// <c>ReStartAsync:80</c>），用例只是观察它。
        ///
        /// <para>用共享的 <see cref="Eventually"/> 原语而不是自建 sleep 循环——后者是
        /// <c>docs/governance/testing/determinism.md</c> 明令禁止的形态，也会被
        /// <c>scripts/check-backend-test-determinism.ps1</c> 判红。</para>
        /// </summary>
        private static ValueTask WaitUntilAsync(Func<bool> condition, Func<string> dump) =>
            Eventually.AssertAsync(
                condition: "CAP ConsumerRegister 的健康状态按预期翻转",
                assertion: _ =>
                {
                    Assert.True(condition(), dump());
                    return Task.CompletedTask;
                },
                options: new EventuallyOptions(Bounded, TimeSpan.FromMilliseconds(10), []));
    }

    private sealed class ProbeConsumerClientFactory : IConsumerClientFactory
    {
        private readonly List<ProbeConsumerClient> created = [];
        private readonly CancellationTokenSource teardown = new();
        private readonly Lock gate = new();

        public TaskCompletionSource<ProbeConsumerClient> NextListeningClient { get; private set; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<ProbeConsumerClient> Created
        {
            get
            {
                lock (gate)
                {
                    return created.ToArray();
                }
            }
        }

        public Task<IConsumerClient> CreateAsync(string groupName, byte groupConcurrent)
        {
            var client = new ProbeConsumerClient(teardown.Token, OnListeningEntered);
            lock (gate)
            {
                created.Add(client);
            }

            return Task.FromResult<IConsumerClient>(client);
        }

        public void ArmNextListeningClient() =>
            NextListeningClient = new TaskCompletionSource<ProbeConsumerClient>(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// broker 掉线：只打<b>已经存在</b>的连接，之后新建的连接是好的——真实的 broker 抖动就是这个形状。
        /// 用「逐连接一次性」而不是一个全局开关，是为了不给 drop / restore 留下时序窗口：
        /// 全局开关下，恢复出来的那一代有可能在开关复位之前就进了循环并被同一次掉线打死。
        /// </summary>
        public void DropBroker()
        {
            foreach (var client in Created)
            {
                client.SignalBrokerDrop();
            }
        }

        /// <summary>
        /// 仅用于用例收尾，防止缺陷体留下的阻塞线程堆积。⚠️ 它只在 <see cref="CapProbeHost.DisposeAsync"/> 里、
        /// <b>所有断言都已经跑完之后</b>才被调用，构造上不可能让任何一条断言变绿。
        /// </summary>
        public void ReleaseEverything() => teardown.Cancel();

        private void OnListeningEntered(ProbeConsumerClient client) => NextListeningClient.TrySetResult(client);
    }

    /// <summary>
    /// 逐条复刻上游 <c>RedisConsumerClient.ListeningAsync</c>：非 <c>async</c>、永不正常返回、同步阻塞调用线程、
    /// 取消时<b>同步抛出</b> <see cref="OperationCanceledException"/>。夹具写成 <c>async</c> 就不再测真实缺陷了。
    /// </summary>
    private sealed class ProbeConsumerClient : IConsumerClient
    {
        private readonly ManualResetEventSlim brokerDown = new(initialState: false);
        private readonly CancellationToken teardown;
        private readonly Action<ProbeConsumerClient>? onEntered;

        public ProbeConsumerClient()
            : this(teardown: CancellationToken.None, onEntered: null)
        {
        }

        public ProbeConsumerClient(CancellationToken teardown, Action<ProbeConsumerClient>? onEntered)
        {
            this.teardown = teardown;
            this.onEntered = onEntered;
        }

        public TaskCompletionSource ListeningEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ListeningExited { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Outcome { get; private set; } = "listening";

        public BrokerAddress BrokerAddress => new("probe", "endpoint-3249");

        public Func<TransportMessage, object?, Task>? OnMessageCallback { get; set; }

        public Action<LogMessageEventArgs>? OnLogCallback { get; set; }

        public void SignalBrokerDrop() => brokerDown.Set();

        public Task<ICollection<string>> FetchTopicsAsync(IEnumerable<string> topicNames) =>
            Task.FromResult<ICollection<string>>(topicNames.ToArray());

        public Task SubscribeAsync(IEnumerable<string> topics) => Task.CompletedTask;

        public Task ListeningAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            ListeningEntered.TrySetResult();
            onEntered?.Invoke(this);

            var handles = new[] { cancellationToken.WaitHandle, teardown.WaitHandle, brokerDown.WaitHandle };
            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (brokerDown.IsSet)
                    {
                        // broker 掉线：上游 RedisConsumerClient 在这种情况下抛的就是 BrokerConnectionException，
                        // ConsumerRegister 的消费线程捕获后把 _isHealthy 置 false —— 这是进入恢复的真实入口。
                        throw new BrokerConnectionException(new IOException("probe broker drop 3249"));
                    }

                    if (teardown.IsCancellationRequested)
                    {
                        // 用例收尾的兜底出口（见 ProbeConsumerClientFactory.ReleaseEverything）。
                        throw new OperationCanceledException(teardown);
                    }

                    WaitHandle.WaitAny(handles, timeout);
                }
            }
            catch (Exception exception)
            {
                Outcome = exception.GetType().Name;
                throw;
            }
            finally
            {
                ListeningExited.TrySetResult();
            }
        }

        public Task CommitAsync(object? sender) => Task.CompletedTask;

        public Task RejectAsync(object? sender) => Task.CompletedTask;

        public ValueTask DisposeAsync()
        {
            Disposed.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// <c>Dispose()</c> 会一直卡住的 processor，用来把停机遍历钉在一个可观测的位置上。
    /// </summary>
    private sealed class StallingProcessingServer : IProcessingServer
    {
        private readonly ManualResetEventSlim release = new(initialState: false);

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask StartAsync(CancellationToken stoppingToken) => ValueTask.CompletedTask;

        public void Release() => release.Set();

        /// <summary>
        /// ⚠️ 有界（30 秒）：用例失败时不能把整条停机链挂死。上界远大于本组 15 秒的断言预算，
        /// 构造上不可能让任何一条断言因为它超时而变绿。
        /// </summary>
        public void Dispose()
        {
            Entered.TrySetResult();
            release.Wait(TimeSpan.FromSeconds(30));
        }
    }

    /// <summary>让 CAP 的 selector 解析出一个消费组；本票不投递任何消息，这个方法体永远不会被调用。</summary>
    private sealed class ProbeSubscriber : ICapSubscribe
    {
        [CapSubscribe("nerv.iip.probe.3249")]
        public Task HandleAsync(string payload) => Task.CompletedTask;
    }

    private sealed class IdleStorageInitializer : IStorageInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public string GetPublishedTableName() => "probe_published";

        public string GetReceivedTableName() => "probe_received";

        public string GetLockTableName() => "probe_lock";
    }

    /// <summary>
    /// 不落盘的存储桩：只让 CAP 自带的 collector/retry/delayed processor 安静空转。
    /// 本票不覆盖持久化行为，这里刻意不做任何记账。
    /// </summary>
    private sealed class IdleDataStorage : IDataStorage
    {
        public Task<bool> AcquireLockAsync(string key, TimeSpan ttl, string instance, CancellationToken token = default) =>
            Task.FromResult(false);

        public Task ReleaseLockAsync(string key, string instance, CancellationToken token = default) => Task.CompletedTask;

        public Task RenewLockAsync(string key, TimeSpan ttl, string instance, CancellationToken token = default) =>
            Task.CompletedTask;

        public Task ChangePublishStateToDelayedAsync(string[] ids) => Task.CompletedTask;

        public Task ChangePublishStateAsync(MediumMessage message, StatusName state, object? transaction = null) =>
            Task.CompletedTask;

        public Task ChangeReceiveStateAsync(MediumMessage message, StatusName state) => Task.CompletedTask;

        public Task<MediumMessage> StoreMessageAsync(string name, Message content, object? transaction = null) =>
            throw new NotSupportedException("#3249 的探针宿主不投递消息。");

        public Task StoreReceivedExceptionMessageAsync(string name, string group, string content) =>
            throw new NotSupportedException("#3249 的探针宿主不投递消息。");

        public Task<MediumMessage> StoreReceivedMessageAsync(string name, string group, Message content) =>
            throw new NotSupportedException("#3249 的探针宿主不投递消息。");

        public Task<int> DeleteExpiresAsync(string table, DateTime timeout, int batchCount = 1000, CancellationToken token = default) =>
            Task.FromResult(0);

        public Task<IEnumerable<MediumMessage>> GetPublishedMessagesOfNeedRetry(TimeSpan lookbackSeconds) =>
            Task.FromResult<IEnumerable<MediumMessage>>([]);

        public Task ScheduleMessagesOfDelayedAsync(
            Func<object, IEnumerable<MediumMessage>, Task> scheduleTask,
            CancellationToken token = default) => Task.CompletedTask;

        public Task<IEnumerable<MediumMessage>> GetReceivedMessagesOfNeedRetry(TimeSpan lookbackSeconds) =>
            Task.FromResult<IEnumerable<MediumMessage>>([]);

        public Task<int> DeleteReceivedMessageAsync(long id) => Task.FromResult(0);

        public Task<int> DeletePublishedMessageAsync(long id) => Task.FromResult(0);

        public IMonitoringApi GetMonitoringApi() => throw new NotSupportedException("#3249 的探针宿主不开 dashboard。");
    }
}
