using DotNetCore.CAP;
using DotNetCore.CAP.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Nerv.IIP.Messaging.CAP;

/// <summary>
/// 把一条 <c>[CapSubscribe]</c> 上声明的 canonical topic **模板**解析成本进程实际订阅的 topic。
///
/// ADR 0011 的 canonical topic 是带占位符的模板（今天全仓只有一个占位符
/// <c>{deployment-profile}</c>）；订阅面必须在 CAP 建立消费者之前把占位符换成本进程的部署 profile，
/// 否则会订阅到未展开的模板字符串、消息永远收不到。
///
/// 这是一个**跨事件族**的抽象：解析器按注册顺序依次作用于同一个 topic，
/// 任何一族都不需要自己的 selector 副本。
/// </summary>
public interface ICapSubscriptionTopicResolver
{
    /// <summary>诊断用名字；解析失败时会出现在异常消息里，用来指认「哪些解析器看过这条模板」。</summary>
    string Name { get; }

    /// <summary>认领并展开自己负责的占位符；不认领时必须原样返回 <paramref name="topic"/>。</summary>
    string Resolve(string topic, string deploymentProfile);
}

/// <summary>
/// 展开 <c>{deployment-profile}</c> 占位符。
///
/// 归一化规则（<c>Trim().ToLowerInvariant()</c>）与 Ordinal 比较口径与各 Contracts 事件族里的
/// <c>ResolveSubscriptionTemplate</c> 完全一致，由 <c>Nerv.IIP.Messaging.CAP.Tests</c> 的
/// 差分测试逐族钉住；本类型不引用任何 <c>Nerv.IIP.Contracts.*</c>，因此共享基础设施不会因为
/// 「认识某个领域的 topic 族」而反向依赖领域契约。
/// </summary>
public sealed class DeploymentProfileTopicResolver : ICapSubscriptionTopicResolver
{
    public const string DeploymentProfileToken = "{deployment-profile}";

    public string Name => nameof(DeploymentProfileTopicResolver);

    public string Resolve(string topic, string deploymentProfile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentException.ThrowIfNullOrWhiteSpace(deploymentProfile);

        return topic.Contains(DeploymentProfileToken, StringComparison.Ordinal)
            ? topic.Replace(
                DeploymentProfileToken,
                deploymentProfile.Trim().ToLowerInvariant(),
                StringComparison.Ordinal)
            : topic;
    }
}

/// <summary>
/// 全仓唯一的 <see cref="IConsumerServiceSelector"/> 替换实现。
///
/// 处置口径（#3122）——对「解析后仍带占位符」的模板 **fail fast**，而不是静默原样放行：
///   * 不含 <c>{</c> 的 topic（例如 <c>[CapSubscribe(nameof(XxxIntegrationEvent))]</c> 这类
///     非 canonical 订阅）本来就没有东西要展开，原样通过，不是错误；
///   * 含 <c>{</c> 却没有任何解析器认领 = 这个进程会去订阅一个字面量占位符串，
///     它一定收不到任何消息，而且没有任何运行时信号。这不是可以放行的状态，
///     所以在 <c>SelectCandidates()</c>（进程启动、CAP bootstrap 阶段）直接抛。
///
/// 判据落在「占位符」而不是「事件族」上：一律报错会误伤大量合法的非 canonical 订阅，
/// 一律放行会把「漏注册解析器」变成静默失败。
/// </summary>
public sealed class DeploymentProfileConsumerServiceSelector : ConsumerServiceSelector
{
    private const char PlaceholderOpen = '{';

    private readonly string _deploymentProfile;
    private readonly IReadOnlyList<ICapSubscriptionTopicResolver> _resolvers;

    public DeploymentProfileConsumerServiceSelector(
        IServiceProvider serviceProvider,
        string deploymentProfile,
        IEnumerable<ICapSubscriptionTopicResolver> resolvers)
        : base(serviceProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deploymentProfile);
        ArgumentNullException.ThrowIfNull(resolvers);

        _deploymentProfile = deploymentProfile;
        _resolvers = resolvers.ToArray();
    }

    protected override IEnumerable<ConsumerExecutorDescriptor> FindConsumersFromInterfaceTypes(IServiceProvider provider) =>
        base.FindConsumersFromInterfaceTypes(provider).Select(ResolveDeploymentProfile);

    protected override IEnumerable<ConsumerExecutorDescriptor> FindConsumersFromControllerTypes() =>
        base.FindConsumersFromControllerTypes().Select(ResolveDeploymentProfile);

    private ConsumerExecutorDescriptor ResolveDeploymentProfile(ConsumerExecutorDescriptor descriptor)
    {
        var declaredTopic = descriptor.Attribute.Name;
        var resolvedTopic = declaredTopic;

        foreach (var resolver in _resolvers)
        {
            resolvedTopic = resolver.Resolve(resolvedTopic, _deploymentProfile);
        }

        if (resolvedTopic.Contains(PlaceholderOpen))
        {
            throw new InvalidOperationException(
                $"CAP subscription topic '{declaredTopic}' declared by "
                + $"{descriptor.ImplTypeInfo.FullName}.{descriptor.MethodInfo.Name} still contains an unresolved "
                + $"template placeholder after deployment-profile resolution (resolved to '{resolvedTopic}', "
                + $"deployment profile '{_deploymentProfile}'). Registered resolvers: "
                + $"{(_resolvers.Count == 0 ? "<none>" : string.Join(", ", _resolvers.Select(resolver => resolver.Name)))}. "
                + "Register an ICapSubscriptionTopicResolver that claims this placeholder; "
                + "subscribing to an unexpanded template silently receives nothing.");
        }

        if (string.Equals(resolvedTopic, declaredTopic, StringComparison.Ordinal))
        {
            return descriptor;
        }

        descriptor.Attribute = new CapSubscribeAttribute(resolvedTopic, descriptor.Attribute.IsPartial)
        {
            Group = descriptor.Attribute.Group,
            GroupConcurrent = descriptor.Attribute.GroupConcurrent,
        };
        return descriptor;
    }
}

/// <summary>
/// 共享注册入口。这是全仓唯一被允许替换 <see cref="IConsumerServiceSelector"/> 的位置，
/// 由 <c>Nerv.IIP.Messaging.CAP.Tests</c> 的收敛门禁按「谁替换了这个接口」这个身份守住
/// （不按类名、不按文件名——#3122 的第三份副本正是靠改名从 grep 里消失的）。
/// </summary>
public static class CanonicalTopicSubscriptionServiceCollectionExtensions
{
    /// <param name="deploymentProfile">
    /// 本进程的部署 profile，取宿主的 <c>EnvironmentName</c>；与发布侧写进 canonical topic 的那一段同源。
    /// </param>
    public static IServiceCollection AddNervIipCanonicalTopicSubscriptions(
        this IServiceCollection services,
        string deploymentProfile)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(deploymentProfile);

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ICapSubscriptionTopicResolver, DeploymentProfileTopicResolver>());
        services.Replace(ServiceDescriptor.Singleton<IConsumerServiceSelector>(serviceProvider =>
            new DeploymentProfileConsumerServiceSelector(
                serviceProvider,
                deploymentProfile,
                serviceProvider.GetServices<ICapSubscriptionTopicResolver>())));

        return services;
    }
}
