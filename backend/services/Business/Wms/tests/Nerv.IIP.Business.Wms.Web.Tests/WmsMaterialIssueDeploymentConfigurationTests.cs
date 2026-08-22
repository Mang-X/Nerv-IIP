using System.Text.RegularExpressions;
using Nerv.IIP.Business.Wms.Web.Application.IntegrationEventHandlers;

namespace Nerv.IIP.Business.Wms.Web.Tests;

/// <summary>
/// 领料默认库位的部署面契约（#2008）。服务读的配置键与 AppHost 下发的环境变量键过去只靠人眼对齐，
/// 任一侧改名的唯一表现是「领料消息全部进死信」，没有任何测试转红；这里把两侧的键集合对起来。
/// 同时钉住环境门控：演示库位只允许在 Development 回落，不得无条件下发到生产安装。
/// </summary>
public sealed class WmsMaterialIssueDeploymentConfigurationTests
{
    private const string AppHostProgramPath = "infra/aspire/Nerv.IIP.AppHost/Program.cs";
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
    public void AppHost_never_injects_demo_warehouse_locations_unconditionally()
    {
        var appHost = ReadRepositoryFile(AppHostProgramPath);

        // 演示站点/库位只能作为 DeploymentWarehouseLocation(s) 的 Development 回落值出现，
        // 不允许再以 .WithEnvironment("Key", "WH-WB-...") 的形式无条件下发。
        var unconditional = Regex
            .Matches(appHost, @"\.WithEnvironment\(\s*""[^""]+""\s*,\s*""(?<value>(SITE-|WH-WB-)[^""]*)""")
            .Select(match => match.Groups["value"].Value)
            .ToArray();

        Assert.Empty(unconditional);
    }

    [Fact]
    public void AppHost_gates_the_development_seed_fallback_on_its_own_environment_name()
    {
        var appHost = ReadRepositoryFile(AppHostProgramPath);

        Assert.Contains(
            "var localDevelopmentAppHost = string.Equals(\n    builder.Environment.EnvironmentName,\n" +
            "    LocalDevelopmentEnvironment,\n    StringComparison.OrdinalIgnoreCase);",
            Normalize(appHost),
            StringComparison.Ordinal);
        // 未配置且非 Development 时返回 null / 空集合 = 该键根本不下发，服务侧 fail-closed 生效。
        Assert.Contains(
            "return localDevelopmentAppHost ? developmentSeedValue : null;",
            appHost,
            StringComparison.Ordinal);
        Assert.Contains(
            "return localDevelopmentAppHost ? developmentSeedValues : [];",
            appHost,
            StringComparison.Ordinal);
        Assert.Contains(
            "return string.IsNullOrWhiteSpace(value) ? project : project.WithEnvironment(name, value);",
            appHost,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Legacy_compose_overlay_declares_the_material_issue_chain_unsupported()
    {
        var section = MaterialIssueConfigurationSection();
        var wms = ComposeServiceBlock(
            ReadRepositoryFile("infra/compose/nerv-iip.platform.yml"),
            "business-wms");
        var baseline = ReadRepositoryFile("docs/architecture/deployment-baseline.md");

        // 只看真正的环境变量赋值行；注释里点名这些键正是「明确声明不支持」的载体。
        Assert.DoesNotMatch($@"(?m)^\s*{Regex.Escape(section)}__[A-Za-z0-9_]+:", wms);
        Assert.Contains("不支持 MES→WMS 领料链路", wms, StringComparison.Ordinal);
        Assert.Contains("不支持 MES→WMS 领料链路", baseline, StringComparison.Ordinal);
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
        TextBetween(ReadRepositoryFile(AppHostProgramPath), "var businessWms =", "var businessIndustrialTelemetry =");

    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

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
