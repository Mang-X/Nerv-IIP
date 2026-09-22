using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Erp.Web.Endpoints.DeadLetters;

/// <summary>
/// 本服务死信读取与重放端点的路由前缀。6 个端点的实现只有一份，位于
/// <c>Nerv.IIP.Messaging.CAP.Endpoints</c> 共享模块；这里只声明前缀并落地具体端点类型。
/// </summary>
public sealed class ErpDeadLetterRoutes : IIntegrationEventDeadLetterRouteGroup
{
    public static string RoutePrefix => "/api/business/v1/erp";
}

public sealed class ListErpDeadLettersEndpoint(IIntegrationEventDeadLetterStore deadLetterStore)
    : ListIntegrationEventDeadLettersEndpointBase<ErpDeadLetterRoutes>(deadLetterStore);

public sealed class GetErpDeadLetterMetricsEndpoint(IIntegrationEventDeadLetterStore deadLetterStore)
    : GetIntegrationEventDeadLetterMetricsEndpointBase<ErpDeadLetterRoutes>(deadLetterStore);

public sealed class GetErpDeadLetterEndpoint(IIntegrationEventDeadLetterStore deadLetterStore)
    : GetIntegrationEventDeadLetterEndpointBase<ErpDeadLetterRoutes>(deadLetterStore);

public sealed class ReplayErpDeadLetterEndpoint : ReplayIntegrationEventDeadLetterEndpointBase<ErpDeadLetterRoutes>;

public sealed class ReplayErpDeadLettersEndpoint : ReplayIntegrationEventDeadLettersEndpointBase<ErpDeadLetterRoutes>;

public sealed class IgnoreErpDeadLetterEndpoint(
    IIntegrationEventDeadLetterStore deadLetterStore,
    TimeProvider timeProvider)
    : IgnoreIntegrationEventDeadLetterEndpointBase<ErpDeadLetterRoutes>(deadLetterStore, timeProvider);

/// <summary>本服务死信端点的 facade-coverage 登记面（#3738；facade 由 #3739 交付）。</summary>
public static class ErpDeadLetterEndpointContracts
{
    public static readonly IReadOnlyCollection<IntegrationEventDeadLetterEndpointContract> All =
        IntegrationEventDeadLetterEndpointContracts.For<ErpDeadLetterRoutes>(
            typeof(ListErpDeadLettersEndpoint),
            typeof(GetErpDeadLetterMetricsEndpoint),
            typeof(GetErpDeadLetterEndpoint),
            typeof(ReplayErpDeadLetterEndpoint),
            typeof(ReplayErpDeadLettersEndpoint),
            typeof(IgnoreErpDeadLetterEndpoint));
}
