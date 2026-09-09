using System.Net;
using System.Net.Http.Headers;
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

    /// <summary>
    /// 交接班附件下载的**唯一**授权入口：复核用途、签发 download grant、校验下游 URL 可代理，
    /// 返回只在网关进程内流转的取字节凭据。用途复核只在本方法一处把关（#3096 审核 A1）；
    /// 这两发都是纯 JSON RPC，因此留在 JSON 面的弹性管线上（#3096 审核 Q2）。
    /// </summary>
    Task<ShiftHandoverAttachmentDownloadTicket> AuthorizeShiftHandoverAttachmentDownloadAsync(
        string internalBearerToken,
        string fileId,
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken);
}

/// <summary>
/// 已通过用途复核并签发完成的取字节凭据。**不出网关进程**：它携带 FileStorage 内部路径，
/// 既不是公开契约类型，也不进 OpenAPI。
/// </summary>
public sealed record ShiftHandoverAttachmentDownloadTicket(
    string DownstreamUrl,
    IReadOnlyDictionary<string, string> TransferHeaders);

/// <summary>
/// FileStorage 的 JSON 面。挂在按幂等性二分的 <c>NonIdempotentSafe</c> 弹性管线上（10 秒总超时 + 熔断）；
/// 字节面不共用这条管线，见 <see cref="IBusinessFileTransferClient"/> 与 ADR 0015。
/// </summary>
public sealed class HttpBusinessFileStorageClient(HttpClient httpClient)
    : BusinessServiceHttpClient(httpClient), IBusinessFileStorageClient
{
    // FileStorage 的错误载体是裸 FileStorageError{Code,Message}，不套平台 ResponseData envelope。
    // 不声明的话，非 400 响应的 code 与 message 会双双落空（#3096 审核阻断 A）。
    protected override bool AcceptsBareDownstreamErrorPayload => true;

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
            FileStorageRoutes.RewriteProxiedUrl(
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
        FileStorageRoutes.CopyHeaders(downloadHeaders, message);

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
        if (!string.Equals(session.Provider, ShiftHandoverAttachments.TusUploadProtocol, StringComparison.Ordinal))
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "filestorage-upload-protocol-unsupported");
        }

        return new BusinessConsoleShiftHandoverAttachmentUploadSessionResponse(
            session.UploadSessionId,
            session.FileId,
            ShiftHandoverAttachments.TusUploadProtocol,
            session.ExpiresAtUtc,
            FileStorageRoutes.RewriteProxiedUrl(
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

    public async Task<ShiftHandoverAttachmentDownloadTicket> AuthorizeShiftHandoverAttachmentDownloadAsync(
        string internalBearerToken,
        string fileId,
        string organizationId,
        string environmentId,
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

        FileStorageRoutes.RequireProxyableDownstreamUrl(
            grant.Download.Url,
            FileStorageRoutes.DownstreamDownloadGrantPrefix);

        return new ShiftHandoverAttachmentDownloadTicket(grant.Download.Url, grant.Download.Headers);
    }
}

/// <summary>FileStorage 下游路径与 BusinessGateway 受控路径的对应关系，两个门面共用。</summary>
public static class FileStorageRoutes
{
    public const string DownstreamTusPrefix = "/api/files/v1/tus/";
    public const string DownstreamDownloadGrantPrefix = "/api/files/v1/download-grants/";

    public const string ConsoleSopDownloadGrantPrefix = "/api/business-console/v1/files/download-grants/";
    public const string ConsoleShiftHandoverTusPrefix = "/api/business-console/v1/files/shift-handover-attachments/tus/";

    public static string RewriteProxiedUrl(string url, string downstreamPrefix, string consolePrefix)
    {
        RequireProxyableDownstreamUrl(url, downstreamPrefix);
        return consolePrefix + url[downstreamPrefix.Length..];
    }

    /// <summary>
    /// FileStorage 只应回内部相对路径。绝对 URL、协议相对 URL 或前缀不符都意味着
    /// 内部地址会漏给调用方或被本网关跟随，失败关闭（ADR 0023 决策 1.3、ADR 0030 决策 1）。
    /// </summary>
    public static void RequireProxyableDownstreamUrl(string url, string downstreamPrefix)
    {
        if (!url.StartsWith(downstreamPrefix, StringComparison.Ordinal))
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "filestorage-transfer-url-not-proxyable");
        }
    }

    public static void CopyHeaders(IReadOnlyDictionary<string, string>? headers, HttpRequestMessage targetRequest)
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
}

/// <summary>
/// 交接班附件门面的固定值：用途与 owner 由 BusinessGateway 决定，不从请求体读取。
/// </summary>
public static class ShiftHandoverAttachments
{
    public const string FilePurpose = "shift-handover-photo";
    public const string OwnerService = "business-mes";
    public const string OwnerType = "shift-handover-attachment";
    public const string TusUploadProtocol = "tus";
}
