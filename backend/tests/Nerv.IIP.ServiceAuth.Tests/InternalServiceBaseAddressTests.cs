using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Nerv.IIP.ServiceAuth.Tests;

/// <summary>
/// 下游基址解析的权威口径。这段逻辑原本在 14 个 Program.cs 里各抄一份并且已经抄漂
/// （6 份只放行 Development、8 份还放行 Testing），收敛后由本组用例锁定**两档**语义。
/// </summary>
public sealed class InternalServiceBaseAddressTests
{
    private const string Key = "MasterData:BaseUrl";
    private const string Fallback = "http://localhost:5107";

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Configured_base_url_wins_in_every_environment_for_both_tiers(string environmentName)
    {
        var configuration = Configuration((Key, "https://master-data.internal:8443"));
        var environment = Environment(environmentName);
        var expected = new Uri("https://master-data.internal:8443");

        Assert.Equal(expected, InternalServiceBaseAddress.Resolve(configuration, environment, Key, Fallback));
        Assert.Equal(expected, InternalServiceBaseAddress.ResolveAllowingTestHost(configuration, environment, Key, Fallback));
    }

    [Fact]
    public void Both_tiers_fall_back_in_development()
    {
        var configuration = Configuration();
        var environment = Environment("Development");

        Assert.Equal(new Uri(Fallback), InternalServiceBaseAddress.Resolve(configuration, environment, Key, Fallback));
        Assert.Equal(new Uri(Fallback), InternalServiceBaseAddress.ResolveAllowingTestHost(configuration, environment, Key, Fallback));
    }

    /// <summary>
    /// **两档的分歧点**：业务服务与 Ops 的集成测试宿主起在 Testing 下，需要回退；
    /// 边缘入口（Gateway）不要——Testing 有可能是真实部署的环境名（staging / 测试环），
    /// 在那里漏配下游基址静默指向 localhost 正是本类要防的事，必须启动失败。
    /// </summary>
    [Fact]
    public void Testing_is_the_dividing_line_between_the_two_tiers()
    {
        var configuration = Configuration();
        var environment = Environment("Testing");

        Assert.Equal(
            new Uri(Fallback),
            InternalServiceBaseAddress.ResolveAllowingTestHost(configuration, environment, Key, Fallback));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            InternalServiceBaseAddress.Resolve(configuration, environment, Key, Fallback));
        Assert.Contains(Key, exception.Message, StringComparison.Ordinal);
    }

    // 生产类环境两档都绝不回退到 localhost：静默指向本机比启动失败难查得多。
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("")]
    public void Neither_tier_points_at_localhost_in_deployed_environments(string environmentName)
    {
        var configuration = Configuration();
        var environment = Environment(environmentName);

        Assert.Throws<InvalidOperationException>(() =>
            InternalServiceBaseAddress.Resolve(configuration, environment, Key, Fallback));
        Assert.Throws<InvalidOperationException>(() =>
            InternalServiceBaseAddress.ResolveAllowingTestHost(configuration, environment, Key, Fallback));
    }

    // 异常文案必须点名本宿主**实际**放行的档位：对放行 Testing 的宿主只说 "outside Development"
    // 会把排障的人引向错误结论（以为把环境设成 Testing 就能起来）。
    [Fact]
    public void Failure_message_names_the_environments_that_tier_actually_allows()
    {
        var configuration = Configuration();
        var environment = Environment("Production");

        var strict = Assert.Throws<InvalidOperationException>(() =>
            InternalServiceBaseAddress.Resolve(configuration, environment, Key, Fallback));
        Assert.Equal($"{Key} is required outside Development.", strict.Message);

        var lenient = Assert.Throws<InvalidOperationException>(() =>
            InternalServiceBaseAddress.ResolveAllowingTestHost(configuration, environment, Key, Fallback));
        Assert.Equal($"{Key} is required outside Development/Testing.", lenient.Message);
    }

    // 空串 / 纯空白与「没配」同义：appsettings 里留个空值不该被当成合法基址。
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_configuration_counts_as_unconfigured(string configured)
    {
        var configuration = Configuration((Key, configured));

        Assert.Equal(
            new Uri(Fallback),
            InternalServiceBaseAddress.Resolve(configuration, Environment("Development"), Key, Fallback));
        Assert.Throws<InvalidOperationException>(() =>
            InternalServiceBaseAddress.Resolve(configuration, Environment("Production"), Key, Fallback));
    }

    [Fact]
    public void Malformed_configured_url_fails_fast_at_startup()
    {
        Assert.Throws<UriFormatException>(() =>
            InternalServiceBaseAddress.Resolve(
                Configuration((Key, "not a url")),
                Environment("Production"),
                Key,
                Fallback));
    }

    /// <summary>
    /// **现状留痕（收敛前后一致，本次不改行为）**：以 `/` 开头的路径在 Unix 上被
    /// <c>new Uri(..., UriKind.Absolute)</c> 解析成 <c>file://</c> 而不是抛异常，
    /// 于是配错的 BaseUrl 不在启动时暴露，要等到真正发起跨服务调用才以晦涩的形式失败。
    /// 收敛到单一实现后，若要加「必须是 http/https」的校验，只需改这一处。
    /// </summary>
    [Fact]
    public void Path_like_configured_url_is_currently_accepted_as_a_file_uri()
    {
        var resolved = InternalServiceBaseAddress.Resolve(
            Configuration((Key, "/api/business")),
            Environment("Production"),
            Key,
            Fallback);

        Assert.Equal(Uri.UriSchemeFile, resolved.Scheme);
    }

    private static IConfiguration Configuration(params (string Key, string? Value)[] entries)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(entries.Select(x => new KeyValuePair<string, string?>(x.Key, x.Value)))
            .Build();
    }

    private static IHostEnvironment Environment(string environmentName)
    {
        return new StubHostEnvironment { EnvironmentName = environmentName };
    }

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = string.Empty;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
