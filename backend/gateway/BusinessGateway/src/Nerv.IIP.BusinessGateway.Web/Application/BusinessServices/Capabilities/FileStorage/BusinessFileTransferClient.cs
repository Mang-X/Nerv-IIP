using System.Net;
using System.Net.Http.Headers;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

/// <summary>
/// FileStorage 的**字节面**。与 <see cref="IBusinessFileStorageClient"/> 的区别不是领域，而是
/// 弹性契约：ADR 0015 的策略表按幂等性二分、只覆盖 JSON RPC 形状，其 10 秒总超时会在弱网下
/// 必然切断 <c>shift-handover-photo</c> 允许的 20,971,520 bytes 级 tus <c>PATCH</c>
/// （`FileStorage.Web/appsettings.json` 的 `MaximumFileSizeBytes`），并把共享熔断器连带打开。
/// 依 ADR 0015「同一客户端读写弹性需求不同就拆成两个 HttpClient 注册」拆出，时限由调用方
/// <see cref="CancellationToken"/> 承担。
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
        foreach (var header in sourceResponse.Headers.Concat(sourceResponse.Content.Headers))
        {
            if (!HopByHopResponseHeaders.Contains(header.Key))
            {
                targetResponse.Headers[header.Key] = header.Value.ToArray();
            }
        }
    }
}
