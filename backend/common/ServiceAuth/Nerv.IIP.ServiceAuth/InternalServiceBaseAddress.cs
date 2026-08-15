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
/// **收敛不等于一律取最松的一档**：回退档位是安全语义，按宿主性质分成两个方法，调用方必须显式选一个。
/// <list type="bullet">
/// <item>
/// <see cref="Resolve"/>（只放行 <c>Development</c>）——**边缘入口用**（Gateway）。
/// 若某环境以 <c>ASPNETCORE_ENVIRONMENT=Testing</c> 部署（staging / 测试环），漏配下游基址时
/// 静默回落 localhost 正是本类要防的事；边缘入口不吃这一档。
/// </item>
/// <item>
/// <see cref="ResolveAllowingTestHost"/>（放行 <c>Development</c> + <c>Testing</c>）——业务服务与 Ops 用。
/// 它们的集成测试宿主起在 <c>Testing</c> 下，不放行就得为每个下游逐一喂配置。
/// </item>
/// </list>
/// 不提供带默认值的布尔参数：默认值正是「不写就悄悄拿到某一档」的老毛病。
/// </para>
/// <para>
/// 放在 <c>Nerv.IIP.ServiceAuth</c> 而不是新建通用工具库：本库已经是「服务间调用」这件事的归属
/// （内部服务令牌 <see cref="InternalServiceAuthentication"/> 就在这里），且上述 14 个宿主项目
/// 无一例外已经引用它，收敛不引入任何新的项目依赖。
/// </para>
/// </remarks>
public static class InternalServiceBaseAddress
{
    /// <summary>集成测试宿主使用的环境名。</summary>
    public const string TestingEnvironmentName = "Testing";

    /// <summary>
    /// 解析下游服务基址；**只有 <c>Development</c> 允许回退**。
    /// </summary>
    /// <remarks>
    /// 边缘入口（Gateway）用这一档：<c>Testing</c> 有可能是真实部署的环境名，
    /// 在那里漏配下游基址必须启动失败，而不是静默指向本机。
    /// </remarks>
    /// <param name="configuration">宿主配置。</param>
    /// <param name="environment">宿主环境。</param>
    /// <param name="configurationKey">配置键，形如 <c>Iam:BaseUrl</c>。</param>
    /// <param name="developmentFallback">本地开发回退地址，形如 <c>http://localhost:5102</c>。</param>
    /// <returns>配置值优先；未配置且处于 Development 时返回回退地址。</returns>
    /// <exception cref="InvalidOperationException">非 Development 且未配置该键。</exception>
    public static Uri Resolve(
        IConfiguration configuration,
        IHostEnvironment environment,
        string configurationKey,
        string developmentFallback)
    {
        return ResolveCore(
            configuration,
            environment,
            configurationKey,
            developmentFallback,
            allowTestHostFallback: false);
    }

    /// <summary>
    /// 解析下游服务基址；<c>Development</c> 与 <c>Testing</c> 都允许回退。
    /// </summary>
    /// <remarks>
    /// 业务服务与 Ops 用这一档：集成测试宿主起在 <c>Testing</c> 下，若不放行就得为每个下游
    /// 逐一喂配置，漏一个就是一条「只在 CI 挂」的启动失败。
    /// **边缘入口（Gateway）不要用这个方法**，改用 <see cref="Resolve"/>。
    /// </remarks>
    /// <param name="configuration">宿主配置。</param>
    /// <param name="environment">宿主环境。</param>
    /// <param name="configurationKey">配置键，形如 <c>MasterData:BaseUrl</c>。</param>
    /// <param name="developmentFallback">本地开发 / 测试回退地址，形如 <c>http://localhost:5107</c>。</param>
    /// <returns>配置值优先；未配置且处于 Development/Testing 时返回回退地址。</returns>
    /// <exception cref="InvalidOperationException">非 Development/Testing 且未配置该键。</exception>
    public static Uri ResolveAllowingTestHost(
        IConfiguration configuration,
        IHostEnvironment environment,
        string configurationKey,
        string developmentFallback)
    {
        return ResolveCore(
            configuration,
            environment,
            configurationKey,
            developmentFallback,
            allowTestHostFallback: true);
    }

    /// <exception cref="InvalidOperationException">
    /// 生产类环境未配置该键。**绝不回退到 localhost**：真实部署里静默指向本机会让跨服务调用
    /// 连接被拒或（更糟）打到同机上的另一个服务，比启动失败难查得多。
    /// </exception>
    private static Uri ResolveCore(
        IConfiguration configuration,
        IHostEnvironment environment,
        string configurationKey,
        string developmentFallback,
        bool allowTestHostFallback)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var configuredBaseUrl = configuration[configurationKey];
        if (!string.IsNullOrWhiteSpace(configuredBaseUrl))
        {
            return new Uri(configuredBaseUrl, UriKind.Absolute);
        }

        if (environment.IsDevelopment()
            || (allowTestHostFallback && environment.IsEnvironment(TestingEnvironmentName)))
        {
            return new Uri(developmentFallback, UriKind.Absolute);
        }

        // 文案点名本宿主实际放行的档位：对放行 Testing 的宿主只说 "outside Development" 会误导排障。
        var allowedEnvironments = allowTestHostFallback ? "Development/Testing" : "Development";
        throw new InvalidOperationException($"{configurationKey} is required outside {allowedEnvironments}.");
    }
}
