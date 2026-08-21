using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Nerv.IIP.Business.Erp.Web.Application.Seed;
using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.Erp.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class ErpDemoSeedStartupGovernanceTests
{
    private const string ProgramRelativePath =
        "backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Program.cs";

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Testing")]
    public void Sales_order_demand_demo_seed_is_rejected_outside_development(string environmentName)
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            [ErpDemoSeedStartupGovernance.SalesOrderDemandDemoEnabledKey] = "true"
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ErpDemoSeedStartupGovernance.EnsureDevelopmentOnly(
                configuration,
                new TestHostEnvironment(environmentName)));

        Assert.Equal(
            "Erp:Seed:SalesOrderDemandDemo:Enabled=true is only allowed for BusinessERP in Development.",
            exception.Message);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void World_history_seed_is_rejected_outside_development(string environmentName)
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            [WorldHistoryConfiguration.EnabledKey] = "true"
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ErpDemoSeedStartupGovernance.EnsureDevelopmentOnly(
                configuration,
                new TestHostEnvironment(environmentName)));

        Assert.Equal(
            "LeaderDemo:History:Enabled=true is only allowed for BusinessERP in Development.",
            exception.Message);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("development")]
    public void Development_keeps_both_demo_seed_switches_usable(string environmentName)
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            [ErpDemoSeedStartupGovernance.SalesOrderDemandDemoEnabledKey] = "true",
            [WorldHistoryConfiguration.EnabledKey] = "true"
        });

        ErpDemoSeedStartupGovernance.EnsureDevelopmentOnly(configuration, new TestHostEnvironment(environmentName));

        Assert.True(ErpDemoSeedStartupGovernance.IsSalesOrderDemandDemoEnabled(configuration));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    public void Disabled_demo_seed_starts_in_any_environment(string? configuredValue)
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            [ErpDemoSeedStartupGovernance.SalesOrderDemandDemoEnabledKey] = configuredValue,
            [WorldHistoryConfiguration.EnabledKey] = configuredValue
        });

        ErpDemoSeedStartupGovernance.EnsureDevelopmentOnly(configuration, new TestHostEnvironment("Production"));

        Assert.False(ErpDemoSeedStartupGovernance.IsSalesOrderDemandDemoEnabled(configuration));
    }

    [Fact]
    public void Seed_switch_reader_uses_the_governed_key()
    {
        Assert.Equal("Erp:Seed:SalesOrderDemandDemo:Enabled", ErpDemoSeedStartupGovernance.SalesOrderDemandDemoEnabledKey);
        Assert.True(ErpDemoSeedStartupGovernance.IsSalesOrderDemandDemoEnabled(BuildConfiguration(
            new Dictionary<string, string?> { ["Erp:Seed:SalesOrderDemandDemo:Enabled"] = "true" })));
        Assert.False(ErpDemoSeedStartupGovernance.IsSalesOrderDemandDemoEnabled(BuildConfiguration(
            new Dictionary<string, string?>())));
    }

    /// <summary>
    /// 真正的不变量：非 Development 的 host 在开始服务请求之前就拒绝启动。
    /// 源码文本断言证明不了这一条（注释掉调用、或把调用挪进 <c>if (autoMigrate)</c> 都能骗过它）。
    /// </summary>
    [Theory]
    [InlineData("Erp:Seed:SalesOrderDemandDemo:Enabled", "Erp:Seed:SalesOrderDemandDemo:Enabled=true")]
    [InlineData("LeaderDemo:History:Enabled", "LeaderDemo:History:Enabled=true")]
    public async Task Host_refuses_to_start_outside_development_when_a_demo_seed_switch_is_on(
        string switchKey,
        string expectedMessageFragment)
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:PostgreSQL"] =
                            "Host=unused;Database=nerv_iip_erp_demo_seed_governance;Username=nerv;Password=nerv",
                        ["InternalService:BearerToken"] = "test-internal-service-token",
                        ["Persistence:AutoMigrate"] = "false",
                        [switchKey] = "true"
                    }));
            });

        var exception = await Record.ExceptionAsync(async () =>
        {
            using var client = factory.CreateClient();
            await client.GetAsync("/health");
        });

        Assert.Contains(exception.Flatten(), candidate =>
            candidate is InvalidOperationException
            && candidate.Message.Contains(expectedMessageFragment, StringComparison.Ordinal)
            && candidate.Message.Contains("only allowed for BusinessERP in Development", StringComparison.Ordinal));
    }

    [Fact]
    public void Host_evaluates_the_fail_closed_gate_before_it_starts_listening()
    {
        var program = ReadRepositoryText(ProgramRelativePath);

        var gateIndex = program.IndexOf(
            "ErpDemoSeedStartupGovernance.EnsureDevelopmentOnly(builder.Configuration, app.Environment);",
            StringComparison.Ordinal);
        var startIndex = program.IndexOf("await app.StartAsync();", StringComparison.Ordinal);

        Assert.True(gateIndex >= 0, "Program.cs 必须调用演示种子 fail-closed 门禁。");
        Assert.True(startIndex >= 0, "Program.cs 必须仍然显式启动 host。");
        Assert.True(gateIndex < startIndex, "fail-closed 门禁必须早于 host 开始监听。");
    }

    [Fact]
    public void Host_reads_the_seed_switch_only_through_the_gate_owner()
    {
        var program = ReadRepositoryText(ProgramRelativePath);

        Assert.DoesNotContain(
            $"\"{ErpDemoSeedStartupGovernance.SalesOrderDemandDemoEnabledKey}\"",
            program,
            StringComparison.Ordinal);
        Assert.Contains(
            "ErpDemoSeedStartupGovernance.IsSalesOrderDemandDemoEnabled(builder.Configuration)",
            program,
            StringComparison.Ordinal);
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static string ReadRepositoryText(string relativePath)
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"未找到受治理文件：{relativePath}");
        return File.ReadAllText(path);
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

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Nerv.IIP.Business.Erp.Web.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
