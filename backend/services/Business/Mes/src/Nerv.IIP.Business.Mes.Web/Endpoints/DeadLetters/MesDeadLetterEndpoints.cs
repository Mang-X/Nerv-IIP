using System.Diagnostics.CodeAnalysis;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Mes.Web.Endpoints.DeadLetters;

/// <summary>
/// 本服务死信读取与重放端点的路由组。6 个端点的实现只有一份，位于
/// <c>Nerv.IIP.Messaging.CAP.Endpoints</c> 共享模块；前缀取自共享清单
/// <see cref="IntegrationEventDeadLetterServices"/>，与 BusinessGateway 扇出用的是同一个字符串。
/// </summary>
public sealed class MesDeadLetterRoutes : IIntegrationEventDeadLetterRouteGroup
{
    public static string RoutePrefix => IntegrationEventDeadLetterServices.Mes.RoutePrefix;
}

public sealed class ListMesDeadLettersEndpoint(IIntegrationEventDeadLetterStore deadLetterStore)
    : ListIntegrationEventDeadLettersEndpointBase<MesDeadLetterRoutes>(deadLetterStore);

public sealed class GetMesDeadLetterMetricsEndpoint(IIntegrationEventDeadLetterStore deadLetterStore)
    : GetIntegrationEventDeadLetterMetricsEndpointBase<MesDeadLetterRoutes>(deadLetterStore);

public sealed class GetMesDeadLetterEndpoint(IIntegrationEventDeadLetterStore deadLetterStore)
    : GetIntegrationEventDeadLetterEndpointBase<MesDeadLetterRoutes>(deadLetterStore);

public sealed class ReplayMesDeadLetterEndpoint : ReplayIntegrationEventDeadLetterEndpointBase<MesDeadLetterRoutes>;

public sealed class ReplayMesDeadLettersEndpoint : ReplayIntegrationEventDeadLettersEndpointBase<MesDeadLetterRoutes>;

public sealed class IgnoreMesDeadLetterEndpoint(
    IIntegrationEventDeadLetterStore deadLetterStore,
    TimeProvider timeProvider)
    : IgnoreIntegrationEventDeadLetterEndpointBase<MesDeadLetterRoutes>(deadLetterStore, timeProvider);

/// <summary>本服务死信端点的 facade-coverage 登记面（#3738；facade 由 #3739 交付）。</summary>
public static class MesDeadLetterEndpointContracts
{
    public static readonly IReadOnlyCollection<IntegrationEventDeadLetterEndpointContract> All =
        IntegrationEventDeadLetterEndpointContracts.For<MesDeadLetterRoutes>(
            "BusinessMes",
            typeof(ListMesDeadLettersEndpoint),
            typeof(GetMesDeadLetterMetricsEndpoint),
            typeof(GetMesDeadLetterEndpoint),
            typeof(ReplayMesDeadLetterEndpoint),
            typeof(ReplayMesDeadLettersEndpoint),
            typeof(IgnoreMesDeadLetterEndpoint));

    public static bool TryGet(Type endpointType, [NotNullWhen(true)] out IntegrationEventDeadLetterEndpointContract? contract) =>
        IntegrationEventDeadLetterEndpointContracts.TryGet(All, endpointType, out contract);
}
