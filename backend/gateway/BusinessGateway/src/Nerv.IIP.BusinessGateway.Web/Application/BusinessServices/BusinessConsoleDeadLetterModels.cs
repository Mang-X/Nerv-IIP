using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

/// <summary>
/// 一条死信及其所属服务。行本身直接复用服务侧契约 <see cref="IntegrationEventDeadLetterResponse"/>，
/// 网关不另抄一份同形 DTO——抄一份就要靠人工保持两边字段一致。
/// </summary>
public sealed record BusinessConsoleDeadLetterItem(string Service, IntegrationEventDeadLetterResponse DeadLetter);

/// <summary>
/// 一个来源服务在本次扇出里的结果。单个服务不可用时它的行拿不到，但其余服务的结果照常返回，
/// 该服务在这里显式标成不可用——不吞成空列表（会被读成「没有死信」），也不让整页 500。
/// </summary>
public sealed record BusinessConsoleDeadLetterSourceStatus(string Service, string Status, string? Reason);

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

public static class BusinessConsoleDeadLetterSourceStatuses
{
    public const string Available = "available";
    public const string Unavailable = "unavailable";
    public const string SourceTimeout = "source-timeout";
    public const string SourceUnavailable = "source-unavailable";
}
