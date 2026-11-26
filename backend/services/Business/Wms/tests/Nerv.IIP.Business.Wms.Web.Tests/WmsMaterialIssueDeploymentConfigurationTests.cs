using System.Text.RegularExpressions;
using Nerv.IIP.Business.Wms.Web.Application.IntegrationEventHandlers;

namespace Nerv.IIP.Business.Wms.Web.Tests;

/// <summary>
/// 领料默认库位的部署面契约（#2008）。服务读的配置键与 AppHost 下发的环境变量键过去只靠人眼对齐，
/// 任一侧改名的唯一表现是「领料消息全部进死信」，没有任何测试转红；这里把两侧的键集合对起来。
/// </summary>
public sealed class WmsMaterialIssueDeploymentConfigurationTests
{
    private const string AppHostServicesPath = "infra/aspire/Nerv.IIP.AppHost/AppHostServices.cs";
    private const string WmsProgramPath =
        "backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web/Program.cs";

    [Fact]
    public void AppHost_only_injects_material_issue_keys_the_service_can_bind()
    {
        var section = MaterialIssueConfigurationSection();
        var bindableKeys = typeof(WmsMaterialIssueLocationOptions)
            .GetProperties()
            .Select(property => $"{section}__{property.Name}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        var injectedKeys = Regex
            .Matches(AppHostWmsRegion(), $@"""(?<key>{Regex.Escape(section)}__[A-Za-z0-9_]+)""")
            .Select(match => match.Groups["key"].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(bindableKeys, injectedKeys);
    }

    [Fact]
    public void AppHost_reads_its_override_under_the_same_configuration_path_it_injects()
    {
        var section = MaterialIssueConfigurationSection();
        var region = AppHostWmsRegion();

        var injectedKeys = Regex
            .Matches(region, $@"""(?<key>{Regex.Escape(section)})__(?<name>[A-Za-z0-9]+)""")
            .Select(match => match.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var overrideKeys = Regex
            .Matches(region, $@"""(?<key>{Regex.Escape(section)}):(?<name>[A-Za-z0-9]+)""")
            .Select(match => match.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(injectedKeys);
        Assert.Equal(injectedKeys, overrideKeys);
    }

    [Fact]
    public void AppHost_confines_every_location_literal_to_the_gated_helpers()
    {
        // 围栏而不是形态匹配：只禁某一个位置码前缀的双字面量形态时，把受治理位置值经
        // const/局部变量转手就能绕过。这里要求**每一个**位置字面量都出现在同一条语句内的
        // DeploymentWarehouseLocation(s) 调用里，转手一次就落到调用之外，立刻转红。
        foreach (var literal in DemandLocationLiterals(ReadRepositoryFile(AppHostServicesPath)))
        {
            Assert.True(
                literal.IsGated,
                $"受治理站点/库位字面量 {literal.Value} 未经 DeploymentWarehouseLocation(s) 门控下发。");
        }
    }

    [Fact]
    public void Legacy_compose_overlay_declares_the_material_issue_chain_unsupported()
    {
        var section = MaterialIssueConfigurationSection();
        var wms = ComposeServiceBlock(
            ReadRepositoryFile("infra/compose/nerv-iip.platform.yml"),
            "business-wms");

        // 扫**整个 overlay 文件**而不是单个服务块：键塞进共享锚点 `&dotnet-env` 同样会到达
        // business-wms，只看服务块会漏。整个 legacy overlay 都不支持该链路，全文件扫描才是对的强度。
        // 只看真正的环境变量赋值行；注释里点名这些键正是「明确声明不支持」的载体。
        Assert.DoesNotMatch(
            $@"(?m)^\s*{Regex.Escape(section)}__[A-Za-z0-9_]+:",
            ReadRepositoryFile("infra/compose/nerv-iip.platform.yml"));
        Assert.Contains("不支持 MES→WMS 领料链路", wms, StringComparison.Ordinal);
    }

    /// <summary>
    /// 配置节名从 WMS 的绑定语句读出来，而不是写死：改了 <c>GetSection</c> 的字符串这里立刻转红。
    /// </summary>
    private static string MaterialIssueConfigurationSection()
    {
        var program = ReadRepositoryFile(WmsProgramPath);
        var match = Regex.Match(
            program,
            $@"Configure<{nameof(WmsMaterialIssueLocationOptions)}>\(builder\.Configuration\.GetSection\(""(?<section>[A-Za-z0-9:]+)""\)\)");

        Assert.True(match.Success, $"{nameof(WmsMaterialIssueLocationOptions)} 的配置节绑定语句未找到。");
        return match.Groups["section"].Value;
    }

    private static string AppHostWmsRegion() =>
        TextBetween(ReadRepositoryFile(AppHostServicesPath), "var businessWms =", "var businessIndustrialTelemetry =");

    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string CollapseWhitespace(string text) =>
        Regex.Replace(Normalize(text), @"\s+", " ");

    /// <summary>
    /// AppHost 源码里的全部受治理站点/库位字符串字面量，以及每个字面量是否落在同一条语句内的
    /// <c>DeploymentWarehouseLocation(s)</c> 调用中。注释里的 <c>SITE-001</c>/<c>WH-WB-*</c>/
    /// <c>loc-*</c> 不带引号，不会被计入。
    /// </summary>
    internal static IReadOnlyList<(string Value, bool IsGated)> DemandLocationLiterals(string appHost) =>
        Regex.Matches(appHost, @"""(SITE-|WH-WB-|loc-)[^""]*""")
            .Select(match =>
            {
                var precedingText = appHost[..match.Index];
                var lastCall = precedingText.LastIndexOf("DeploymentWarehouseLocation", StringComparison.Ordinal);
                var lastStatementEnd = precedingText.LastIndexOfAny([';', '{', '}']);
                return (match.Value, lastCall > lastStatementEnd);
            })
            .ToArray();

    private static string ComposeServiceBlock(string yaml, string serviceName)
    {
        var match = Regex.Match(
            Normalize(yaml),
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
