using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Nerv.IIP.Contracts.FileStorage;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public interface IBusinessFileStorageClient
{
    Task<BusinessConsoleSopFileDownloadGrantResponse> CreateSopFileDownloadGrantAsync(
        string internalBearerToken,
        string fileId,
        BusinessConsoleCreateSopFileDownloadGrantRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleSopFileContentResponse> DownloadSopFileContentAsync(
        string internalBearerToken,
        string downloadGrantId,
        IReadOnlyDictionary<string, string> downloadHeaders,
        CancellationToken cancellationToken);

    Task<BusinessConsoleShiftHandoverAttachmentUploadSessionResponse> CreateShiftHandoverAttachmentUploadSessionAsync(
        string internalBearerToken,
        string ownerId,
        BusinessConsoleCreateShiftHandoverAttachmentUploadSessionRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesShiftHandoverAttachment> CompleteShiftHandoverAttachmentUploadAsync(
        string internalBearerToken,
        string uploadSessionId,
        BusinessConsoleCompleteShiftHandoverAttachmentUploadRequest request,
        CancellationToken cancellationToken);

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
    /// 交接班附件字节面。下载授权由本方法在服务端签发并**立即兑换**，调用方拿不到 downloadGrantId：
    /// FileStorage 的 grant id 是全服务共用命名空间且兑换面不校验用途，一旦把 id 交到调用方手里，
    /// 任何持交接班读权限的主体都能兑换别的门面（例如工程 SOP）签发的 grant。见 #3096 审核 A1。
    /// </summary>
    Task StreamShiftHandoverAttachmentContentAsync(
        string internalBearerToken,
        string fileId,
        string organizationId,
        string environmentId,
        HttpResponse targetResponse,
        CancellationToken cancellationToken);
}

public sealed class HttpBusinessFileStorageClient : BusinessServiceHttpClient, IBusinessFileStorageClient
{
    private readonly HttpClient httpClient;

    public HttpBusinessFileStorageClient(HttpClient httpClient)
        : base(httpClient) => this.httpClient = httpClient;

    private const string TusUploadProtocol = "tus";

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

    public async Task<BusinessConsoleSopFileDownloadGrantResponse> CreateSopFileDownloadGrantAsync(
        string internalBearerToken,
        string fileId,
        BusinessConsoleCreateSopFileDownloadGrantRequest request,
        CancellationToken cancellationToken)
    {
        var grant = await SendAsync<DownloadGrantResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/files/v1/files/{Uri.EscapeDataString(fileId)}/download-grants",
            new CreateDownloadGrantRequest(request.OrganizationId, request.EnvironmentId),
            cancellationToken);
        return new BusinessConsoleSopFileDownloadGrantResponse(
            grant.FileId,
            grant.ExpiresAtUtc,
            RewriteProxiedUrl(
                grant.Download.Url,
                FileStorageRoutes.DownstreamDownloadGrantPrefix,
                FileStorageRoutes.ConsoleSopDownloadGrantPrefix),
            grant.Download.Headers);
    }

    public async Task<BusinessConsoleSopFileContentResponse> DownloadSopFileContentAsync(
        string internalBearerToken,
        string downloadGrantId,
        IReadOnlyDictionary<string, string> downloadHeaders,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/files/v1/download-grants/{Uri.EscapeDataString(downloadGrantId)}/content");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", internalBearerToken);
        CopyHeaders(downloadHeaders, message);

        using var response = await SendRawAsync(message, HttpCompletionOption.ResponseContentRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(response.StatusCode, "filestorage-download-content-failed");
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return new BusinessConsoleSopFileContentResponse(
            response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream",
            response.Content.Headers.ContentLength,
            bytes);
    }

    public async Task<BusinessConsoleShiftHandoverAttachmentUploadSessionResponse> CreateShiftHandoverAttachmentUploadSessionAsync(
        string internalBearerToken,
        string ownerId,
        BusinessConsoleCreateShiftHandoverAttachmentUploadSessionRequest request,
        CancellationToken cancellationToken)
    {
        var session = await SendAsync<CreateUploadSessionResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/files/v1/upload-sessions",
            new CreateUploadSessionRequest(
                request.OrganizationId,
                request.EnvironmentId,
                new OwnerReference(
                    ShiftHandoverAttachments.OwnerService,
                    ShiftHandoverAttachments.OwnerType,
                    ownerId),
                ShiftHandoverAttachments.FilePurpose,
                request.FileName,
                request.ContentType,
                request.ExpectedSizeBytes,
                request.Checksum),
            cancellationToken);

        // ADR 0023：tus 是唯一目标传输协议，默认 server-proxy 只生成没有字节 endpoint 的占位指令。
        // 把占位指令原样交给调用方等于发一个必然写不进字节的 URL，所以这里失败关闭。
        if (!string.Equals(session.Provider, TusUploadProtocol, StringComparison.Ordinal))
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "filestorage-upload-protocol-unsupported");
        }

        return new BusinessConsoleShiftHandoverAttachmentUploadSessionResponse(
            session.UploadSessionId,
            session.FileId,
            TusUploadProtocol,
            session.ExpiresAtUtc,
            RewriteProxiedUrl(
                session.Upload.Url,
                FileStorageRoutes.DownstreamTusPrefix,
                FileStorageRoutes.ConsoleShiftHandoverTusPrefix),
            session.Upload.Headers);
    }

    public async Task<BusinessConsoleMesShiftHandoverAttachment> CompleteShiftHandoverAttachmentUploadAsync(
        string internalBearerToken,
        string uploadSessionId,
        BusinessConsoleCompleteShiftHandoverAttachmentUploadRequest request,
        CancellationToken cancellationToken)
    {
        var file = await SendAsync<FileMetadataResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/files/v1/upload-sessions/{Uri.EscapeDataString(uploadSessionId)}/complete",
            new CompleteUploadSessionRequest(
                request.OrganizationId,
                request.EnvironmentId,
                ShiftHandoverAttachments.FilePurpose,
                request.Checksum,
                request.SizeBytes),
            cancellationToken);

        // complete 的返回值就是交接班写面要的那四个字段：调用方不需要再自己拼附件行。
        return new BusinessConsoleMesShiftHandoverAttachment(
            file.FileId,
            file.FileName,
            file.ContentType,
            file.SizeBytes);
    }

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

    public async Task StreamShiftHandoverAttachmentContentAsync(
        string internalBearerToken,
        string fileId,
        string organizationId,
        string environmentId,
        HttpResponse targetResponse,
        CancellationToken cancellationToken)
    {
        // business.mes.handovers.read 只授权读交接班照片。FileStorage 的 download-grant 不看用途，
        // 所以用途口径必须在这里收：否则持交接班读权限的人可以拿任意 fileId（例如工程 SOP 文件）换字节。
        var metadata = await SendAsync<FileMetadataResponse>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/files/v1/files/{Uri.EscapeDataString(fileId)}",
            body: null,
            cancellationToken);
        if (!string.Equals(metadata.FilePurpose, ShiftHandoverAttachments.FilePurpose, StringComparison.Ordinal))
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.NotFound,
                "filestorage-file-not-shift-handover-attachment");
        }

        var grant = await SendAsync<DownloadGrantResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/files/v1/files/{Uri.EscapeDataString(fileId)}/download-grants",
            new CreateDownloadGrantRequest(organizationId, environmentId),
            cancellationToken);

        // 下游只应回内部相对路径；出现绝对/协议相对 URL 说明内部地址会被跟随（ADR 0023 决策 1.3）。
        RequireProxyableDownstreamUrl(grant.Download.Url, FileStorageRoutes.DownstreamDownloadGrantPrefix);

        await ProxyRawAsync(
            HttpMethod.Get,
            grant.Download.Url,
            internalBearerToken,
            sourceRequest: null,
            targetResponse,
            grant.Download.Headers,
            cancellationToken);
    }

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
        CopyHeaders(additionalHeaders, message);

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

    /// <summary>
    /// 字节面自己发请求（基类的 <c>SendAsync</c> 只处理 ResponseData JSON），
    /// 但传输故障映射与基类保持同一口径：连不上 503、超时 504，不让 <see cref="HttpRequestException"/>
    /// 逃逸成 500「未知错误」。
    /// </summary>
    private async Task<HttpResponseMessage> SendRawAsync(
        HttpRequestMessage message,
        HttpCompletionOption completionOption,
        CancellationToken cancellationToken)
    {
        try
        {
            return await httpClient.SendAsync(message, completionOption, cancellationToken);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.ServiceUnavailable,
                "downstream-timeout",
                exception);
        }
        catch (Polly.Timeout.TimeoutRejectedException exception)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.GatewayTimeout,
                "downstream-timeout",
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.ServiceUnavailable,
                "downstream-unavailable",
                exception);
        }
    }

    private static void CopyHeaders(IReadOnlyDictionary<string, string>? headers, HttpRequestMessage targetRequest)
    {
        if (headers is null)
        {
            return;
        }

        foreach (var (key, value) in headers)
        {
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
            {
                targetRequest.Headers.TryAddWithoutValidation(key, value);
            }
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

    private static string RewriteProxiedUrl(string url, string downstreamPrefix, string consolePrefix)
    {
        RequireProxyableDownstreamUrl(url, downstreamPrefix);
        return consolePrefix + url[downstreamPrefix.Length..];
    }

    private static void RequireProxyableDownstreamUrl(string url, string downstreamPrefix)
    {
        // FileStorage 只应回内部相对路径。绝对 URL、协议相对 URL 或前缀不符都意味着
        // 内部地址会漏给调用方或被本网关跟随，失败关闭（ADR 0023 决策 1.3、ADR 0030）。
        if (!url.StartsWith(downstreamPrefix, StringComparison.Ordinal))
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "filestorage-transfer-url-not-proxyable");
        }
    }
}

/// <summary>FileStorage 下游路径与 BusinessGateway 受控路径的对应关系，两个门面共用。</summary>
public static class FileStorageRoutes
{
    public const string DownstreamTusPrefix = "/api/files/v1/tus/";
    public const string DownstreamDownloadGrantPrefix = "/api/files/v1/download-grants/";

    public const string ConsoleSopDownloadGrantPrefix = "/api/business-console/v1/files/download-grants/";
    public const string ConsoleShiftHandoverTusPrefix = "/api/business-console/v1/files/shift-handover-attachments/tus/";
}

/// <summary>
/// 交接班附件门面的固定值：用途与 owner 由 BusinessGateway 决定，不从请求体读取。
/// </summary>
public static class ShiftHandoverAttachments
{
    public const string FilePurpose = "shift-handover-photo";
    public const string OwnerService = "business-mes";
    public const string OwnerType = "shift-handover-attachment";
}
