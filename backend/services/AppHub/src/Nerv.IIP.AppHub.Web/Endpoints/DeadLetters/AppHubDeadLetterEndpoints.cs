using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.AppHub.Web.Endpoints.DeadLetters;

/// <summary>
/// 本服务死信读取与重放端点的路由组。6 个端点的实现只有一份，位于
/// <c>Nerv.IIP.Messaging.CAP.Endpoints</c> 共享模块；前缀取自共享清单
/// <see cref="IntegrationEventDeadLetterServices"/>，与 BusinessGateway 扇出用的是同一个字符串。
/// </summary>
public sealed class AppHubDeadLetterRoutes : IIntegrationEventDeadLetterRouteGroup
{
    public static string RoutePrefix => IntegrationEventDeadLetterServices.AppHub.RoutePrefix;
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
