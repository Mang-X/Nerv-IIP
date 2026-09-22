using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Scheduling.Web.Endpoints.DeadLetters;

/// <summary>
/// 本服务死信读取与重放端点的路由前缀。6 个端点的实现只有一份，位于
/// <c>Nerv.IIP.Messaging.CAP.Endpoints</c> 共享模块；这里只声明前缀并落地具体端点类型。
/// </summary>
public sealed class SchedulingDeadLetterRoutes : IIntegrationEventDeadLetterRouteGroup
{
    public static string RoutePrefix => "/api/business/v1/scheduling";
}

public sealed class ListSchedulingDeadLettersEndpoint(IIntegrationEventDeadLetterStore deadLetterStore)
    : ListIntegrationEventDeadLettersEndpointBase<SchedulingDeadLetterRoutes>(deadLetterStore);

public sealed class GetSchedulingDeadLetterMetricsEndpoint(IIntegrationEventDeadLetterStore deadLetterStore)
    : GetIntegrationEventDeadLetterMetricsEndpointBase<SchedulingDeadLetterRoutes>(deadLetterStore);

public sealed class GetSchedulingDeadLetterEndpoint(IIntegrationEventDeadLetterStore deadLetterStore)
    : GetIntegrationEventDeadLetterEndpointBase<SchedulingDeadLetterRoutes>(deadLetterStore);

public sealed class ReplaySchedulingDeadLetterEndpoint : ReplayIntegrationEventDeadLetterEndpointBase<SchedulingDeadLetterRoutes>;

public sealed class ReplaySchedulingDeadLettersEndpoint : ReplayIntegrationEventDeadLettersEndpointBase<SchedulingDeadLetterRoutes>;

public sealed class IgnoreSchedulingDeadLetterEndpoint(
    IIntegrationEventDeadLetterStore deadLetterStore,
    TimeProvider timeProvider)
    : IgnoreIntegrationEventDeadLetterEndpointBase<SchedulingDeadLetterRoutes>(deadLetterStore, timeProvider);

/// <summary>本服务死信端点的 facade-coverage 登记面（#3738；facade 由 #3739 交付）。</summary>
public static class SchedulingDeadLetterEndpointContracts
{
    public static readonly IReadOnlyCollection<IntegrationEventDeadLetterEndpointContract> All =
        IntegrationEventDeadLetterEndpointContracts.For<SchedulingDeadLetterRoutes>(
            "Scheduling",
            typeof(ListSchedulingDeadLettersEndpoint),
            typeof(GetSchedulingDeadLetterMetricsEndpoint),
            typeof(GetSchedulingDeadLetterEndpoint),
            typeof(ReplaySchedulingDeadLetterEndpoint),
            typeof(ReplaySchedulingDeadLettersEndpoint),
            typeof(IgnoreSchedulingDeadLetterEndpoint));
}
