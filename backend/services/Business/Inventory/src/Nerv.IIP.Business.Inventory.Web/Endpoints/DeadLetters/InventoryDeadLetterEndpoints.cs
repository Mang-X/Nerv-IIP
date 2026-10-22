using System.Diagnostics.CodeAnalysis;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Inventory.Web.Endpoints.DeadLetters;

/// <summary>
/// 本服务死信读取与重放端点的路由组。6 个端点的实现只有一份，位于
/// <c>Nerv.IIP.Messaging.CAP.Endpoints</c> 共享模块；前缀取自共享清单
/// <see cref="IntegrationEventDeadLetterServices"/>，与 BusinessGateway 扇出用的是同一个字符串。
/// </summary>
public sealed class InventoryDeadLetterRoutes : IIntegrationEventDeadLetterRouteGroup
{
    public static string RoutePrefix => IntegrationEventDeadLetterServices.Inventory.RoutePrefix;
}

public sealed class ListInventoryDeadLettersEndpoint(IIntegrationEventDeadLetterStore deadLetterStore)
    : ListIntegrationEventDeadLettersEndpointBase<InventoryDeadLetterRoutes>(deadLetterStore);

public sealed class GetInventoryDeadLetterMetricsEndpoint(IIntegrationEventDeadLetterStore deadLetterStore)
    : GetIntegrationEventDeadLetterMetricsEndpointBase<InventoryDeadLetterRoutes>(deadLetterStore);

public sealed class GetInventoryDeadLetterEndpoint(IIntegrationEventDeadLetterStore deadLetterStore)
    : GetIntegrationEventDeadLetterEndpointBase<InventoryDeadLetterRoutes>(deadLetterStore);

public sealed class ReplayInventoryDeadLetterEndpoint : ReplayIntegrationEventDeadLetterEndpointBase<InventoryDeadLetterRoutes>;

public sealed class ReplayInventoryDeadLettersEndpoint : ReplayIntegrationEventDeadLettersEndpointBase<InventoryDeadLetterRoutes>;

public sealed class IgnoreInventoryDeadLetterEndpoint(
    IIntegrationEventDeadLetterStore deadLetterStore,
    TimeProvider timeProvider)
    : IgnoreIntegrationEventDeadLetterEndpointBase<InventoryDeadLetterRoutes>(deadLetterStore, timeProvider);

/// <summary>本服务死信端点的 facade-coverage 登记面（#3738；facade 由 #3739 交付）。</summary>
public static class InventoryDeadLetterEndpointContracts
{
    public static readonly IReadOnlyCollection<IntegrationEventDeadLetterEndpointContract> All =
        IntegrationEventDeadLetterEndpointContracts.For<InventoryDeadLetterRoutes>(
            "Inventory",
            typeof(ListInventoryDeadLettersEndpoint),
            typeof(GetInventoryDeadLetterMetricsEndpoint),
            typeof(GetInventoryDeadLetterEndpoint),
            typeof(ReplayInventoryDeadLetterEndpoint),
            typeof(ReplayInventoryDeadLettersEndpoint),
            typeof(IgnoreInventoryDeadLetterEndpoint));

    public static bool TryGet(Type endpointType, [NotNullWhen(true)] out IntegrationEventDeadLetterEndpointContract? contract) =>
        IntegrationEventDeadLetterEndpointContracts.TryGet(All, endpointType, out contract);
}
