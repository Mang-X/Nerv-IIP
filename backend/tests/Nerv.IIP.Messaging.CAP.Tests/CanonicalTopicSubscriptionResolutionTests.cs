using Xunit;
using DotNetCore.CAP;
using DotNetCore.CAP.Internal;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Contracts.Maintenance;
using Nerv.IIP.Contracts.Mes;
using Savorboard.CAP.InMemoryMessageQueue;

namespace Nerv.IIP.Messaging.CAP.Tests;

public sealed class CanonicalTopicSubscriptionResolutionTests
{
    private const string UnclaimedTemplate = "nerv-iip.{tenant}.probe.unclaimed.v2";

    public sealed class ProbeConsumer : ICapSubscribe
    {
        public const string PlainTopic = "ProbePlainIntegrationEvent";
        public const string CanonicalTemplate = "nerv-iip.{deployment-profile}.probe.canonical.v2";

        [CapSubscribe(PlainTopic, Group = "probe.plain")]
        public Task HandlePlainAsync() => Task.CompletedTask;

        [CapSubscribe(CanonicalTemplate, Group = "probe.canonical")]
        public Task HandleCanonicalAsync() => Task.CompletedTask;
    }

    public sealed class UnclaimedTemplateConsumer : ICapSubscribe
    {
        [CapSubscribe(UnclaimedTemplate, Group = "probe.unclaimed")]
        public Task HandleAsync() => Task.CompletedTask;
    }

    private sealed class TenantTopicResolver : ICapSubscriptionTopicResolver
    {
        public string Name => nameof(TenantTopicResolver);

        public string Resolve(string topic, string deploymentProfile) =>
            topic.Replace("{tenant}", "acme", StringComparison.Ordinal);
    }

    /// <summary>
    /// 共享解析器与各 Contracts 事件族里的 <c>ResolveSubscriptionTemplate</c> 逐族逐 profile 等值。
    /// 这是下沉「零漂移」的编译期可回归面：只要哪一族改了 token 或归一化规则，这条先红。
    /// </summary>
    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    [InlineData("  StAgInG  ")]
    [InlineData("Testing")]
    public void Shared_resolver_matches_every_contract_family_resolver(string deploymentProfile)
    {
        var resolver = new DeploymentProfileTopicResolver();
        string[] templates =
        [
            AssetUnavailableIntegrationEventTopics.V2Template,
            MesActualTimeIntegrationEventTopics.SettledV2Template,
            MesActualTimeIntegrationEventTopics.VoidedV2Template,
            AssetUnavailableIntegrationEventTopics.V1LegacyAlias,
            MesActualTimeIntegrationEventTopics.SettledV1LegacyAlias,
            MesActualTimeIntegrationEventTopics.VoidedV1LegacyAlias,
        ];

        foreach (var template in templates)
        {
            var shared = resolver.Resolve(template, deploymentProfile);

            Assert.Equal(
                AssetUnavailableIntegrationEventTopics.ResolveSubscriptionTemplate(template, deploymentProfile),
                shared,
                StringComparer.Ordinal);
            Assert.Equal(
                MesActualTimeIntegrationEventTopics.ResolveSubscriptionTemplate(template, deploymentProfile),
                shared,
                StringComparer.Ordinal);
        }
    }

    [Fact]
    public void Selector_expands_canonical_templates_and_leaves_non_canonical_topics_untouched()
    {
        using var provider = BuildProvider("  StAgInG  ", typeof(ProbeConsumer));

        var candidates = provider.GetRequiredService<IConsumerServiceSelector>().SelectCandidates().ToArray();

        var canonical = Assert.Single(
            candidates,
            candidate => candidate.MethodInfo.Name == nameof(ProbeConsumer.HandleCanonicalAsync));
        Assert.Equal("nerv-iip.staging.probe.canonical.v2", canonical.Attribute.Name, StringComparer.Ordinal);
        // CAP 会给 Group 追加 `.{Version}` 后缀；这里断言 selector 没有丢掉声明的 Group 前缀。
        Assert.StartsWith("probe.canonical", canonical.Attribute.Group, StringComparison.Ordinal);

        var plain = Assert.Single(
            candidates,
            candidate => candidate.MethodInfo.Name == nameof(ProbeConsumer.HandlePlainAsync));
        Assert.Equal(ProbeConsumer.PlainTopic, plain.Attribute.Name, StringComparer.Ordinal);
        Assert.StartsWith("probe.plain", plain.Attribute.Group, StringComparison.Ordinal);
    }

    /// <summary>
    /// #3122 的处置口径：没有任何解析器认领的模板占位符 = fail fast，不是静默原样放行。
    /// 静默放行的表现是订阅到字面量占位符串、永远收不到消息、没有任何运行时信号。
    /// </summary>
    [Fact]
    public void Selector_throws_when_no_resolver_claims_a_template_placeholder()
    {
        using var provider = BuildProvider("Development", typeof(UnclaimedTemplateConsumer));

        var exception = Assert.Throws<InvalidOperationException>(
            () => provider.GetRequiredService<IConsumerServiceSelector>().SelectCandidates().ToArray());

        Assert.Contains(UnclaimedTemplate, exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(UnclaimedTemplateConsumer), exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(DeploymentProfileTopicResolver), exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 多族：注册第二个解析器就能认领第二个占位符，共享组件不因此引入任何 Contracts 依赖。
    /// </summary>
    [Fact]
    public void Selector_supports_additional_registered_resolvers()
    {
        using var provider = BuildProvider(
            "Development",
            typeof(UnclaimedTemplateConsumer),
            services => services.AddSingleton<ICapSubscriptionTopicResolver, TenantTopicResolver>());

        var candidate = Assert.Single(
            provider.GetRequiredService<IConsumerServiceSelector>().SelectCandidates());

        Assert.Equal("nerv-iip.acme.probe.unclaimed.v2", candidate.Attribute.Name, StringComparer.Ordinal);
    }

    [Fact]
    public void Registration_entry_replaces_the_default_selector()
    {
        using var provider = BuildProvider("Development", typeof(ProbeConsumer));

        Assert.IsType<DeploymentProfileConsumerServiceSelector>(
            provider.GetRequiredService<IConsumerServiceSelector>());
    }

    private static ServiceProvider BuildProvider(
        string deploymentProfile,
        Type consumerType,
        Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(consumerType);
        services.AddSingleton(typeof(ICapSubscribe), consumerType);
        services.AddCap(options =>
        {
            options.UseInMemoryMessageQueue();
        });
        configure?.Invoke(services);
        services.AddNervIipCanonicalTopicSubscriptions(deploymentProfile);
        return services.BuildServiceProvider();
    }
}
