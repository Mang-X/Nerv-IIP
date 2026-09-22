using System.Diagnostics.CodeAnalysis;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Endpoints.DeadLetters;

/// <summary>
/// 本服务死信读取与重放端点的路由组。6 个端点的实现只有一份，位于
/// <c>Nerv.IIP.Messaging.CAP.Endpoints</c> 共享模块；前缀取自共享清单
/// <see cref="IntegrationEventDeadLetterServices"/>，与 BusinessGateway 扇出用的是同一个字符串。
/// </summary>
public sealed class IiotDeadLetterRoutes : IIntegrationEventDeadLetterRouteGroup
{
    public static string RoutePrefix => IntegrationEventDeadLetterServices.IndustrialTelemetry.RoutePrefix;
}

public sealed class ListIiotDeadLettersEndpoint(IIntegrationEventDeadLetterStore deadLetterStore)
    : ListIntegrationEventDeadLettersEndpointBase<IiotDeadLetterRoutes>(deadLetterStore);

public sealed class GetIiotDeadLetterMetricsEndpoint(IIntegrationEventDeadLetterStore deadLetterStore)
    : GetIntegrationEventDeadLetterMetricsEndpointBase<IiotDeadLetterRoutes>(deadLetterStore);

public sealed class GetIiotDeadLetterEndpoint(IIntegrationEventDeadLetterStore deadLetterStore)
    : GetIntegrationEventDeadLetterEndpointBase<IiotDeadLetterRoutes>(deadLetterStore);

public sealed class ReplayIiotDeadLetterEndpoint : ReplayIntegrationEventDeadLetterEndpointBase<IiotDeadLetterRoutes>;

public sealed class ReplayIiotDeadLettersEndpoint : ReplayIntegrationEventDeadLettersEndpointBase<IiotDeadLetterRoutes>;

public sealed class IgnoreIiotDeadLetterEndpoint(
    IIntegrationEventDeadLetterStore deadLetterStore,
    TimeProvider timeProvider)
    : IgnoreIntegrationEventDeadLetterEndpointBase<IiotDeadLetterRoutes>(deadLetterStore, timeProvider);

/// <summary>本服务死信端点的 facade-coverage 登记面（#3738；facade 由 #3739 交付）。</summary>
public static class IiotDeadLetterEndpointContracts
{
    public static readonly IReadOnlyCollection<IntegrationEventDeadLetterEndpointContract> All =
        IntegrationEventDeadLetterEndpointContracts.For<IiotDeadLetterRoutes>(
            "BusinessIiot",
            typeof(ListIiotDeadLettersEndpoint),
            typeof(GetIiotDeadLetterMetricsEndpoint),
            typeof(GetIiotDeadLetterEndpoint),
            typeof(ReplayIiotDeadLetterEndpoint),
            typeof(ReplayIiotDeadLettersEndpoint),
            typeof(IgnoreIiotDeadLetterEndpoint));

    public static bool TryGet(Type endpointType, [NotNullWhen(true)] out IntegrationEventDeadLetterEndpointContract? contract) =>
        IntegrationEventDeadLetterEndpointContracts.TryGet(All, endpointType, out contract);
}
