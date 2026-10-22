using System.Diagnostics.CodeAnalysis;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Maintenance.Web.Endpoints.DeadLetters;

/// <summary>
/// 本服务死信读取与重放端点的路由组。6 个端点的实现只有一份，位于
/// <c>Nerv.IIP.Messaging.CAP.Endpoints</c> 共享模块；前缀取自共享清单
/// <see cref="IntegrationEventDeadLetterServices"/>，与 BusinessGateway 扇出用的是同一个字符串。
/// </summary>
public sealed class MaintenanceDeadLetterRoutes : IIntegrationEventDeadLetterRouteGroup
{
    public static string RoutePrefix => IntegrationEventDeadLetterServices.Maintenance.RoutePrefix;
}

public sealed class ListMaintenanceDeadLettersEndpoint(IIntegrationEventDeadLetterStore deadLetterStore)
    : ListIntegrationEventDeadLettersEndpointBase<MaintenanceDeadLetterRoutes>(deadLetterStore);

public sealed class GetMaintenanceDeadLetterMetricsEndpoint(IIntegrationEventDeadLetterStore deadLetterStore)
    : GetIntegrationEventDeadLetterMetricsEndpointBase<MaintenanceDeadLetterRoutes>(deadLetterStore);

public sealed class GetMaintenanceDeadLetterEndpoint(IIntegrationEventDeadLetterStore deadLetterStore)
    : GetIntegrationEventDeadLetterEndpointBase<MaintenanceDeadLetterRoutes>(deadLetterStore);

public sealed class ReplayMaintenanceDeadLetterEndpoint : ReplayIntegrationEventDeadLetterEndpointBase<MaintenanceDeadLetterRoutes>;

public sealed class ReplayMaintenanceDeadLettersEndpoint : ReplayIntegrationEventDeadLettersEndpointBase<MaintenanceDeadLetterRoutes>;

public sealed class IgnoreMaintenanceDeadLetterEndpoint(
    IIntegrationEventDeadLetterStore deadLetterStore,
    TimeProvider timeProvider)
    : IgnoreIntegrationEventDeadLetterEndpointBase<MaintenanceDeadLetterRoutes>(deadLetterStore, timeProvider);

/// <summary>本服务死信端点的 facade-coverage 登记面（#3738；facade 由 #3739 交付）。</summary>
public static class MaintenanceDeadLetterEndpointContracts
{
    public static readonly IReadOnlyCollection<IntegrationEventDeadLetterEndpointContract> All =
        IntegrationEventDeadLetterEndpointContracts.For<MaintenanceDeadLetterRoutes>(
            "Maintenance",
            typeof(ListMaintenanceDeadLettersEndpoint),
            typeof(GetMaintenanceDeadLetterMetricsEndpoint),
            typeof(GetMaintenanceDeadLetterEndpoint),
            typeof(ReplayMaintenanceDeadLetterEndpoint),
            typeof(ReplayMaintenanceDeadLettersEndpoint),
            typeof(IgnoreMaintenanceDeadLetterEndpoint));

    public static bool TryGet(Type endpointType, [NotNullWhen(true)] out IntegrationEventDeadLetterEndpointContract? contract) =>
        IntegrationEventDeadLetterEndpointContracts.TryGet(All, endpointType, out contract);
}
