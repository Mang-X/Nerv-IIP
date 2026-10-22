using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Mes.Web.Application.Quality;

/// <summary>
/// 首件门禁（#2780）：某工单某工序的首件判定合格之前，不允许继续批量报工。
/// 「这一次是不是首件那一件」由 Quality 的首件进度直接回答，不由 MES 本地报工历史推断。
/// </summary>
public interface IMesFirstArticleGate
{
    Task EnsureBatchReportAllowedAsync(
        string organizationId,
        string environmentId,
        string workOrderId,
        string operationTaskId,
        CancellationToken cancellationToken);
}

/// <summary>
/// 取数形态是**同步读**：波 1（#2779）只交付了 <c>GET /api/business/v1/quality/first-article-confirmation</c>
/// 这一个服务间读契约，没有发布任何集成事件。要做事件投影得由 MES 自建一张本地表，
/// 而门禁是写路径上的强一致判据——投影延迟窗口里放行的每一次报工都是漏检，
/// 用可用性耦合换正确性在这里是划算的（来源不可用时拒绝，不是放行）。
/// </summary>
public sealed class HttpMesFirstArticleGate(
    MesQualityHttpClient qualityClient,
    IInternalServiceTokenProvider internalTokenProvider)
    : IMesFirstArticleGate
{
    private const string SourceUnavailable =
        "FIRST_ARTICLE_SOURCE_UNAVAILABLE: Quality 首件确认来源暂不可用。";

    public async Task EnsureBatchReportAllowedAsync(
        string organizationId,
        string environmentId,
        string workOrderId,
        string operationTaskId,
        CancellationToken cancellationToken)
    {
        // Quality 的工单发布事实按 OperationId 落库，MES 的报工按 OperationTaskId 发出，两者是同一个工序身份。
        var requestUri = "/api/business/v1/quality/first-article-confirmation?" + string.Join(
            '&',
            Pair("organizationId", organizationId),
            Pair("environmentId", environmentId),
            Pair("workOrderId", workOrderId),
            Pair("operationId", operationTaskId));

        FirstArticleConfirmationResponse confirmation;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            if (!string.IsNullOrWhiteSpace(internalTokenProvider.BearerToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue(
                    "Bearer",
                    internalTokenProvider.BearerToken);
            }

            using var httpResponse = await qualityClient.HttpClient.SendAsync(request, cancellationToken);
            if (!httpResponse.IsSuccessStatusCode)
            {
                throw new KnownException(SourceUnavailable);
            }

            var envelope = await httpResponse.Content
                .ReadFromJsonAsync<ResponseDataEnvelope<FirstArticleConfirmationResponse>>(cancellationToken);
            confirmation = envelope is { Success: true, Data: not null }
                ? envelope.Data
                : throw new KnownException(SourceUnavailable);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (KnownException)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            throw new KnownException(SourceUnavailable, exception);
        }
        catch (TaskCanceledException exception)
        {
            throw new KnownException(SourceUnavailable, exception);
        }
        catch (JsonException exception)
        {
            throw new KnownException(SourceUnavailable, exception);
        }

        EnsureAllows(confirmation);
    }

    /// <summary>
    /// 放行判据（#2780 拍板决策 1 与决策 2）：
    /// <list type="bullet">
    /// <item><c>not-required</c>：本工序无需首件。</item>
    /// <item><c>not-opened</c>：首件任务尚未开出，而**开单的唯一触发点就是本次报工的事件**——
    /// 这一次就是决策 2 说的「首件那一件」，放行它；它落库后下一次报工会读到 <c>pending</c> 被拒。</item>
    /// <item><c>decided</c> + <c>passed</c>：首件已判合格。</item>
    /// </list>
    /// 其余一律拒。让步放行是对**已产出那批件**的处置结论，不回答「后面能不能继续批量做」，与不合格同样拒；
    /// <c>not-synchronized</c> 是 Quality 还不掌握该工序事实，它靠工单发布事实到达恢复、不靠报工恢复，
    /// 拒掉它不会锁死任何东西（拍板校正第 3 条要的 fail closed 落在这一支上）。
    /// </summary>
    private static void EnsureAllows(FirstArticleConfirmationResponse confirmation)
    {
        switch (confirmation.Status)
        {
            case QualityFirstArticleConfirmationStatuses.NotRequired:
            case QualityFirstArticleConfirmationStatuses.NotOpened:
                return;
            case QualityFirstArticleConfirmationStatuses.Decided:
                EnsureDecisionAllows(confirmation.Result);
                return;
            case QualityFirstArticleConfirmationStatuses.Pending:
                throw new KnownException("本工序首件尚未判定，暂不能继续报工。可在工序行操作打开首件检验记录。");
            case QualityFirstArticleConfirmationStatuses.NotSynchronized:
                throw new KnownException("本工序的工单发布事实尚未同步到质量，暂不能报工。请稍后重试。");
            default:
                throw new KnownException(SourceUnavailable);
        }
    }

    // 拒绝文案点名入口位置：前端把服务端消息原样上屏（超过 60 字会被截断，下面各条都在阈值内），
    // 而报工抽屉里拿不到跳转，操作员需要知道去哪儿看结论。
    private static void EnsureDecisionAllows(string? result)
    {
        switch (result)
        {
            case QualityInspectionDispositionStatuses.Passed:
                return;
            case QualityInspectionDispositionStatuses.Rejected:
                throw new KnownException("本工序首件判定不合格，请返工后重新首件检验；记录见工序行操作。");
            case QualityInspectionDispositionStatuses.ConditionalRelease:
                throw new KnownException("本工序首件为让步放行，不解锁批量报工。请纠正后重新首件检验。");
            default:
                throw new KnownException(SourceUnavailable);
        }
    }

    private static string Pair(string name, string value) =>
        $"{name}={Uri.EscapeDataString(value.Trim())}";

    /// <summary>
    /// 只声明门禁判据用得到的三个字段。契约另有 <c>attemptNumber</c> 与两个检验单 id，
    /// 那是给人看的追溯线索，门禁不据此判定，不在这里重述一遍。
    /// </summary>
    private sealed record FirstArticleConfirmationResponse(string? Status, string? Result);

    private sealed record ResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);
}
