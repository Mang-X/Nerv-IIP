using System.Diagnostics.CodeAnalysis;
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
    /// 由路由前缀与 operationId 词干一次生成 6 条登记行。
    ///
    /// 这份登记表是 operationId 的**唯一产出方**：各服务的 <c>Endpoints.NameGenerator</c> 通过
    /// <see cref="TryGet"/> 直接取这里的值（与它取本服务 <c>*EndpointContracts</c> 的写法同形），
    /// 所以登记值与真实 OpenAPI 是同一个字符串，而不是两条互相照抄的派生规则。
    /// 一致性由 Quality 的 <c>QualityOpenApiTests</c> 对真实 swagger 文档实跑钉住。
    /// </summary>
    public static IReadOnlyCollection<IntegrationEventDeadLetterEndpointContract> For<TRoutes>(
        string operationIdInfix,
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
            $"list{operationIdInfix}DeadLetters"),
        new(metricsEndpoint, "GET", TRoutes.RoutePrefix + IntegrationEventDeadLetterRoutes.Metrics,
            NervIipPermissionCodes.BusinessDlqRead, InternalServiceAuthorizationPolicy.Name,
            $"get{operationIdInfix}DeadLetterMetrics"),
        new(detailEndpoint, "GET", TRoutes.RoutePrefix + IntegrationEventDeadLetterRoutes.Item,
            NervIipPermissionCodes.BusinessDlqRead, InternalServiceAuthorizationPolicy.Name,
            $"get{operationIdInfix}DeadLetter"),
        new(replayEndpoint, "POST", TRoutes.RoutePrefix + IntegrationEventDeadLetterRoutes.Replay,
            NervIipPermissionCodes.BusinessDlqManage, InternalServiceAuthorizationPolicy.Name,
            $"replay{operationIdInfix}DeadLetter"),
        new(replayBatchEndpoint, "POST", TRoutes.RoutePrefix + IntegrationEventDeadLetterRoutes.ReplayBatch,
            NervIipPermissionCodes.BusinessDlqManage, InternalServiceAuthorizationPolicy.Name,
            $"replay{operationIdInfix}DeadLetters"),
        new(ignoreEndpoint, "POST", TRoutes.RoutePrefix + IntegrationEventDeadLetterRoutes.Ignore,
            NervIipPermissionCodes.BusinessDlqManage, InternalServiceAuthorizationPolicy.Name,
            $"ignore{operationIdInfix}DeadLetter"),
    ];

    /// <summary>
    /// 供各服务 <c>NameGenerator</c> 链上本登记表，用法与各服务自有的
    /// <c>*EndpointContracts.TryGet</c> 完全一致。
    /// </summary>
    public static bool TryGet(
        IReadOnlyCollection<IntegrationEventDeadLetterEndpointContract> contracts,
        Type endpointType,
        [NotNullWhen(true)] out IntegrationEventDeadLetterEndpointContract? contract)
    {
        ArgumentNullException.ThrowIfNull(contracts);
        contract = contracts.SingleOrDefault(candidate => candidate.EndpointType == endpointType);
        return contract is not null;
    }
}
