using Nerv.IIP.Contracts.Iam;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Messaging.CAP;

/// <summary>
/// 一个服务的死信读取/重放路由前缀。每个注册了 <see cref="IIntegrationEventDeadLetterStore"/> 的服务
/// 实现一次，再由该服务的 6 个密封端点类把它填进共享端点基类。
/// </summary>
public interface IIntegrationEventDeadLetterRouteGroup
{
    /// <summary>服务既有的路由前缀，例如 <c>/api/business/v1/mes</c>；不带结尾斜杠。</summary>
    static abstract string RoutePrefix { get; }
}

/// <summary>死信读取面的路由后缀。端点基类与各服务的 endpoint 契约登记读同一份常量。</summary>
public static class IntegrationEventDeadLetterRoutes
{
    public const string Collection = "/dlq";
    public const string Metrics = "/dlq/metrics";
    public const string Item = "/dlq/{deadLetterId}";
    public const string Replay = "/dlq/{deadLetterId}/replay";
    public const string ReplayBatch = "/dlq/replay-batch";
    public const string Ignore = "/dlq/{deadLetterId}/ignore";
}

public sealed class ListIntegrationEventDeadLettersRequest
{
    public string? ConsumerName { get; set; }

    public string? EventType { get; set; }

    public string? Status { get; set; }

    public int? Skip { get; set; }

    public int? Take { get; set; }
}

public sealed class ReplayIntegrationEventDeadLetterBatchRequest
{
    public string? ConsumerName { get; set; }

    public string? EventType { get; set; }

    public string? Status { get; set; }

    public int? Take { get; set; }
}

public sealed class IgnoreIntegrationEventDeadLetterRequest
{
    public string Reason { get; set; } = string.Empty;
}

public sealed record IntegrationEventDeadLetterResponse(
    Guid Id,
    string ConsumerName,
    string? EventId,
    string? EventType,
    int? EventVersion,
    string? SourceService,
    string? IdempotencyKey,
    string FailureCode,
    string FailureMessage,
    string Status,
    DateTimeOffset DeadLetteredAtUtc,
    DateTimeOffset? ReplayedAtUtc);

public sealed record IntegrationEventDeadLetterDetailResponse(
    Guid Id,
    string ConsumerName,
    string? EventId,
    string? EventType,
    int? EventVersion,
    string? SourceService,
    string? IdempotencyKey,
    string EventClrType,
    string EventJson,
    string FailureCode,
    string FailureMessage,
    string Status,
    DateTimeOffset DeadLetteredAtUtc,
    DateTimeOffset? ReplayedAtUtc);

public sealed record IntegrationEventDeadLetterListResponse(
    IReadOnlyCollection<IntegrationEventDeadLetterResponse> Items);

public sealed record IntegrationEventDeadLetterReplayResponse(
    Guid Id,
    bool Succeeded,
    string Status,
    string? Message);

public sealed record IntegrationEventDeadLetterBatchReplayResponse(
    IReadOnlyCollection<IntegrationEventDeadLetterReplayResponse> Items);

public sealed record IntegrationEventDeadLetterEventTypeMetricsResponse(
    string EventType,
    int ActionableCount,
    int PendingCount,
    int FailedCount,
    int IgnoredCount,
    int ReplayedCount);

public sealed record IntegrationEventDeadLetterMetricsResponse(
    int ActionableCount,
    int PendingCount,
    int FailedCount,
    int IgnoredCount,
    int ReplayedCount,
    IReadOnlyCollection<IntegrationEventDeadLetterEventTypeMetricsResponse> EventTypes);

/// <summary>
/// 死信端点的 facade-coverage 登记行。形状与各服务自有的 <c>*EndpointContract</c> 一致
/// （HttpMethod / Route / OperationId 由 <c>Nerv.IIP.FacadeCoverage.Tests</c> 按属性名反射读取）。
/// </summary>
public sealed record IntegrationEventDeadLetterEndpointContract(
    Type EndpointType,
    string HttpMethod,
    string Route,
    string PermissionCode,
    string AuthorizationPolicy,
    string OperationId);

public static class IntegrationEventDeadLetterEndpointContracts
{
    /// <summary>
    /// 由路由前缀与**端点类型名**一次生成 6 条登记行。
    ///
    /// operationId 不接受调用方另给的词干：各服务的 <c>Endpoints.NameGenerator</c> 在自己的 registry 里
    /// 查不到该端点时一律回落到「类型名去掉 Endpoint 后缀 + 首字母小写」，而 DLQ 契约另立在
    /// <c>*DeadLetterEndpointContracts</c>、不在那些 registry 里，所以真实 OpenAPI operationId 只由类型名决定。
    /// 允许手写词干就等于允许登记表与真实契约各写各的（#3738 审核质量轴阻断：Mes/Quality/IIoT 共 18 行曾这样漂）。
    /// 这条派生规则与真实文档的一致性由 IIoT 的 OpenAPI 用例实跑钉住，不靠「照抄了 canonical」。
    /// </summary>
    public static IReadOnlyCollection<IntegrationEventDeadLetterEndpointContract> For<TRoutes>(
        Type listEndpoint,
        Type metricsEndpoint,
        Type detailEndpoint,
        Type replayEndpoint,
        Type replayBatchEndpoint,
        Type ignoreEndpoint)
        where TRoutes : IIntegrationEventDeadLetterRouteGroup =>
    [
        new(listEndpoint, "GET", TRoutes.RoutePrefix + IntegrationEventDeadLetterRoutes.Collection,
            NervIipPermissionCodes.BusinessDlqRead, InternalServiceAuthorizationPolicy.Name,
            OperationIdOf(listEndpoint)),
        new(metricsEndpoint, "GET", TRoutes.RoutePrefix + IntegrationEventDeadLetterRoutes.Metrics,
            NervIipPermissionCodes.BusinessDlqRead, InternalServiceAuthorizationPolicy.Name,
            OperationIdOf(metricsEndpoint)),
        new(detailEndpoint, "GET", TRoutes.RoutePrefix + IntegrationEventDeadLetterRoutes.Item,
            NervIipPermissionCodes.BusinessDlqRead, InternalServiceAuthorizationPolicy.Name,
            OperationIdOf(detailEndpoint)),
        new(replayEndpoint, "POST", TRoutes.RoutePrefix + IntegrationEventDeadLetterRoutes.Replay,
            NervIipPermissionCodes.BusinessDlqManage, InternalServiceAuthorizationPolicy.Name,
            OperationIdOf(replayEndpoint)),
        new(replayBatchEndpoint, "POST", TRoutes.RoutePrefix + IntegrationEventDeadLetterRoutes.ReplayBatch,
            NervIipPermissionCodes.BusinessDlqManage, InternalServiceAuthorizationPolicy.Name,
            OperationIdOf(replayBatchEndpoint)),
        new(ignoreEndpoint, "POST", TRoutes.RoutePrefix + IntegrationEventDeadLetterRoutes.Ignore,
            NervIipPermissionCodes.BusinessDlqManage, InternalServiceAuthorizationPolicy.Name,
            OperationIdOf(ignoreEndpoint)),
    ];

    /// <summary>各服务 <c>NameGenerator</c> 未命中 registry 时的回落规则，见 <see cref="For{TRoutes}"/> 的说明。</summary>
    public static string OperationIdOf(Type endpointType)
    {
        ArgumentNullException.ThrowIfNull(endpointType);
        var name = endpointType.Name.EndsWith("Endpoint", StringComparison.Ordinal)
            ? endpointType.Name[..^"Endpoint".Length]
            : endpointType.Name;
        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}
