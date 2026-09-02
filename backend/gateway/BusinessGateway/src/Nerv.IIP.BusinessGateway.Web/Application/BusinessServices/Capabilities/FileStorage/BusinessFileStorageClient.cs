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

    Task<BusinessConsoleShiftHandoverAttachmentDownloadGrantResponse> CreateShiftHandoverAttachmentDownloadGrantAsync(
        string internalBearerToken,
        string fileId,
        BusinessConsoleCreateShiftHandoverAttachmentDownloadGrantRequest request,
        CancellationToken cancellationToken);

    Task ProxyShiftHandoverAttachmentTusAsync(
        string internalBearerToken,
        string uploadSessionId,
        HttpRequest? sourceRequest,
        HttpResponse targetResponse,
        CancellationToken cancellationToken);

    Task ProxyShiftHandoverAttachmentContentAsync(
        string internalBearerToken,
        string downloadGrantId,
        string organizationId,
        string environmentId,
        HttpResponse targetResponse,
        CancellationToken cancellationToken);
}

public sealed class HttpBusinessFileStorageClient(HttpClient httpClient) : IBusinessFileStorageClient
{
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
        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/files/v1/files/{Uri.EscapeDataString(fileId)}/download-grants");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", internalBearerToken);
        message.Content = JsonContent.Create(new CreateDownloadGrantRequest(request.OrganizationId, request.EnvironmentId));
        using var response = await SendAsync(message, HttpCompletionOption.ResponseContentRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(response.StatusCode, "filestorage-download-grant-failed");
        }

        var grant = await response.Content.ReadFromJsonAsync<DownloadGrantResponse>(cancellationToken: cancellationToken)
            ?? throw BusinessServiceProxyException.FromSafeDownstreamMessage(HttpStatusCode.BadGateway, "filestorage-empty-response");
        return new BusinessConsoleSopFileDownloadGrantResponse(
            grant.FileId,
            grant.ExpiresAtUtc,
            RewriteDownloadGrantContentUrl(grant.Download.Url),
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
        foreach (var (key, value) in downloadHeaders)
        {
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
            {
                message.Headers.TryAddWithoutValidation(key, value);
            }
        }

        using var response = await SendAsync(message, HttpCompletionOption.ResponseContentRead, cancellationToken);
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
        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/files/v1/upload-sessions");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", internalBearerToken);
        message.Content = JsonContent.Create(new CreateUploadSessionRequest(
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
            request.Checksum));
        using var response = await SendAsync(message, HttpCompletionOption.ResponseContentRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(response.StatusCode, "filestorage-upload-session-failed");
        }

        var session = await response.Content.ReadFromJsonAsync<CreateUploadSessionResponse>(cancellationToken: cancellationToken)
            ?? throw BusinessServiceProxyException.FromSafeDownstreamMessage(HttpStatusCode.BadGateway, "filestorage-empty-response");

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
                ShiftHandoverAttachments.DownstreamTusPrefix,
                ShiftHandoverAttachments.ConsoleTusPrefix),
            session.Upload.Headers);
    }

    public async Task<BusinessConsoleMesShiftHandoverAttachment> CompleteShiftHandoverAttachmentUploadAsync(
        string internalBearerToken,
        string uploadSessionId,
        BusinessConsoleCompleteShiftHandoverAttachmentUploadRequest request,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/files/v1/upload-sessions/{Uri.EscapeDataString(uploadSessionId)}/complete");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", internalBearerToken);
        message.Content = JsonContent.Create(new CompleteUploadSessionRequest(
            request.OrganizationId,
            request.EnvironmentId,
            ShiftHandoverAttachments.FilePurpose,
            request.Checksum,
            request.SizeBytes));
        using var response = await SendAsync(message, HttpCompletionOption.ResponseContentRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(response.StatusCode, "filestorage-upload-complete-failed");
        }

        var file = await response.Content.ReadFromJsonAsync<FileMetadataResponse>(cancellationToken: cancellationToken)
            ?? throw BusinessServiceProxyException.FromSafeDownstreamMessage(HttpStatusCode.BadGateway, "filestorage-empty-response");

        // complete 的返回值就是交接班写面要的那四个字段：调用方不需要再自己拼附件行。
        return new BusinessConsoleMesShiftHandoverAttachment(
            file.FileId,
            file.FileName,
            file.ContentType,
            file.SizeBytes);
    }

    public async Task<BusinessConsoleShiftHandoverAttachmentDownloadGrantResponse> CreateShiftHandoverAttachmentDownloadGrantAsync(
        string internalBearerToken,
        string fileId,
        BusinessConsoleCreateShiftHandoverAttachmentDownloadGrantRequest request,
        CancellationToken cancellationToken)
    {
        // business.mes.handovers.read 只授权读交接班照片。FileStorage 的 download-grant 不看用途，
        // 所以用途口径必须在这里收：否则持交接班读权限的人可以拿任意 fileId（例如工程 SOP 文件）换字节。
        var metadata = await GetFileMetadataAsync(internalBearerToken, fileId, cancellationToken);
        if (!string.Equals(metadata.FilePurpose, ShiftHandoverAttachments.FilePurpose, StringComparison.Ordinal))
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.NotFound,
                "filestorage-file-not-shift-handover-attachment");
        }

        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/files/v1/files/{Uri.EscapeDataString(fileId)}/download-grants");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", internalBearerToken);
        message.Content = JsonContent.Create(new CreateDownloadGrantRequest(request.OrganizationId, request.EnvironmentId));
        using var response = await SendAsync(message, HttpCompletionOption.ResponseContentRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(response.StatusCode, "filestorage-download-grant-failed");
        }

        var grant = await response.Content.ReadFromJsonAsync<DownloadGrantResponse>(cancellationToken: cancellationToken)
            ?? throw BusinessServiceProxyException.FromSafeDownstreamMessage(HttpStatusCode.BadGateway, "filestorage-empty-response");
        return new BusinessConsoleShiftHandoverAttachmentDownloadGrantResponse(
            grant.FileId,
            grant.ExpiresAtUtc,
            RewriteProxiedUrl(
                grant.Download.Url,
                ShiftHandoverAttachments.DownstreamDownloadGrantPrefix,
                ShiftHandoverAttachments.ConsoleDownloadGrantPrefix),
            grant.Download.Headers);
    }

    public Task ProxyShiftHandoverAttachmentTusAsync(
        string internalBearerToken,
        string uploadSessionId,
        HttpRequest? sourceRequest,
        HttpResponse targetResponse,
        CancellationToken cancellationToken) =>
        ProxyRawAsync(
            sourceRequest is null ? HttpMethod.Head : HttpMethod.Patch,
            $"/api/files/v1/tus/{Uri.EscapeDataString(uploadSessionId)}",
            internalBearerToken,
            sourceRequest,
            targetResponse,
            null,
            cancellationToken);

    public Task ProxyShiftHandoverAttachmentContentAsync(
        string internalBearerToken,
        string downloadGrantId,
        string organizationId,
        string environmentId,
        HttpResponse targetResponse,
        CancellationToken cancellationToken) =>
        ProxyRawAsync(
            HttpMethod.Get,
            $"/api/files/v1/download-grants/{Uri.EscapeDataString(downloadGrantId)}/content",
            internalBearerToken,
            null,
            targetResponse,
            new Dictionary<string, string>
            {
                ["X-Organization-Id"] = organizationId,
                ["X-Environment-Id"] = environmentId,
            },
            cancellationToken);

    private async Task<FileMetadataResponse> GetFileMetadataAsync(
        string internalBearerToken,
        string fileId,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/files/v1/files/{Uri.EscapeDataString(fileId)}");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", internalBearerToken);
        using var response = await SendAsync(message, HttpCompletionOption.ResponseContentRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(response.StatusCode, "filestorage-file-metadata-failed");
        }

        return await response.Content.ReadFromJsonAsync<FileMetadataResponse>(cancellationToken: cancellationToken)
            ?? throw BusinessServiceProxyException.FromSafeDownstreamMessage(HttpStatusCode.BadGateway, "filestorage-empty-response");
    }

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
        if (additionalHeaders is not null)
        {
            foreach (var header in additionalHeaders)
            {
                message.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

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

        using var response = await SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        targetResponse.StatusCode = (int)response.StatusCode;
        CopyResponseHeaders(response, targetResponse);
        if (method != HttpMethod.Head)
        {
            await response.Content.CopyToAsync(targetResponse.Body, cancellationToken);
        }
    }

    // 下游连不上时必须映射成可诊断的 503：否则 HttpRequestException 逃逸到 FastEndpoints，
    // 调用方只会看到 500「未知错误」，排障要靠翻 Gateway 日志。
    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage message,
        HttpCompletionOption completionOption,
        CancellationToken cancellationToken)
    {
        try
        {
            return await httpClient.SendAsync(message, completionOption, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.ServiceUnavailable,
                "filestorage-unavailable",
                exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.ServiceUnavailable,
                "filestorage-unavailable",
                exception);
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
        // FileStorage 只会回相对内部路径；出现绝对/协议相对 URL 说明内部地址会漏给调用方（ADR 0023 决策 1.3）。
        if (!url.StartsWith(downstreamPrefix, StringComparison.Ordinal))
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "filestorage-transfer-url-not-proxyable");
        }

        return consolePrefix + url[downstreamPrefix.Length..];
    }

    private static string RewriteDownloadGrantContentUrl(string downloadUrl)
    {
        const string fileStoragePrefix = "/api/files/v1/download-grants/";
        const string businessConsolePrefix = "/api/business-console/v1/files/download-grants/";
        return downloadUrl.StartsWith(fileStoragePrefix, StringComparison.Ordinal)
            ? businessConsolePrefix + downloadUrl[fileStoragePrefix.Length..]
            : downloadUrl;
    }
}

/// <summary>
/// 交接班附件门面的固定值与路径前缀：用途、owner 与两条受控代理路径都由 BusinessGateway 决定，
/// 不从请求体读取，也不把 FileStorage 内部路径交给调用方。
/// </summary>
public static class ShiftHandoverAttachments
{
    public const string FilePurpose = "shift-handover-photo";
    public const string OwnerService = "business-mes";
    public const string OwnerType = "shift-handover-attachment";

    public const string DownstreamTusPrefix = "/api/files/v1/tus/";
    public const string ConsoleTusPrefix = "/api/business-console/v1/files/shift-handover-attachments/tus/";
    public const string DownstreamDownloadGrantPrefix = "/api/files/v1/download-grants/";
    public const string ConsoleDownloadGrantPrefix = "/api/business-console/v1/files/shift-handover-attachments/download-grants/";
}
