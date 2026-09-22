using System.Text.RegularExpressions;
using Xunit;

namespace Nerv.IIP.Messaging.CAP.Tests;

/// <summary>
/// #3738：死信「只写不读」的结构性门。
///
/// 判定维度是**服务注册了 <c>IIntegrationEventDeadLetterStore</c>**，不是「调了
/// <c>ConfigureIntegrationEventDeadLetters()</c>」——后者会把 Maintenance 漏在扫描面外（它复制了一套
/// 同名表实现，却注册同一个接口）。方向是 fail-closed：新增一个写死信的服务而没有读取出口就红，
/// 名单不在这里维护，来自对 <c>backend/services</c> 的反向枚举。
///
/// 读取出口有两种落地形态，都要被认成「有出口」：
///   * 引用共享端点模块 <c>Nerv.IIP.Messaging.CAP.Endpoints</c>（10 个服务走这条）；
///   * 在自己的 <c>Endpoints/</c> 下声明消费 store 的 <c>/dlq</c> 端点（Notification 的历史实现）。
/// 两种形态都不点名任何服务，加一个新服务不需要改这份文件。
/// </summary>
public sealed class IntegrationEventDeadLetterEndpointCoverageTests
{
    private const string StoreInterfaceName = "IIntegrationEventDeadLetterStore";

    private static readonly Regex RouteGroupDeclaration = new(
        @":\s*IIntegrationEventDeadLetterRouteGroup\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex StoreRegistration = new(
        @"Add(?:Scoped|Singleton|Transient)<\s*" + StoreInterfaceName + @"\s*,",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public void Every_service_registering_a_dead_letter_store_exposes_dead_letter_endpoints()
    {
        var services = DiscoverServices();
        Assert.NotEmpty(services);

        var registering = services.Where(service => service.RegistersStore).ToArray();
        Assert.NotEmpty(registering);

        // 自检：正则写错会表现为「零命中」而全绿。提到该接口的服务必须就是注册它的服务——
        // 只读不写的消费方（例如只注入 store 的端点）也总在同一个服务里完成注册。
        var mentioning = services.Where(service => service.MentionsStore).Select(service => service.Name).ToArray();
        Assert.Equal(mentioning, registering.Select(service => service.Name).ToArray());

        var uncovered = registering
            .Where(service => !service.ExposesEndpoints)
            .Select(service => service.Name)
            .ToArray();

        Assert.True(
            uncovered.Length == 0,
            "这些服务把集成事件死信写进了自己的库，却没有任何 HTTP 读取出口——写入方增加时读取面必须同步，"
            + "否则消费失败等于静默丢失（#3727）：\n"
            + string.Join('\n', uncovered.Select(name => $"  {name}"))
            + "\n补法二选一：Web 项目引用 Nerv.IIP.Messaging.CAP.Endpoints 并落地 6 个密封端点类，"
            + "或在本服务 Endpoints/ 下自建消费 store 的 /dlq 端点。");
    }

    private static IReadOnlyList<ServiceScan> DiscoverServices()
    {
        var servicesRoot = Path.Combine(FindRepositoryRoot(), "backend", "services");
        return EnumerateServiceRoots(servicesRoot)
            .Select(Scan)
            .OrderBy(service => service.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<string> EnumerateServiceRoots(string servicesRoot)
    {
        foreach (var candidate in Directory.EnumerateDirectories(servicesRoot, "*", SearchOption.AllDirectories)
                     .Append(servicesRoot)
                     .SelectMany(directory => Directory.EnumerateDirectories(directory)))
        {
            if (Directory.Exists(Path.Combine(candidate, "src")))
            {
                yield return candidate;
            }
        }
    }

    private static ServiceScan Scan(string serviceRoot)
    {
        var sources = Directory.EnumerateFiles(Path.Combine(serviceRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Select(path => (Path: path, Text: File.ReadAllText(path)))
            .ToArray();
        var projects = Directory.EnumerateFiles(Path.Combine(serviceRoot, "src"), "*.csproj", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToArray();

        // 「引用了共享模块」单独不够：只删掉服务的 6 个密封端点类而留着 ProjectReference，该服务的死信
        // 会重回「只写不读」，而引用还在。必须同时要求该服务真的落地了路由组（#3738 审核证据轴实测）。
        var referencesSharedModule = projects.Any(project =>
            project.Contains("Nerv.IIP.Messaging.CAP.Endpoints.csproj", StringComparison.Ordinal));
        var declaresRouteGroup = sources.Any(source =>
            RouteGroupDeclaration.IsMatch(source.Text));
        var usesSharedModule = referencesSharedModule && declaresRouteGroup;
        var hasLocalDeadLetterEndpoint = sources.Any(source =>
            source.Path.Contains($"{Path.DirectorySeparatorChar}Endpoints{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && source.Text.Contains(StoreInterfaceName, StringComparison.Ordinal)
            && source.Text.Contains("/dlq", StringComparison.Ordinal));

        return new ServiceScan(
            Path.GetFileName(serviceRoot),
            sources.Any(source => StoreRegistration.IsMatch(source.Text)),
            sources.Any(source => source.Text.Contains(StoreInterfaceName, StringComparison.Ordinal)),
            usesSharedModule || hasLocalDeadLetterEndpoint);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "README.md"))
                && Directory.Exists(Path.Combine(directory.FullName, "backend")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private sealed record ServiceScan(string Name, bool RegistersStore, bool MentionsStore, bool ExposesEndpoints);
}
