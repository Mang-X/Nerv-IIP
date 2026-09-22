using System.Diagnostics.CodeAnalysis;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Wms.Web.Endpoints.DeadLetters;

/// <summary>
/// 本服务死信读取与重放端点的路由前缀。6 个端点的实现只有一份，位于
/// <c>Nerv.IIP.Messaging.CAP.Endpoints</c> 共享模块；这里只声明前缀并落地具体端点类型。
/// </summary>
public sealed class WmsDeadLetterRoutes : IIntegrationEventDeadLetterRouteGroup
{
    public static string RoutePrefix => "/api/business/v1/wms";
}

public sealed class ListWmsDeadLettersEndpoint(IIntegrationEventDeadLetterStore deadLetterStore)
    : ListIntegrationEventDeadLettersEndpointBase<WmsDeadLetterRoutes>(deadLetterStore);

public sealed class GetWmsDeadLetterMetricsEndpoint(IIntegrationEventDeadLetterStore deadLetterStore)
    : GetIntegrationEventDeadLetterMetricsEndpointBase<WmsDeadLetterRoutes>(deadLetterStore);

public sealed class GetWmsDeadLetterEndpoint(IIntegrationEventDeadLetterStore deadLetterStore)
    : GetIntegrationEventDeadLetterEndpointBase<WmsDeadLetterRoutes>(deadLetterStore);

public sealed class ReplayWmsDeadLetterEndpoint : ReplayIntegrationEventDeadLetterEndpointBase<WmsDeadLetterRoutes>;

public sealed class ReplayWmsDeadLettersEndpoint : ReplayIntegrationEventDeadLettersEndpointBase<WmsDeadLetterRoutes>;

public sealed class IgnoreWmsDeadLetterEndpoint(
    IIntegrationEventDeadLetterStore deadLetterStore,
    TimeProvider timeProvider)
    : IgnoreIntegrationEventDeadLetterEndpointBase<WmsDeadLetterRoutes>(deadLetterStore, timeProvider);

/// <summary>本服务死信端点的 facade-coverage 登记面（#3738；facade 由 #3739 交付）。</summary>
public static class WmsDeadLetterEndpointContracts
{
    public static readonly IReadOnlyCollection<IntegrationEventDeadLetterEndpointContract> All =
        IntegrationEventDeadLetterEndpointContracts.For<WmsDeadLetterRoutes>(
            "Wms",
            typeof(ListWmsDeadLettersEndpoint),
            typeof(GetWmsDeadLetterMetricsEndpoint),
            typeof(GetWmsDeadLetterEndpoint),
            typeof(ReplayWmsDeadLetterEndpoint),
            typeof(ReplayWmsDeadLettersEndpoint),
            typeof(IgnoreWmsDeadLetterEndpoint));

    public static bool TryGet(Type endpointType, [NotNullWhen(true)] out IntegrationEventDeadLetterEndpointContract? contract) =>
        IntegrationEventDeadLetterEndpointContracts.TryGet(All, endpointType, out contract);
}
