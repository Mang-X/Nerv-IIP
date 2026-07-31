using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.ServiceAuth.Tests;

/// <summary>
/// 下游基址解析的权威口径。这段逻辑原本在 14 个 Program.cs 里各抄一份并且已经抄漂
/// （6 份只放行 Development、8 份还放行 Testing），收敛后由本组用例锁定单一语义。
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
    public void Configured_base_url_wins_in_every_environment(string environmentName)
    {
        var resolved = InternalServiceBaseAddress.Resolve(
            Configuration((Key, "https://master-data.internal:8443")),
            Environment(environmentName),
            Key,
            Fallback);

        Assert.Equal(new Uri("https://master-data.internal:8443"), resolved);
    }

    // Testing 与 Development 同档：集成测试宿主起在 Testing 下，不放行就得为每个下游逐一喂配置。
    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void Development_and_testing_fall_back_to_the_local_default(string environmentName)
    {
        var resolved = InternalServiceBaseAddress.Resolve(
            Configuration(),
            Environment(environmentName),
            Key,
            Fallback);

        Assert.Equal(new Uri(Fallback), resolved);
    }

    // 生产类环境绝不回退到 localhost：静默指向本机比启动失败难查得多。
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("")]
    public void Other_environments_fail_fast_instead_of_pointing_at_localhost(string environmentName)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            InternalServiceBaseAddress.Resolve(Configuration(), Environment(environmentName), Key, Fallback));

        Assert.Contains(Key, exception.Message, StringComparison.Ordinal);
    }

    // 空串 / 纯空白与「没配」同义：appsettings 里留个空值不该被当成合法基址。
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_configuration_counts_as_unconfigured(string configured)
    {
        Assert.Equal(
            new Uri(Fallback),
            InternalServiceBaseAddress.Resolve(Configuration((Key, configured)), Environment("Development"), Key, Fallback));

        Assert.Throws<InvalidOperationException>(() =>
            InternalServiceBaseAddress.Resolve(Configuration((Key, configured)), Environment("Production"), Key, Fallback));
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
