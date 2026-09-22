using System.Net;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

/// <summary>
/// 死信运维面的跨服务扇出。
/// </summary>
/// <remarks>
/// <para><b>权限只在一处把关</b>：10 个来源共用同一对权限码（<c>business.dlq.read</c> /
/// <c>business.dlq.manage</c>），端点基类 <c>AuthorizedBusinessProxyEndpoint</c> 已就该码对当前主体
/// 做过一次实时检查。再按来源各查一次是同一个调用重复 10 遍，不会多回答任何一个问题。
/// 这与「聚合 API 必须对每个来源分别做权限检查」不冲突：那条规则针对的是**各来源权限码不同**
/// 的聚合面（例如全局搜索），无权来源要单独标出而不是泄漏内容。</para>
///
/// <para><b>take 是逐来源窗口</b>：扇出没有跨服务的全局游标，也不返回 <c>total</c>，
/// 因此不存在「对已分页结果再过滤却表现为全量搜索」的形态。筛选条件全部下推到各服务查询里执行。</para>
/// </remarks>
public sealed class BusinessConsoleDeadLetterService(
    IBusinessDeadLetterClient deadLetters,
    BusinessDeadLetterSources sources,
    IInternalServiceTokenProvider tokenProvider)
{
    /// <summary>单个来源的等待上限，与既有跨来源扇出 <c>BusinessConsoleSearchService</c> 取同一个值。</summary>
    private static readonly TimeSpan SourceTimeout = TimeSpan.FromMilliseconds(1500);

    public async Task<BusinessConsoleDeadLetterListResponse> ListAsync(
        string? service,
        ListIntegrationEventDeadLettersRequest request,
        CancellationToken cancellationToken)
    {
        var outcomes = await FanOutAsync(
            service,
            (source, token) => deadLetters.ListAsync(tokenProvider.BearerToken, source, request, token),
            cancellationToken);

        var items = outcomes
            .Where(outcome => outcome.Value is not null)
            .SelectMany(outcome => outcome.Value!.Items.Select(item =>
                new BusinessConsoleDeadLetterItem(outcome.Source.Name, item)))
            .OrderBy(item => item.DeadLetter.DeadLetteredAtUtc)
            .ThenBy(item => item.Service, StringComparer.Ordinal)
            .ThenBy(item => item.DeadLetter.Id)
            .ToArray();

        return new BusinessConsoleDeadLetterListResponse(items, ToSourceStatuses(outcomes));
    }

    public async Task<BusinessConsoleDeadLetterMetricsResponse> GetMetricsAsync(
        string? service,
        CancellationToken cancellationToken)
    {
        var outcomes = await FanOutAsync(
            service,
            (source, token) => deadLetters.GetMetricsAsync(tokenProvider.BearerToken, source, token),
            cancellationToken);

        var reached = outcomes
            .Where(outcome => outcome.Value is not null)
            .Select(outcome => new BusinessConsoleDeadLetterServiceMetrics(outcome.Source.Name, outcome.Value!))
            .ToArray();

        return new BusinessConsoleDeadLetterMetricsResponse(
            reached.Sum(x => x.Metrics.ActionableCount),
            reached.Sum(x => x.Metrics.PendingCount),
            reached.Sum(x => x.Metrics.FailedCount),
            reached.Sum(x => x.Metrics.IgnoredCount),
            reached.Sum(x => x.Metrics.ReplayedCount),
            reached,
            ToSourceStatuses(outcomes));
    }

    public BusinessDeadLetterSource RequireSource(string? service) =>
        sources.TryGet(service, out var source)
            ? source
            : throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadRequest,
                "unknown-dead-letter-service");

    private async Task<IReadOnlyList<SourceOutcome<TValue>>> FanOutAsync<TValue>(
        string? service,
        Func<BusinessDeadLetterSource, CancellationToken, Task<TValue>> read,
        CancellationToken cancellationToken)
        where TValue : class
    {
        var selected = string.IsNullOrWhiteSpace(service)
            ? sources.All
            : [RequireSource(service)];
        return await Task.WhenAll(selected.Select(source => ReadSourceAsync(source, read, cancellationToken)));
    }

    private static async Task<SourceOutcome<TValue>> ReadSourceAsync<TValue>(
        BusinessDeadLetterSource source,
        Func<BusinessDeadLetterSource, CancellationToken, Task<TValue>> read,
        CancellationToken cancellationToken)
        where TValue : class
    {
        using var sourceCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        sourceCts.CancelAfter(SourceTimeout);
        try
        {
            return new(source, await read(source, sourceCts.Token).WaitAsync(SourceTimeout, cancellationToken), null);
        }
        catch (Exception ex) when (IsDegradableSourceFailure(ex, cancellationToken))
        {
            return new(source, null, FailureReason(ex, cancellationToken));
        }
    }

    private static IReadOnlyCollection<BusinessConsoleDeadLetterSourceStatus> ToSourceStatuses<TValue>(
        IEnumerable<SourceOutcome<TValue>> outcomes)
        where TValue : class =>
        outcomes
            .OrderBy(outcome => outcome.Source.Name, StringComparer.Ordinal)
            .Select(outcome => new BusinessConsoleDeadLetterSourceStatus(
                outcome.Source.Name,
                outcome.Value is null
                    ? BusinessConsoleDeadLetterSourceStatuses.Unavailable
                    : BusinessConsoleDeadLetterSourceStatuses.Available,
                outcome.Reason))
            .ToArray();

    private static bool IsDegradableSourceFailure(Exception ex, CancellationToken requestCancellationToken) =>
        ex is BusinessServiceProxyException or HttpRequestException or TimeoutException
        || (ex is TaskCanceledException && !requestCancellationToken.IsCancellationRequested);

    private static string FailureReason(Exception ex, CancellationToken requestCancellationToken) =>
        ex is TimeoutException || (ex is TaskCanceledException && !requestCancellationToken.IsCancellationRequested)
            ? BusinessConsoleDeadLetterSourceStatuses.SourceTimeout
            : BusinessConsoleDeadLetterSourceStatuses.SourceUnavailable;

    private sealed record SourceOutcome<TValue>(BusinessDeadLetterSource Source, TValue? Value, string? Reason)
        where TValue : class;
}
