using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Nerv.IIP.Ops.Web.Tests;

/// <summary>
/// 钉住 <see cref="OpsTestHostIsolation"/> 的两侧：CAP 确实注册了后台宿主服务（否则隔离是空操作、
/// NERV-733 的竞态会悄悄回来），以及隔离只摘 CAP 那一条（否则连 Web 服务器本身都会被删掉）。
/// </summary>
[Collection(WebApplicationFactoryCollection.Name)]
public sealed class OpsTestHostIsolationTests
{
    [Fact]
    public void Postgres_profile_host_registers_one_cap_hosted_service_and_isolation_removes_exactly_it()
    {
        List<ServiceDescriptor> beforeIsolation = [];
        List<ServiceDescriptor> afterIsolation = [];
        var removed = -1;

        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                ConfigurePostgresProfile(builder);
                builder.ConfigureServices(beforeIsolation.AddRange);
                builder.ConfigureServices(services => removed = OpsTestHostIsolation.RemoveCapBackgroundProcessing(services));
                builder.ConfigureServices(afterIsolation.AddRange);
            });

        // 触发宿主构建，让三个 ConfigureServices 回调按序执行。
        _ = factory.Services;

        // 上界与下界都要钉：CAP 若改成注册两条后台服务，只断言「摘干净了」不会红，但另一条会继续跑。
        Assert.Single(beforeIsolation, OpsTestHostIsolation.IsCapOwnedHostedService);
        Assert.Equal(1, removed);
        Assert.DoesNotContain(afterIsolation, OpsTestHostIsolation.IsCapOwnedHostedService);

        // 变异检测：把判定放宽成「删掉全部 IHostedService」时，这一行会红——GenericWebHostService
        // （Web 服务器）与 Ops 自己的 lease reaper 都必须留在宿主里。
        Assert.Equal(
            beforeIsolation.Count(x => x.ServiceType == typeof(IHostedService)) - 1,
            afterIsolation.Count(x => x.ServiceType == typeof(IHostedService)));
        Assert.Contains(
            afterIsolation,
            x => x.ImplementationType?.Name == "GenericWebHostService");
    }

    [Fact]
    public async Task Isolated_postgres_profile_host_starts_and_disposes_without_database_or_broker()
    {
        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                ConfigurePostgresProfile(builder);
                builder.WithoutCapBackgroundProcessing();
            });

        try
        {
            var client = factory.CreateClient();

            // /health 既不碰 DB 也不碰 broker：能起来就说明摘掉 CAP 后台生命周期之后，
            // PostgreSQL 档的测试宿主不再需要任何可达的外部依赖。
            Assert.Equal("Healthy", await client.GetStringAsync("/health"));
        }
        finally
        {
            // 释放路径本身是被测面：NERV-733 的症状恰好只在 DisposeAsync() 里抛出。
            // 竞态无法被确定性复现，因此这里钉住的是「隔离后的宿主起得来、也停得干净」，
            // 而不是「竞态已复现并消失」。
            await factory.DisposeAsync();
        }
    }

    private static void ConfigurePostgresProfile(IWebHostBuilder builder)
    {
        builder.UseSetting("Persistence:Provider", "PostgreSQL");
        builder.UseSetting(
            "ConnectionStrings:OpsDb",
            "Host=ops-isolation-test.invalid;Database=ops_isolation_test;Username=ops_test;Password=ops_test");
        builder.UseSetting("Ops:LeaseReaper:Enabled", "false");
    }
}
