using System.Text.RegularExpressions;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;

namespace Nerv.IIP.Business.Mes.Web.Tests;

/// <summary>
/// 线边收料/完工入库的站点与库位部署面契约（#2008）。服务读的 <c>Inventory:*</c> 配置键与 AppHost
/// 下发的 <c>Inventory__*</c> 环境变量键过去只靠人眼对齐，改名的唯一表现是运行时 KnownException，
/// 没有任何测试转红。这里把 AppHost 下发的键约束为服务真的会读的键，并钉住环境门控：
/// 主线产品站点/库位（SITE-001 + loc-*）只允许在 Development 回落。
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
    public void AppHost_confines_every_product_location_literal_to_the_gated_helpers()
    {
        // 围栏而不是形态匹配：只禁 `.WithEnvironment("Key", "loc-…")` 双字面量形态时，把主线产品值经
        // const/局部变量转手就能绕过。这里要求**每一个**演示字面量都出现在同一条语句内的
        // DeploymentWarehouseLocation(s) 调用里，转手一次就落到调用之外，立刻转红。
        var appHost = ReadRepositoryFile(AppHostProgramPath);
        var literals = DemandLocationLiterals(appHost);

        Assert.NotEmpty(literals);
        foreach (var literal in literals)
        {
            Assert.True(
                literal.IsGated,
                $"演示站点/库位字面量 {literal.Value} 未经 DeploymentWarehouseLocation(s) 门控下发。");
        }
    }

    [Fact]
    public void AppHost_gates_the_development_seed_fallback_on_a_pinned_environment_name()
    {
        // 门控判据的**值**必须一并钉住：只钉 `LocalDevelopmentEnvironment` 这个名字的话，把常量
        // 改成 "Production" 就恰好是本门禁要拦的错误（生产回落演示库位），却一条测试都不会红。
        var appHost = ReadRepositoryFile(AppHostProgramPath);

        Assert.Contains(
            "const string LocalDevelopmentEnvironment = \"Development\";",
            appHost,
            StringComparison.Ordinal);
        Assert.Contains(
            "var localDevelopmentAppHost = string.Equals( builder.Environment.EnvironmentName, " +
            "LocalDevelopmentEnvironment, StringComparison.OrdinalIgnoreCase);",
            Regex.Replace(appHost.Replace("\r\n", "\n", StringComparison.Ordinal), @"\s+", " "),
            StringComparison.Ordinal);
        Assert.Contains("DeploymentWarehouseLocations(", AppHostMesRegion(), StringComparison.Ordinal);
    }

    [Fact]
    public void AppHost_gating_helpers_have_no_return_path_that_bypasses_the_environment_check()
    {
        // 钉 return 语句的**完整集合**而不是若干子串：子串断言对加性变异无感——在受门控 return
        // 之前插一句无条件早退，原来的子串全都还在，测试照绿。集合断言里多出一条 return 即红。
        // 语句内空白已归一，等价重排版不会假红。
        var appHost = ReadRepositoryFile(AppHostProgramPath);

        Assert.Equal(
            ["return configured.Trim();", "return localDevelopmentAppHost ? developmentSeedValue : null;"],
            ReturnStatements(appHost, "string? DeploymentWarehouseLocation(", "IReadOnlyList<string> DeploymentWarehouseLocations("));
        Assert.Equal(
            ["return indexed;", "return values;", "return localDevelopmentAppHost ? developmentSeedValues : [];"],
            ReturnStatements(appHost, "IReadOnlyList<string> DeploymentWarehouseLocations(", "> WithDeploymentEnvironment("));
        Assert.Equal(
            ["return string.IsNullOrWhiteSpace(value) ? project : project.WithEnvironment(name, value);"],
            ReturnStatements(appHost, "> WithDeploymentEnvironment(", "> WithRedisMessagingTransport("));
    }

    /// <summary>
    /// 取局部函数体内的全部 <c>return</c> 语句，语句内空白归一，保持源码顺序。
    /// </summary>
    private static string[] ReturnStatements(string appHost, string startMarker, string endMarker) =>
        Regex.Matches(TextBetween(appHost, startMarker, endMarker), @"return\s[^;]*;")
            .Select(match => Regex.Replace(match.Value, @"\s+", " "))
            .ToArray();

    [Fact]
    public void Legacy_compose_overlay_declares_the_line_side_supply_chain_unsupported()
    {
        var section = InventoryConfigurationSection();
        var mes = ComposeServiceBlock(
            ReadRepositoryFile("infra/compose/nerv-iip.platform.yml"),
            "business-mes");
        var baseline = ReadRepositoryFile("docs/architecture/deployment-baseline.md");

        // 扫**整个 overlay 文件**而不是单个服务块：键塞进共享锚点 `&dotnet-env` 同样会到达
        // business-mes，只看服务块会漏。整个 legacy overlay 都不支持该链路，全文件扫描才是对的强度。
        // 只看真正的环境变量赋值行；注释里点名这些键正是「明确声明不支持」的载体。
        var overlay = ReadRepositoryFile("infra/compose/nerv-iip.platform.yml");
        foreach (var key in GatedLocationKeys)
        {
            Assert.DoesNotMatch($@"(?m)^\s*{Regex.Escape(section)}__{Regex.Escape(key)}(__[0-9]+)?:", overlay);
        }

        Assert.Contains("不支持线边收料", mes, StringComparison.Ordinal);
        Assert.Contains("MATERIAL_SUPPLY_LOCATION_UNCONFIGURED", baseline, StringComparison.Ordinal);
    }

    /// <summary>
    /// AppHost 源码里的全部主线产品站点/库位字符串字面量，以及每个字面量是否落在同一条语句内的
    /// <c>DeploymentWarehouseLocation(s)</c> 调用中。注释里的 <c>SITE-001</c>/<c>loc-*</c> 不带
    /// 引号，不会被计入。
    /// </summary>
    private static IReadOnlyList<(string Value, bool IsGated)> DemandLocationLiterals(string appHost) =>
        Regex.Matches(appHost, @"""(SITE-|loc-)[^""]*""")
            .Select(match =>
            {
                var precedingText = appHost[..match.Index];
                var lastCall = precedingText.LastIndexOf("DeploymentWarehouseLocation", StringComparison.Ordinal);
                var lastStatementEnd = precedingText.LastIndexOfAny([';', '{', '}']);
                return (match.Value, lastCall > lastStatementEnd);
            })
            .ToArray();

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
