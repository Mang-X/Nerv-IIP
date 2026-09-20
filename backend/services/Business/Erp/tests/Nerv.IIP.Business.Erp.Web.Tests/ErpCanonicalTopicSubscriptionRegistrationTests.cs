using DotNetCore.CAP.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Erp.Web.Tests;

/// <summary>
/// #3179：钉住「ERP 的生产 CAP 接线必须安装共享 canonical topic selector」这条不变量。
///
/// 判据形态是**行为断言**而不是源码文本扫描：按生产参数把 ERP 的 CAP 接线注册进一个真实
/// <see cref="IServiceProvider"/>，再解析 CAP 的 consumer selector 接口观察结果。
/// 改写法、改命名、换文件都绕不过去；一旦
/// <c>AddErpCapIntegrationEvents</c> 里对 <c>AddNervIipCanonicalTopicSubscriptions</c> 的调用消失，
/// 容器就会回落到 CAP 默认 selector，两条断言各自独立地红。
///
/// 文本扫描护栏在本仓已有反例（PR #3214：三轮八种绕法、护栏膨胀成手搓词法分析器），本条不走那条路。
/// </summary>
public sealed class ErpCanonicalTopicSubscriptionRegistrationTests
{
    private const string DeploymentProfile = "Development";

    // 期望值写成裸字面量：不引用 Contracts 里的模板常量，避免「断言与被测实现同源」的同义反复。
    private const string SettledV2DevelopmentTopic =
        "nerv-iip.development.business-mes.mes.operation-actual-time-settled.v2";
    private const string VoidedV2DevelopmentTopic =
        "nerv-iip.development.business-mes.mes.operation-actual-time-settlement-voided.v2";

    [Fact]
    public void Erp_production_cap_registration_installs_the_shared_canonical_topic_selector()
    {
        using var provider = BuildProductionCapContainer(
            nameof(Erp_production_cap_registration_installs_the_shared_canonical_topic_selector));

        var selector = provider.GetRequiredService<IConsumerServiceSelector>();

        Assert.IsType<DeploymentProfileConsumerServiceSelector>(selector);
    }

    [Fact]
    public void Erp_production_cap_subscriptions_expand_the_deployment_profile_placeholder()
    {
        using var provider = BuildProductionCapContainer(
            nameof(Erp_production_cap_subscriptions_expand_the_deployment_profile_placeholder));
        var selector = provider.GetRequiredService<IConsumerServiceSelector>();

        var topics = selector.SelectCandidates().Select(candidate => candidate.Attribute.Name).ToArray();

        Assert.Contains(SettledV2DevelopmentTopic, topics);
        Assert.Contains(VoidedV2DevelopmentTopic, topics);
    }

    private static ServiceProvider BuildProductionCapContainer(string databaseName)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Messaging:Provider"] = "InMemory",
                ["Cap:Version"] = "test-erp-canonical-topic-registration",
            })
            .Build();

        services.AddLogging();
        services.AddMediatR(options => options.RegisterServicesFromAssembly(typeof(global::Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));

        // 生产参数：isTesting 取默认的 false，与 Program.cs 在非 Testing 环境下的取值一致。
        services.AddErpCapIntegrationEvents(configuration, DeploymentProfile);

        return services.BuildServiceProvider();
    }
}
