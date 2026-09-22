using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.DemandPlanning.Web.Endpoints.DeadLetters;

/// <summary>
/// 本服务死信读取与重放端点的路由前缀。6 个端点的实现只有一份，位于
/// <c>Nerv.IIP.Messaging.CAP.Endpoints</c> 共享模块；这里只声明前缀并落地具体端点类型。
/// </summary>
public sealed class PlanningDeadLetterRoutes : IIntegrationEventDeadLetterRouteGroup
{
    public static string RoutePrefix => "/api/business/v1/planning";
}

public sealed class ListPlanningDeadLettersEndpoint(IIntegrationEventDeadLetterStore deadLetterStore)
    : ListIntegrationEventDeadLettersEndpointBase<PlanningDeadLetterRoutes>(deadLetterStore);

public sealed class GetPlanningDeadLetterMetricsEndpoint(IIntegrationEventDeadLetterStore deadLetterStore)
    : GetIntegrationEventDeadLetterMetricsEndpointBase<PlanningDeadLetterRoutes>(deadLetterStore);

public sealed class GetPlanningDeadLetterEndpoint(IIntegrationEventDeadLetterStore deadLetterStore)
    : GetIntegrationEventDeadLetterEndpointBase<PlanningDeadLetterRoutes>(deadLetterStore);

public sealed class ReplayPlanningDeadLetterEndpoint : ReplayIntegrationEventDeadLetterEndpointBase<PlanningDeadLetterRoutes>;

public sealed class ReplayPlanningDeadLettersEndpoint : ReplayIntegrationEventDeadLettersEndpointBase<PlanningDeadLetterRoutes>;

public sealed class IgnorePlanningDeadLetterEndpoint(
    IIntegrationEventDeadLetterStore deadLetterStore,
    TimeProvider timeProvider)
    : IgnoreIntegrationEventDeadLetterEndpointBase<PlanningDeadLetterRoutes>(deadLetterStore, timeProvider);

/// <summary>本服务死信端点的 facade-coverage 登记面（#3738；facade 由 #3739 交付）。</summary>
public static class PlanningDeadLetterEndpointContracts
{
    public static readonly IReadOnlyCollection<IntegrationEventDeadLetterEndpointContract> All =
        IntegrationEventDeadLetterEndpointContracts.For<PlanningDeadLetterRoutes>(
            typeof(ListPlanningDeadLettersEndpoint),
            typeof(GetPlanningDeadLetterMetricsEndpoint),
            typeof(GetPlanningDeadLetterEndpoint),
            typeof(ReplayPlanningDeadLetterEndpoint),
            typeof(ReplayPlanningDeadLettersEndpoint),
            typeof(IgnorePlanningDeadLetterEndpoint));
}
