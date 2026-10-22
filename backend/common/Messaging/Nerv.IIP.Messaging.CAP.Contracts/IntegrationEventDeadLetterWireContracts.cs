using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nerv.IIP.Messaging.CAP;

/// <summary>
/// 死信行的状态。既是持久化取值（EF <c>HasConversion&lt;string&gt;</c> 存成员名），也是 HTTP 出口
/// 与 Gateway facade 的公开取值——**同一个类型**，因此服务、网关和生成客户端不可能各拿一份会漂移的集合。
///
/// ⚠️ 由此带来的约束：**改成员名就是改列值**。重命名任一成员都要配一次数据迁移，
/// 不能只当作一次改名。
/// </summary>
[JsonConverter(typeof(CamelCaseJsonStringEnumConverter<IntegrationEventDeadLetterStatus>))]
public enum IntegrationEventDeadLetterStatus
{
    Pending = 0,
    Replayed = 1,
    Failed = 2,
    Ignored = 3
}

/// <summary>
/// 一次重放的结果。取值集合与 <see cref="IntegrationEventDeadLetterStatus"/> **不同**，不是它的副本：
/// 重放可能因为本服务没有可用 handler 而一次都没尝试（<see cref="NoHandler"/>，原行保持原状），
/// 也可能目标行根本不存在（<see cref="NotFound"/>，HTTP 出口据此判 404）。
/// </summary>
[JsonConverter(typeof(CamelCaseJsonStringEnumConverter<IntegrationEventDeadLetterReplayStatus>))]
public enum IntegrationEventDeadLetterReplayStatus
{
    /// <summary>handler 执行成功，行已置 <see cref="IntegrationEventDeadLetterStatus.Replayed"/>。</summary>
    Replayed = 0,

    /// <summary>handler 抛出异常，行已置 <see cref="IntegrationEventDeadLetterStatus.Failed"/> 并写入原因。</summary>
    Failed = 1,

    /// <summary>本服务没有能处理该事件的重放 handler，因此一次都没有尝试过；原行保持原状。</summary>
    NoHandler = 2,

    /// <summary>目标死信行不存在。</summary>
    NotFound = 3
}

/// <summary>
/// 把枚举固定成 camelCase 字符串，与各 Host 自己的 <c>JsonOptions</c> 无关。
/// 死信取值要跨「服务 HTTP 出口 → Gateway facade → 生成客户端」三跳，各服务并未统一注册
/// <c>JsonStringEnumConverter</c>；不钉住的话同一个值在服务侧是数字、在网关侧是字符串。
/// </summary>
public sealed class CamelCaseJsonStringEnumConverter<TEnum> : JsonStringEnumConverter<TEnum>
    where TEnum : struct, Enum
{
    public CamelCaseJsonStringEnumConverter()
        : base(JsonNamingPolicy.CamelCase)
    {
    }
}

/// <summary>死信读取面的路由后缀。端点基类、facade 登记行与 Gateway 客户端读同一份常量。</summary>
public static class IntegrationEventDeadLetterRoutes
{
    public const string Collection = "/dlq";
    public const string Metrics = "/dlq/metrics";
    public const string Item = "/dlq/{deadLetterId}";
    public const string Replay = "/dlq/{deadLetterId}/replay";
    public const string ReplayBatch = "/dlq/replay-batch";
    public const string Ignore = "/dlq/{deadLetterId}/ignore";

    /// <summary>把带参路由模板填成具体路径。客户端由模板派生，不另写一份同形字面量。</summary>
    public static string ItemFor(Guid deadLetterId) => Fill(Item, deadLetterId);

    /// <inheritdoc cref="ItemFor"/>
    public static string ReplayFor(Guid deadLetterId) => Fill(Replay, deadLetterId);

    /// <inheritdoc cref="ItemFor"/>
    public static string IgnoreFor(Guid deadLetterId) => Fill(Ignore, deadLetterId);

    private static string Fill(string template, Guid deadLetterId) =>
        template.Replace("{deadLetterId}", deadLetterId.ToString("D"), StringComparison.Ordinal);
}

/// <summary>
/// 一个暴露了共享死信读取面的服务。<see cref="Name"/> 就是 <c>backend/services</c> 下该服务的目录名，
/// 不做任何大小写/连字符变换——完备性断言按目录名反向枚举，多一层变换就多一处会算错的规则。
/// </summary>
public sealed record IntegrationEventDeadLetterService(string Name, string RoutePrefix);

/// <summary>
/// 死信读取面的服务清单：**路由前缀的唯一产出方**。
///
/// 各服务的 <c>*DeadLetterRoutes.RoutePrefix</c> 与 BusinessGateway 的扇出客户端都从这里取值，
/// 因此「服务上线的路由」和「网关去请求的路由」是同一个字符串，而不是两份互相照抄的字面量。
/// 清单的完备性不在这里维护——由 <c>Nerv.IIP.Messaging.CAP.Tests</c> 对 <c>backend/services</c>
/// 反向枚举钉住：注册了死信 store 的服务没进这份清单就红。
/// </summary>
public static class IntegrationEventDeadLetterServices
{
    public static IntegrationEventDeadLetterService AppHub { get; } = new("AppHub", "/internal/apphub/v1");

    public static IntegrationEventDeadLetterService DemandPlanning { get; } = new("DemandPlanning", "/api/business/v1/planning");

    public static IntegrationEventDeadLetterService Erp { get; } = new("Erp", "/api/business/v1/erp");

    public static IntegrationEventDeadLetterService IndustrialTelemetry { get; } = new("IndustrialTelemetry", "/api/business/v1/iiot");

    public static IntegrationEventDeadLetterService Inventory { get; } = new("Inventory", "/api/inventory/v1");

    public static IntegrationEventDeadLetterService Maintenance { get; } = new("Maintenance", "/api/business/v1/maintenance");

    public static IntegrationEventDeadLetterService Mes { get; } = new("Mes", "/api/business/v1/mes");

    public static IntegrationEventDeadLetterService Quality { get; } = new("Quality", "/api/business/v1/quality");

    public static IntegrationEventDeadLetterService Scheduling { get; } = new("Scheduling", "/api/business/v1/scheduling");

    public static IntegrationEventDeadLetterService Wms { get; } = new("Wms", "/api/business/v1/wms");

    public static IReadOnlyList<IntegrationEventDeadLetterService> All { get; } =
    [
        AppHub,
        DemandPlanning,
        Erp,
        IndustrialTelemetry,
        Inventory,
        Maintenance,
        Mes,
        Quality,
        Scheduling,
        Wms,
    ];
}

public sealed class ListIntegrationEventDeadLettersRequest
{
    public string? ConsumerName { get; set; }

    public string? EventType { get; set; }

    public string? FailureCode { get; set; }

    public IntegrationEventDeadLetterStatus? Status { get; set; }

    public DateTimeOffset? DeadLetteredFromUtc { get; set; }

    public DateTimeOffset? DeadLetteredToUtc { get; set; }

    public int? Skip { get; set; }

    public int? Take { get; set; }
}

public sealed class ReplayIntegrationEventDeadLetterBatchRequest
{
    public string? ConsumerName { get; set; }

    public string? EventType { get; set; }

    public string? FailureCode { get; set; }

    public IntegrationEventDeadLetterStatus? Status { get; set; }

    public DateTimeOffset? DeadLetteredFromUtc { get; set; }

    public DateTimeOffset? DeadLetteredToUtc { get; set; }

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
    IntegrationEventDeadLetterStatus Status,
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
    IntegrationEventDeadLetterStatus Status,
    DateTimeOffset DeadLetteredAtUtc,
    DateTimeOffset? ReplayedAtUtc);

public sealed record IntegrationEventDeadLetterListResponse(
    IReadOnlyCollection<IntegrationEventDeadLetterResponse> Items);

public sealed record IntegrationEventDeadLetterReplayResponse(
    Guid Id,
    bool Succeeded,
    IntegrationEventDeadLetterReplayStatus Status,
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
