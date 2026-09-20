using System.Net;
using System.Net.Http.Headers;
using Nerv.IIP.Contracts.FileStorage;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public interface IBusinessFileStorageClient
{
    /// <summary>
    /// 工程 SOP 文件下载的**唯一**授权入口：复核用途、签发 download grant、校验下游 URL 可代理，
    /// 返回只在网关进程内流转的取字节凭据。调用方全程拿不到 downloadGrantId（#3314）。
    /// </summary>
    Task<BusinessFileDownloadTicket> AuthorizeSopFileDownloadAsync(
        string internalBearerToken,
        string fileId,
        string organizationId,
        string environmentId,
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
    Task<BusinessFileDownloadTicket> AuthorizeShiftHandoverAttachmentDownloadAsync(
        string internalBearerToken,
        string fileId,
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken);
}

/// <summary>
/// 已通过用途复核并签发完成的取字节凭据。**不出网关进程**：它携带 FileStorage 内部路径，
/// 既不是公开契约类型，也不进 OpenAPI。
///
/// #3314 第 2 轮审核 E1 的结构性替代。前两轮的护栏写在**契约形状**上（先禁参数拼写、
/// 再禁路径前缀与参数枚举），连续被三种形状打穿：换个参数名、把标识改走 query、把路由挂到
/// 扫描前缀之外——每一种都让「调用方携带 grant id 并据此兑换」原样复活。按本仓判据，
/// 连续多轮点名同类特例时应换结构性替代，而不是加第四条谓词。
///
/// 替代就是本类型：**构造函数私有，没有接受 URL 字符串或 grant 标识的入口**。唯一的工厂
/// <see cref="FromSignedGrant"/> 的入参是 FileStorage 的签发响应，不是任何调用方值；而字节面的
/// <see cref="IBusinessFileTransferClient.StreamFileContentAsync"/> 只接受本类型、不接受字符串。
/// 于是「拿调用方传来的 grant id 去兑换」这句话在字节面上**写不出来**——无论那个标识走 path
/// 还是 query、路由叫什么名字、挂在哪个前缀下。
///
/// **不自称完备**：在本文件里新增第二个工厂、或伪造一个 <c>DownloadGrantResponse</c> 再喂给
/// <see cref="FromSignedGrant"/>，仍可绕过。区别在于暴露面从「任意一处的任意字符串」收缩成
/// 「这一个类型上的工厂集合」——那是一次显式的、评审看得见的编辑。
/// </summary>
public sealed record BusinessFileDownloadTicket
{
    private BusinessFileDownloadTicket(string downstreamUrl, IReadOnlyDictionary<string, string> transferHeaders)
    {
        DownstreamUrl = downstreamUrl;
        TransferHeaders = transferHeaders;
    }

    public string DownstreamUrl { get; }

    public IReadOnlyDictionary<string, string> TransferHeaders { get; }

    /// <summary>
    /// 由**本网关刚刚签发**的 download grant 产出取字节凭据。FileStorage 只应回内部相对路径；
    /// 绝对 URL、协议相对 URL 与前缀不符都在这里失败关闭（ADR 0023 决策 1.3、ADR 0030 决策 1）。
    /// </summary>
    public static BusinessFileDownloadTicket FromSignedGrant(DownloadGrantResponse grant)
    {
        FileStorageRoutes.RequireProxyableDownstreamUrl(
            grant.Download.Url,
            FileStorageRoutes.DownstreamDownloadGrantPrefix);

        return new BusinessFileDownloadTicket(grant.Download.Url, grant.Download.Headers);
    }
}

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

    public Task<BusinessFileDownloadTicket> AuthorizeSopFileDownloadAsync(
        string internalBearerToken,
        string fileId,
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken) =>
        AuthorizeDownloadAsync(
            internalBearerToken,
            fileId,
            organizationId,
            environmentId,
            EngineeringDocuments.FilePurpose,
            "filestorage-file-not-engineering-document",
            cancellationToken);

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

    public Task<BusinessFileDownloadTicket> AuthorizeShiftHandoverAttachmentDownloadAsync(
        string internalBearerToken,
        string fileId,
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken) =>
        AuthorizeDownloadAsync(
            internalBearerToken,
            fileId,
            organizationId,
            environmentId,
            ShiftHandoverAttachments.FilePurpose,
            "filestorage-file-not-shift-handover-attachment",
            cancellationToken);

    /// <summary>
    /// 两个业务门面共用的下载授权序列。业务域读权限只授权读**本门面用途**的文件，而 FileStorage 的
    /// download-grant 既不看用途、也不记签发门面，所以用途口径必须在这里收：否则持任一门面读权限的
    /// 主体可以拿任意 fileId（例如另一门面的文件）换字节。
    ///
    /// 签发出来的 grant id **不出本进程**——它由 <see cref="IBusinessFileTransferClient.StreamFileContentAsync"/>
    /// 就地兑换。#3314 实测过相反做法：把 id 交给调用方后，两条权限口径不同的网关路由可以互相兑换
    /// 对方签发的 grant，三层没有一层拒绝。
    /// </summary>
    private async Task<BusinessFileDownloadTicket> AuthorizeDownloadAsync(
        string internalBearerToken,
        string fileId,
        string organizationId,
        string environmentId,
        string requiredFilePurpose,
        string purposeMismatchCode,
        CancellationToken cancellationToken)
    {
        var metadata = await SendAsync<FileMetadataResponse>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/files/v1/files/{Uri.EscapeDataString(fileId)}",
            body: null,
            cancellationToken);
        if (!string.Equals(metadata.FilePurpose, requiredFilePurpose, StringComparison.Ordinal))
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.NotFound,
                purposeMismatchCode);
        }

        var grant = await SendAsync<DownloadGrantResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/files/v1/files/{Uri.EscapeDataString(fileId)}/download-grants",
            new CreateDownloadGrantRequest(organizationId, environmentId),
            cancellationToken);

        return BusinessFileDownloadTicket.FromSignedGrant(grant);
    }
}

/// <summary>FileStorage 下游路径与 BusinessGateway 受控路径的对应关系，两个门面共用。</summary>
public static class FileStorageRoutes
{
    public const string DownstreamTusPrefix = "/api/files/v1/tus/";
    public const string DownstreamDownloadGrantPrefix = "/api/files/v1/download-grants/";

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
/// 工程 SOP 文件门面的固定值：用途由 BusinessGateway 决定，不从请求体读取。
/// </summary>
public static class EngineeringDocuments
{
    public const string FilePurpose = "engineering-document";
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
