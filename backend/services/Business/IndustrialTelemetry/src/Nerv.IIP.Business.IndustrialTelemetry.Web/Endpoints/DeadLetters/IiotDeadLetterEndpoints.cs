using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Endpoints.DeadLetters;

/// <summary>
/// 本服务死信读取与重放端点的路由前缀。6 个端点的实现只有一份，位于
/// <c>Nerv.IIP.Messaging.CAP.Endpoints</c> 共享模块；这里只声明前缀并落地具体端点类型。
/// </summary>
public sealed class IiotDeadLetterRoutes : IIntegrationEventDeadLetterRouteGroup
{
    public static string RoutePrefix => "/api/business/v1/iiot";
}

public sealed class ListBusinessIiotDeadLettersEndpoint(IIntegrationEventDeadLetterStore deadLetterStore)
    : ListIntegrationEventDeadLettersEndpointBase<IiotDeadLetterRoutes>(deadLetterStore);

public sealed class GetBusinessIiotDeadLetterMetricsEndpoint(IIntegrationEventDeadLetterStore deadLetterStore)
    : GetIntegrationEventDeadLetterMetricsEndpointBase<IiotDeadLetterRoutes>(deadLetterStore);

public sealed class GetBusinessIiotDeadLetterEndpoint(IIntegrationEventDeadLetterStore deadLetterStore)
    : GetIntegrationEventDeadLetterEndpointBase<IiotDeadLetterRoutes>(deadLetterStore);

public sealed class ReplayBusinessIiotDeadLetterEndpoint : ReplayIntegrationEventDeadLetterEndpointBase<IiotDeadLetterRoutes>;

public sealed class ReplayBusinessIiotDeadLettersEndpoint : ReplayIntegrationEventDeadLettersEndpointBase<IiotDeadLetterRoutes>;

public sealed class IgnoreBusinessIiotDeadLetterEndpoint(
    IIntegrationEventDeadLetterStore deadLetterStore,
    TimeProvider timeProvider)
    : IgnoreIntegrationEventDeadLetterEndpointBase<IiotDeadLetterRoutes>(deadLetterStore, timeProvider);

/// <summary>本服务死信端点的 facade-coverage 登记面（#3738；facade 由 #3739 交付）。</summary>
public static class IiotDeadLetterEndpointContracts
{
    public static readonly IReadOnlyCollection<IntegrationEventDeadLetterEndpointContract> All =
        IntegrationEventDeadLetterEndpointContracts.For<IiotDeadLetterRoutes>(
            typeof(ListBusinessIiotDeadLettersEndpoint),
            typeof(GetBusinessIiotDeadLetterMetricsEndpoint),
            typeof(GetBusinessIiotDeadLetterEndpoint),
            typeof(ReplayBusinessIiotDeadLetterEndpoint),
            typeof(ReplayBusinessIiotDeadLettersEndpoint),
            typeof(IgnoreBusinessIiotDeadLetterEndpoint));
}
