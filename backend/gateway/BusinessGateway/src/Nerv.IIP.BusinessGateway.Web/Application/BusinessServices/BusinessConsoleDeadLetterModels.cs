using System.Text.Json.Serialization;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

/// <summary>
/// 一条死信及其所属服务。行本身直接复用服务侧契约 <see cref="IntegrationEventDeadLetterResponse"/>，
/// 网关不另抄一份同形 DTO——抄一份就要靠人工保持两边字段一致。
/// </summary>
public sealed record BusinessConsoleDeadLetterItem(string Service, IntegrationEventDeadLetterResponse DeadLetter);

/// <summary>本次扇出里一个来源服务的结果状态。</summary>
[JsonConverter(typeof(CamelCaseJsonStringEnumConverter<BusinessConsoleDeadLetterSourceState>))]
public enum BusinessConsoleDeadLetterSourceState
{
    /// <summary>该来源答上来了，它的行在 <c>items</c> 里。</summary>
    Available,

    /// <summary>该来源没答上来。它的行本次拿不到，但这**不等于**它没有死信。</summary>
    Unavailable,
}

/// <summary>
/// 一个来源没答上来的原因。与 <see cref="BusinessConsoleDeadLetterSourceState"/> 是**两个维度**，
/// 因此是两个类型：把它们写反会编译不过，而不是产出一个取值合法、含义错误的响应。
/// </summary>
[JsonConverter(typeof(CamelCaseJsonStringEnumConverter<BusinessConsoleDeadLetterSourceFailureReason>))]
public enum BusinessConsoleDeadLetterSourceFailureReason
{
    /// <summary>在逐源等待上限内没有回答。</summary>
    SourceTimeout,

    /// <summary>连接失败、下游报错或熔断打开。</summary>
    SourceUnavailable,
}

/// <summary>
/// 一个来源服务在本次扇出里的结果。单个服务不可用时它的行拿不到，但其余服务的结果照常返回，
/// 该服务在这里显式标成不可用——不吞成空列表（会被读成「没有死信」），也不让整页 500。
/// </summary>
/// <remarks>
/// 取值集合稳定且有限，因此按 <c>contracts-and-codegen.md:44</c> 以受控枚举进 OpenAPI，
/// 不用自由文本：这一层是 #3740 页面「该服务是真没有死信，还是这次没读到」的唯一判据，
/// 生成客户端拿到闭集才谈得上穷举分支。
/// </remarks>
public sealed record BusinessConsoleDeadLetterSourceStatus(
    string Service,
    BusinessConsoleDeadLetterSourceState Status,
    BusinessConsoleDeadLetterSourceFailureReason? Reason);

public sealed record BusinessConsoleDeadLetterListResponse(
    IReadOnlyCollection<BusinessConsoleDeadLetterItem> Items,
    IReadOnlyCollection<BusinessConsoleDeadLetterSourceStatus> SourceStatuses);

public sealed record BusinessConsoleDeadLetterServiceMetrics(
    string Service,
    IntegrationEventDeadLetterMetricsResponse Metrics);

public sealed record BusinessConsoleDeadLetterMetricsResponse(
    int ActionableCount,
    int PendingCount,
    int FailedCount,
    int IgnoredCount,
    int ReplayedCount,
    IReadOnlyCollection<BusinessConsoleDeadLetterServiceMetrics> Services,
    IReadOnlyCollection<BusinessConsoleDeadLetterSourceStatus> SourceStatuses);
