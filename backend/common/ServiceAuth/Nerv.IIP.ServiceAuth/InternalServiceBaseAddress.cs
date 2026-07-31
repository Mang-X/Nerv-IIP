using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Nerv.IIP.ServiceAuth;

/// <summary>
/// 服务间调用的下游基址解析：把 <c>&lt;Service&gt;:BaseUrl</c> 配置读成绝对 <see cref="Uri"/>。
/// </summary>
/// <remarks>
/// <para>
/// 收敛理由：这段逻辑原本在 14 个 <c>Program.cs</c>（11 个业务服务 + Ops + 两个 Gateway）里各抄一份，
/// 而且**已经抄漂了**——其中 6 份只放行 <c>Development</c>，另外 8 份还放行 <c>Testing</c>；
/// 异常文案也有两种写法。结果是同一个「忘配 BaseUrl」的错误，在不同服务上表现不同：
/// 有的在 Testing 下静默走本地端口，有的直接启动失败。
/// </para>
/// <para>
/// 放在 <c>Nerv.IIP.ServiceAuth</c> 而不是新建通用工具库：本库已经是「服务间调用」这件事的归属
/// （内部服务令牌 <see cref="InternalServiceAuthentication"/> 就在这里），且上述 14 个宿主项目
/// 无一例外已经引用它，收敛不引入任何新的项目依赖。
/// </para>
/// </remarks>
public static class InternalServiceBaseAddress
{
    /// <summary>
    /// 解析下游服务基址。
    /// </summary>
    /// <param name="configuration">宿主配置。</param>
    /// <param name="environment">宿主环境；<c>Development</c> 与 <c>Testing</c> 才允许回退。</param>
    /// <param name="configurationKey">配置键，形如 <c>MasterData:BaseUrl</c>。</param>
    /// <param name="developmentFallback">本地开发/测试回退地址，形如 <c>http://localhost:5107</c>。</param>
    /// <returns>配置值优先；未配置且处于 Development/Testing 时返回回退地址。</returns>
    /// <exception cref="InvalidOperationException">
    /// 生产类环境未配置该键。**绝不回退到 localhost**：真实部署里静默指向本机会让跨服务调用
    /// 连接被拒或（更糟）打到同机上的另一个服务，比启动失败难查得多。
    /// </exception>
    public static Uri Resolve(
        IConfiguration configuration,
        IHostEnvironment environment,
        string configurationKey,
        string developmentFallback)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var configuredBaseUrl = configuration[configurationKey];
        if (!string.IsNullOrWhiteSpace(configuredBaseUrl))
        {
            return new Uri(configuredBaseUrl, UriKind.Absolute);
        }

        // Testing 与 Development 同档：集成测试宿主起在 Testing 下，若不放行就得为每个下游
        // 逐一喂配置，漏一个就是一条「只在 CI 挂」的启动失败。
        if (environment.IsDevelopment() || environment.IsEnvironment(TestingEnvironmentName))
        {
            return new Uri(developmentFallback, UriKind.Absolute);
        }

        throw new InvalidOperationException($"{configurationKey} is required outside Development.");
    }

    /// <summary>集成测试宿主使用的环境名。</summary>
    public const string TestingEnvironmentName = "Testing";
}
