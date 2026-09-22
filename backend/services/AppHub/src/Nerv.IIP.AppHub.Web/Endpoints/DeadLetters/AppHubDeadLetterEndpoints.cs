using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.AppHub.Web.Endpoints.DeadLetters;

/// <summary>
/// 本服务死信读取与重放端点的路由前缀。6 个端点的实现只有一份，位于
/// <c>Nerv.IIP.Messaging.CAP.Endpoints</c> 共享模块；这里只声明前缀并落地具体端点类型。
/// </summary>
public sealed class AppHubDeadLetterRoutes : IIntegrationEventDeadLetterRouteGroup
{
    public static string RoutePrefix => "/internal/apphub/v1";
}

public sealed class ListAppHubDeadLettersEndpoint(IIntegrationEventDeadLetterStore deadLetterStore)
    : ListIntegrationEventDeadLettersEndpointBase<AppHubDeadLetterRoutes>(deadLetterStore);

public sealed class GetAppHubDeadLetterMetricsEndpoint(IIntegrationEventDeadLetterStore deadLetterStore)
    : GetIntegrationEventDeadLetterMetricsEndpointBase<AppHubDeadLetterRoutes>(deadLetterStore);

public sealed class GetAppHubDeadLetterEndpoint(IIntegrationEventDeadLetterStore deadLetterStore)
    : GetIntegrationEventDeadLetterEndpointBase<AppHubDeadLetterRoutes>(deadLetterStore);

public sealed class ReplayAppHubDeadLetterEndpoint : ReplayIntegrationEventDeadLetterEndpointBase<AppHubDeadLetterRoutes>;

public sealed class ReplayAppHubDeadLettersEndpoint : ReplayIntegrationEventDeadLettersEndpointBase<AppHubDeadLetterRoutes>;

public sealed class IgnoreAppHubDeadLetterEndpoint(
    IIntegrationEventDeadLetterStore deadLetterStore,
    TimeProvider timeProvider)
    : IgnoreIntegrationEventDeadLetterEndpointBase<AppHubDeadLetterRoutes>(deadLetterStore, timeProvider);
