using System.Text.RegularExpressions;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;

namespace Nerv.IIP.Business.Mes.Web.Tests;

/// <summary>
/// 线边收料/完工入库的站点与库位部署面契约（#2008）。服务读的 <c>Inventory:*</c> 配置键与 AppHost
/// 下发的 <c>Inventory__*</c> 环境变量键过去只靠人眼对齐，改名的唯一表现是运行时 KnownException，
/// 没有任何测试转红。这里把 AppHost 下发的键约束为服务真的会读的键，并钉住环境门控：
/// 演示站点/库位（SITE-001 + WH-WB-*）只允许在 Development 回落。
/// </summary>
public sealed class MesInventoryLocationDeploymentConfigurationTests
{
    private const string AppHostProgramPath = "infra/aspire/Nerv.IIP.AppHost/Program.cs";
    private const string MesProgramPath =
        "backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Program.cs";

    /// <summary>门控覆盖的仓储位置键；<c>BaseUrl</c> 是服务端点，不在门控范围内。</summary>
    private static readonly string[] GatedLocationKeys =
    [
        "FinishedGoodsLocationCode",
        "LineSideLocationCode",
        "SiteCode",
        "SourceLocationCodes"
    ];

    [Fact]
    public void AppHost_only_injects_inventory_keys_the_service_actually_reads()
    {
        var section = InventoryConfigurationSection();
        var program = ReadRepositoryFile(MesProgramPath);

        // 服务侧可读键有两个来源：绑定到 options 类型的属性，以及 Program.cs 里直接读的配置路径。
        var boundKeys = typeof(MesMaterialSupplyLocationOptions)
            .GetProperties()
            .Select(property => property.Name);
        var directlyReadKeys = Regex
            .Matches(program, $@"""{Regex.Escape(section)}:(?<name>[A-Za-z0-9]+)""")
            .Select(match => match.Groups["name"].Value);
        var readableKeys = boundKeys
            .Concat(directlyReadKeys)
            .ToHashSet(StringComparer.Ordinal);

        var injectedKeys = InjectedKeyNames(section);

        Assert.NotEmpty(injectedKeys);
        Assert.Empty(injectedKeys.Except(readableKeys, StringComparer.Ordinal));
    }

    [Fact]
    public void AppHost_injects_every_gated_warehouse_location_key()
    {
        var injectedKeys = InjectedKeyNames(InventoryConfigurationSection());

        Assert.Empty(GatedLocationKeys.Except(injectedKeys, StringComparer.Ordinal));
    }

    [Fact]
    public void AppHost_reads_its_override_under_the_same_configuration_path_it_injects()
    {
        var section = InventoryConfigurationSection();
        var region = AppHostMesRegion();

        var overrideKeys = Regex
            .Matches(region, $@"""{Regex.Escape(section)}:(?<name>[A-Za-z0-9]+)""")
            .Select(match => match.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);

        // 每个受门控的库位键都必须在同名配置路径下提供部署方覆盖入口。
        Assert.Empty(GatedLocationKeys.Except(overrideKeys, StringComparer.Ordinal));
    }

    [Fact]
    public void AppHost_never_injects_demo_sites_or_locations_unconditionally()
    {
        var appHost = ReadRepositoryFile(AppHostProgramPath);

        var unconditional = Regex
            .Matches(appHost, @"\.WithEnvironment\(\s*""[^""]+""\s*,\s*""(?<value>(SITE-|WH-WB-)[^""]*)""")
            .Select(match => match.Groups["value"].Value)
            .ToArray();

        Assert.Empty(unconditional);
        // 演示种子值只能作为受门控 helper 的 Development 回落参数出现。
        var region = AppHostMesRegion();
        Assert.Contains("DeploymentWarehouseLocation(\"Inventory:SiteCode\", \"SITE-001\")", region, StringComparison.Ordinal);
        Assert.Contains("DeploymentWarehouseLocations(", region, StringComparison.Ordinal);
    }

    [Fact]
    public void Legacy_compose_overlay_declares_the_line_side_supply_chain_unsupported()
    {
        var section = InventoryConfigurationSection();
        var mes = ComposeServiceBlock(
            ReadRepositoryFile("infra/compose/nerv-iip.platform.yml"),
            "business-mes");
        var baseline = ReadRepositoryFile("docs/architecture/deployment-baseline.md");

        // 只看真正的环境变量赋值行；注释里点名这些键正是「明确声明不支持」的载体。
        foreach (var key in GatedLocationKeys)
        {
            Assert.DoesNotMatch($@"(?m)^\s*{Regex.Escape(section)}__{Regex.Escape(key)}(__[0-9]+)?:", mes);
        }

        Assert.Contains("不支持线边收料", mes, StringComparison.Ordinal);
        Assert.Contains("MATERIAL_SUPPLY_LOCATION_UNCONFIGURED", baseline, StringComparison.Ordinal);
    }

    private static HashSet<string> InjectedKeyNames(string section)
    {
        // 索引形态（Inventory__SourceLocationCodes__0）与标量形态都归一到键名本身。
        return Regex
            .Matches(AppHostMesRegion(), $@"""{Regex.Escape(section)}__(?<name>[A-Za-z0-9]+)")
            .Select(match => match.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// 配置节名从 MES 的绑定语句读出来，而不是写死：改了 <c>GetSection</c> 的字符串这里立刻转红。
    /// </summary>
    private static string InventoryConfigurationSection()
    {
        var program = ReadRepositoryFile(MesProgramPath);
        var match = Regex.Match(
            program,
            $@"Configure<{nameof(MesMaterialSupplyLocationOptions)}>\(builder\.Configuration\.GetSection\(""(?<section>[A-Za-z0-9:]+)""\)\)");

        Assert.True(match.Success, $"{nameof(MesMaterialSupplyLocationOptions)} 的配置节绑定语句未找到。");
        return match.Groups["section"].Value;
    }

    private static string AppHostMesRegion() =>
        TextBetween(ReadRepositoryFile(AppHostProgramPath), "var businessMes =", "var businessDemandPlanning =");

    private static string ComposeServiceBlock(string yaml, string serviceName)
    {
        var match = Regex.Match(
            yaml.Replace("\r\n", "\n", StringComparison.Ordinal),
            $@"(?ms)^  {Regex.Escape(serviceName)}:\s*\n(?<body>.*?)(?=^  [a-z0-9][a-z0-9-]*:\s*$|\z)");

        Assert.True(match.Success, $"Compose service '{serviceName}' was not found.");
        return match.Value;
    }

    private static string TextBetween(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        var end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Text range '{startMarker}' to '{endMarker}' was not found.");
        return text[start..end];
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        var repositoryRoot = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine(
            repositoryRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "README.md")) &&
                Directory.Exists(Path.Combine(directory.FullName, "backend")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
