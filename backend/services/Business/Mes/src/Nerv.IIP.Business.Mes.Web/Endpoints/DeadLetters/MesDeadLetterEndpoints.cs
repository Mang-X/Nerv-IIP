using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Mes.Web.Endpoints.DeadLetters;

/// <summary>
/// 本服务死信读取与重放端点的路由前缀。6 个端点的实现只有一份，位于
/// <c>Nerv.IIP.Messaging.CAP.Endpoints</c> 共享模块；这里只声明前缀并落地具体端点类型。
/// </summary>
public sealed class MesDeadLetterRoutes : IIntegrationEventDeadLetterRouteGroup
{
    public static string RoutePrefix => "/api/business/v1/mes";
}

public sealed class ListBusinessMesDeadLettersEndpoint(IIntegrationEventDeadLetterStore deadLetterStore)
    : ListIntegrationEventDeadLettersEndpointBase<MesDeadLetterRoutes>(deadLetterStore);

public sealed class GetBusinessMesDeadLetterMetricsEndpoint(IIntegrationEventDeadLetterStore deadLetterStore)
    : GetIntegrationEventDeadLetterMetricsEndpointBase<MesDeadLetterRoutes>(deadLetterStore);

public sealed class GetBusinessMesDeadLetterEndpoint(IIntegrationEventDeadLetterStore deadLetterStore)
    : GetIntegrationEventDeadLetterEndpointBase<MesDeadLetterRoutes>(deadLetterStore);

public sealed class ReplayBusinessMesDeadLetterEndpoint : ReplayIntegrationEventDeadLetterEndpointBase<MesDeadLetterRoutes>;

public sealed class ReplayBusinessMesDeadLettersEndpoint : ReplayIntegrationEventDeadLettersEndpointBase<MesDeadLetterRoutes>;

public sealed class IgnoreBusinessMesDeadLetterEndpoint(
    IIntegrationEventDeadLetterStore deadLetterStore,
    TimeProvider timeProvider)
    : IgnoreIntegrationEventDeadLetterEndpointBase<MesDeadLetterRoutes>(deadLetterStore, timeProvider);

/// <summary>本服务死信端点的 facade-coverage 登记面（#3738；facade 由 #3739 交付）。</summary>
public static class MesDeadLetterEndpointContracts
{
    public static readonly IReadOnlyCollection<IntegrationEventDeadLetterEndpointContract> All =
        IntegrationEventDeadLetterEndpointContracts.For<MesDeadLetterRoutes>(
            typeof(ListBusinessMesDeadLettersEndpoint),
            typeof(GetBusinessMesDeadLetterMetricsEndpoint),
            typeof(GetBusinessMesDeadLetterEndpoint),
            typeof(ReplayBusinessMesDeadLetterEndpoint),
            typeof(ReplayBusinessMesDeadLettersEndpoint),
            typeof(IgnoreBusinessMesDeadLetterEndpoint));
}
