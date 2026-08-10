using System.Net;
using System.Reflection;
using DotNetCore.CAP;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Nerv.IIP.Ops.Web.Application.Auth;

namespace Nerv.IIP.Ops.Web.Tests;

/// <summary>
/// Ops 测试宿主的外部依赖隔离：CAP 后台生命周期与 IAM 出网调用。
/// </summary>
/// <remarks>
/// <para>
/// 只有 <c>Persistence:Provider=PostgreSQL</c> 的宿主才会注册 CAP，而 CAP 的后台宿主服务
/// （<c>Bootstrapper</c>）在 <c>host.Start()</c> 时会串起 storage initializer、consumer register
/// 和 dispatcher：storage initializer 去连 PostgreSQL，consumer register 去连 broker。测试里这两个
/// 端点都不可达，于是 consumer register 进入「连接失败 → 重启」循环，它在重启路径上换掉自己的
/// <c>CancellationTokenSource</c>；宿主 dispose 时 <c>ConsumerRegister.Dispose()</c> 又会
/// <c>Pulse()</c> 那个 CTS。两者相撞就是断言早已通过、却在 teardown 阶段抛
/// <c>ObjectDisposedException: The CancellationTokenSource has been disposed</c>——CI 上表现为与业务
/// diff 无关的随机红（NERV-733）。
/// </para>
/// <para>
/// 收敛方向按 NERV-733 的范围限定在测试侧：不改 CAP 生产行为，也不改服务注册，只把测试宿主里
/// **本就不该跑**的 CAP 后台生命周期摘掉。被测面（endpoint、认证、EF 查询）没有一条依赖 CAP 后台
/// 处理，因此摘掉它不降低任何断言强度。
/// </para>
/// </remarks>
internal static class OpsTestHostIsolation
{
    private static readonly Assembly CapAssembly = typeof(ICapPublisher).Assembly;

    /// <summary>把 CAP 注册的后台宿主服务从测试宿主中摘除。</summary>
    public static IWebHostBuilder WithoutCapBackgroundProcessing(this IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.ConfigureServices(services => RemoveCapBackgroundProcessing(services));
    }

    /// <summary>摘除 CAP 后台宿主服务，返回实际摘除的条数。</summary>
    public static int RemoveCapBackgroundProcessing(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var capHostedServices = services.Where(IsCapOwnedHostedService).ToArray();
        foreach (var descriptor in capHostedServices)
        {
            services.Remove(descriptor);
        }

        return capHostedServices.Length;
    }

    /// <summary>判断一条注册是否为 CAP 自己的 <see cref="IHostedService"/>。</summary>
    /// <remarks>
    /// 判定必须窄到「服务类型是 <see cref="IHostedService"/> 且实现来自 CAP 程序集」：宿主里的
    /// <c>GenericWebHostService</c>（Web 服务器本身）和 Ops 自己的 lease reaper 都是
    /// <see cref="IHostedService"/>，一并删掉就没有测试宿主可言了。
    /// CAP 10 用工厂而非实现类型注册 Bootstrapper（<see cref="ServiceDescriptor.ImplementationType"/>
    /// 为 <see langword="null"/>），所以工厂那一支要回退到 lambda 的声明程序集。
    /// <c>OpsTestHostIsolationTests</c> 钉住这两支的判定结果，CAP 升级改注册形态时会在那里变红。
    /// </remarks>
    public static bool IsCapOwnedHostedService(ServiceDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        if (descriptor.ServiceType != typeof(IHostedService))
        {
            return false;
        }

        if (descriptor.ImplementationType is { } implementationType)
        {
            return implementationType.Assembly == CapAssembly;
        }

        return descriptor.ImplementationFactory?.Method.DeclaringType?.Assembly == CapAssembly;
    }
}

/// <summary>
/// 把 Ops 的 IAM 类型化客户端换成进程内脚本化 handler，使 Production 宿主的凭据校验既走完整的
/// IAM 分支，又不产生任何真实网络往返。
/// </summary>
/// <remarks>
/// 不用「把 <c>Iam:BaseUrl</c> 指向不可达端口」的老写法：那条路径拿到的是 <c>iam-unavailable</c>
/// 而非 <c>iam-rejected</c>，两者都 fail closed 成 401，断言因此分不清「IAM 拒绝」和「连不上 IAM」；
/// 而且 refused 在 macOS/BSD 上会被静默丢弃成超时，本身就是一类不确定性。
/// </remarks>
internal sealed class StubbedIamCredentialHandlerFilter(HttpStatusCode statusCode) : IHttpMessageHandlerBuilderFilter
{
    private int requestCount;

    /// <summary>IAM 校验实际被调用的次数。</summary>
    public int RequestCount => Volatile.Read(ref requestCount);

    public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next)
    {
        ArgumentNullException.ThrowIfNull(next);

        return builder =>
        {
            next(builder);
            if (builder.Name != nameof(IamOpsConnectorCredentialValidator))
            {
                return;
            }

            builder.PrimaryHandler = new ScriptedHttpMessageHandler((_, _) =>
            {
                Interlocked.Increment(ref requestCount);
                return Task.FromResult(new HttpResponseMessage(statusCode));
            });
        };
    }
}
