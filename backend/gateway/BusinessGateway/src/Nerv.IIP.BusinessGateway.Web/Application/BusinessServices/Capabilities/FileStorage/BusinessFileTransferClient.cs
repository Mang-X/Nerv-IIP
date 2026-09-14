using System.Net;
using System.Net.Http.Headers;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

/// <summary>
/// FileStorage 的**字节面**。与 <see cref="IBusinessFileStorageClient"/> 的区别不是领域，而是
/// 弹性契约：ADR 0015 的策略表按幂等性二分、只覆盖 JSON RPC 形状，其 10 秒总超时会在弱网下
/// 必然切断 <c>shift-handover-photo</c> 允许的 20,971,520 bytes 级 tus <c>PATCH</c>
/// （`FileStorage.Web/appsettings.json` 的 `MaximumFileSizeBytes`），并把共享熔断器连带打开。
/// 拆分依据是 <b>ADR 0030 决策 5</b>——它对 ADR 0015 决策 2 的「单次调用超时为 10 秒」作了
/// 部分修订，使该参数不适用于经网关代理的字节流传输跳；接缝划在「每一跳实际发起什么」上，
/// 依据是 ADR 0015 决策 1 末行的原话「策略选择以客户端实际可发起的操作为准，不以客户端名称、
/// 所属 Gateway 或页面来源推断。」时限改由调用方的 <see cref="CancellationToken"/> 承担。
///
/// 注意<b>不是</b>依 ADR 0015 决策 3.3 拆的：该条原文带「且读操作确需自动重试时」这一限定，
/// 而本次拆分与重试无关（两面都不自动重试）。
/// </summary>
public interface IBusinessFileTransferClient
{
    Task ProxyShiftHandoverAttachmentTusHeadAsync(
        string internalBearerToken,
        string uploadSessionId,
        HttpResponse targetResponse,
        CancellationToken cancellationToken);

    Task ProxyShiftHandoverAttachmentTusPatchAsync(
        string internalBearerToken,
        string uploadSessionId,
        HttpRequest sourceRequest,
        HttpResponse targetResponse,
        CancellationToken cancellationToken);

    /// <summary>
    /// 兑换已授权凭据取字节。凭据由 <see cref="IBusinessFileStorageClient.AuthorizeShiftHandoverAttachmentDownloadAsync"/>
    /// 在 JSON 面产出（用途已复核、URL 已校验），调用方全程拿不到 FileStorage 的 downloadGrantId——
    /// 该 id 是全服务共用命名空间且兑换面不校验用途，交出去就成了跨门面兑换通道（#3096 审核 A1）。
    /// 本方法只做一跳真字节转发，这是本 client 存在的全部理由（#3096 审核 Q2）。
    /// </summary>
    Task StreamShiftHandoverAttachmentContentAsync(
        string internalBearerToken,
        ShiftHandoverAttachmentDownloadTicket ticket,
        HttpResponse targetResponse,
        CancellationToken cancellationToken);
}

public sealed class HttpBusinessFileTransferClient(HttpClient httpClient)
    : BusinessServiceHttpClient(httpClient), IBusinessFileTransferClient
{
    private static readonly string[] TusForwardedRequestHeaders =
    [
        "Tus-Resumable",
        "Upload-Offset",
        "Upload-Checksum",
    ];

    private static readonly HashSet<string> HopByHopResponseHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection",
        "Content-Length",
        "Keep-Alive",
        "Proxy-Authenticate",
        "Proxy-Authorization",
        "TE",
        "Trailer",
        "Transfer-Encoding",
        "Upgrade",
    };

    public Task ProxyShiftHandoverAttachmentTusHeadAsync(
        string internalBearerToken,
        string uploadSessionId,
        HttpResponse targetResponse,
        CancellationToken cancellationToken) =>
        ProxyRawAsync(
            HttpMethod.Head,
            TusRequestUri(uploadSessionId),
            internalBearerToken,
            sourceRequest: null,
            targetResponse,
            additionalHeaders: null,
            cancellationToken);

    public Task ProxyShiftHandoverAttachmentTusPatchAsync(
        string internalBearerToken,
        string uploadSessionId,
        HttpRequest sourceRequest,
        HttpResponse targetResponse,
        CancellationToken cancellationToken) =>
        ProxyRawAsync(
            HttpMethod.Patch,
            TusRequestUri(uploadSessionId),
            internalBearerToken,
            sourceRequest,
            targetResponse,
            additionalHeaders: null,
            cancellationToken);

    public Task StreamShiftHandoverAttachmentContentAsync(
        string internalBearerToken,
        ShiftHandoverAttachmentDownloadTicket ticket,
        HttpResponse targetResponse,
        CancellationToken cancellationToken) =>
        ProxyRawAsync(
            HttpMethod.Get,
            ticket.DownstreamUrl,
            internalBearerToken,
            sourceRequest: null,
            targetResponse,
            ticket.TransferHeaders,
            cancellationToken);

    private static string TusRequestUri(string uploadSessionId) =>
        $"/api/files/v1/tus/{Uri.EscapeDataString(uploadSessionId)}";

    private async Task ProxyRawAsync(
        HttpMethod method,
        string requestUri,
        string internalBearerToken,
        HttpRequest? sourceRequest,
        HttpResponse targetResponse,
        IReadOnlyDictionary<string, string>? additionalHeaders,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(method, requestUri);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", internalBearerToken);
        FileStorageRoutes.CopyHeaders(additionalHeaders, message);

        if (sourceRequest is not null)
        {
            foreach (var name in TusForwardedRequestHeaders)
            {
                if (sourceRequest.Headers.TryGetValue(name, out var values))
                {
                    message.Headers.TryAddWithoutValidation(name, values.ToArray());
                }
            }

            message.Content = new StreamContent(sourceRequest.Body);
            if (!string.IsNullOrWhiteSpace(sourceRequest.ContentType))
            {
                message.Content.Headers.TryAddWithoutValidation("Content-Type", sourceRequest.ContentType);
            }
        }

        using var response = await SendRawAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        targetResponse.StatusCode = (int)response.StatusCode;
        CopyResponseHeaders(response, targetResponse);
        if (method != HttpMethod.Head)
        {
            await response.Content.CopyToAsync(targetResponse.Body, cancellationToken);
        }
    }

    private static void CopyResponseHeaders(HttpResponseMessage sourceResponse, HttpResponse targetResponse)
    {
        // `Connection` 头自身列出的字段同样是 hop-by-hop（RFC 9110 §7.6.1），必须连同静态名单
        // 一起挡掉；只比静态名单会把下游声明的动态 token 原样转发给浏览器。
        var connectionHeaderValues = sourceResponse.Headers.Connection
            .SelectMany(value => value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var header in sourceResponse.Headers.Concat(sourceResponse.Content.Headers))
        {
            if (!HopByHopResponseHeaders.Contains(header.Key)
                && !connectionHeaderValues.Contains(header.Key))
            {
                targetResponse.Headers[header.Key] = header.Value.ToArray();
            }
        }
    }
}
