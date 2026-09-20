using DotNetCore.CAP.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Mes.Web.Tests;

/// <summary>
/// #3179：钉住「MES 的生产 CAP 接线必须安装共享 canonical topic selector」这条不变量。
///
/// MES 早就有可测入口 <c>AddMesCapIntegrationEvents</c>，<c>MesCapSubscriptionTests</c> 也顺带
/// 观察到了展开后的 v2 topic；但那条用例的主题是「订阅者被发现」，共享入口是否被调用只是它的
/// 副产物。本仓教训是「提供了可测入口 ≠ 有测试断言它被调用了」，所以这里把不变量单列成命名断言，
/// 与 ERP / 排产域同形，让三个服务在同一判据上可比。
/// </summary>
public sealed class MesCanonicalTopicSubscriptionRegistrationTests
{
    private const string DeploymentProfile = "Development";

    // 期望值写成裸字面量：不引用 Contracts 里的模板常量，避免「断言与被测实现同源」的同义反复。
    private const string AssetUnavailableV2DevelopmentTopic =
        "nerv-iip.development.business-maintenance.maintenance.asset-unavailable.v2";

    [Fact]
    public void Mes_production_cap_registration_installs_the_shared_canonical_topic_selector()
    {
        using var provider = BuildProductionCapContainer(
            nameof(Mes_production_cap_registration_installs_the_shared_canonical_topic_selector));

        var selector = provider.GetRequiredService<IConsumerServiceSelector>();

        Assert.IsType<DeploymentProfileConsumerServiceSelector>(selector);
    }

    [Fact]
    public void Mes_production_cap_subscriptions_expand_the_deployment_profile_placeholder()
    {
        using var provider = BuildProductionCapContainer(
            nameof(Mes_production_cap_subscriptions_expand_the_deployment_profile_placeholder));
        var selector = provider.GetRequiredService<IConsumerServiceSelector>();

        var topics = selector.SelectCandidates().Select(candidate => candidate.Attribute.Name).ToArray();

        Assert.Contains(AssetUnavailableV2DevelopmentTopic, topics);
    }

    private static ServiceProvider BuildProductionCapContainer(string databaseName)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Messaging:Provider"] = "InMemory",
                ["Cap:Version"] = "test-mes-canonical-topic-registration",
            })
            .Build();

        services.AddLogging();
        services.AddMediatR(options => options.RegisterServicesFromAssembly(typeof(global::Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));

        // 生产参数：isTesting 取默认的 false，与 Program.cs 在非 Testing 环境下的取值一致。
        services.AddMesCapIntegrationEvents(configuration, DeploymentProfile);

        return services.BuildServiceProvider();
    }
}
