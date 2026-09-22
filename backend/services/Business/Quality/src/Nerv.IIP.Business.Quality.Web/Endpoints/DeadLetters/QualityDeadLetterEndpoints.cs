using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Quality.Web.Endpoints.DeadLetters;

/// <summary>
/// 本服务死信读取与重放端点的路由前缀。6 个端点的实现只有一份，位于
/// <c>Nerv.IIP.Messaging.CAP.Endpoints</c> 共享模块；这里只声明前缀并落地具体端点类型。
/// </summary>
public sealed class QualityDeadLetterRoutes : IIntegrationEventDeadLetterRouteGroup
{
    public static string RoutePrefix => "/api/business/v1/quality";
}

public sealed class ListQualityDeadLettersEndpoint(IIntegrationEventDeadLetterStore deadLetterStore)
    : ListIntegrationEventDeadLettersEndpointBase<QualityDeadLetterRoutes>(deadLetterStore);

public sealed class GetQualityDeadLetterMetricsEndpoint(IIntegrationEventDeadLetterStore deadLetterStore)
    : GetIntegrationEventDeadLetterMetricsEndpointBase<QualityDeadLetterRoutes>(deadLetterStore);

public sealed class GetQualityDeadLetterEndpoint(IIntegrationEventDeadLetterStore deadLetterStore)
    : GetIntegrationEventDeadLetterEndpointBase<QualityDeadLetterRoutes>(deadLetterStore);

public sealed class ReplayQualityDeadLetterEndpoint : ReplayIntegrationEventDeadLetterEndpointBase<QualityDeadLetterRoutes>;

public sealed class ReplayQualityDeadLettersEndpoint : ReplayIntegrationEventDeadLettersEndpointBase<QualityDeadLetterRoutes>;

public sealed class IgnoreQualityDeadLetterEndpoint(
    IIntegrationEventDeadLetterStore deadLetterStore,
    TimeProvider timeProvider)
    : IgnoreIntegrationEventDeadLetterEndpointBase<QualityDeadLetterRoutes>(deadLetterStore, timeProvider);

/// <summary>本服务死信端点的 facade-coverage 登记面（#3738；facade 由 #3739 交付）。</summary>
public static class QualityDeadLetterEndpointContracts
{
    public static readonly IReadOnlyCollection<IntegrationEventDeadLetterEndpointContract> All =
        IntegrationEventDeadLetterEndpointContracts.For<QualityDeadLetterRoutes>(
            "BusinessQuality",
            typeof(ListQualityDeadLettersEndpoint),
            typeof(GetQualityDeadLetterMetricsEndpoint),
            typeof(GetQualityDeadLetterEndpoint),
            typeof(ReplayQualityDeadLetterEndpoint),
            typeof(ReplayQualityDeadLettersEndpoint),
            typeof(IgnoreQualityDeadLetterEndpoint));
}
