using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.Contracts.FileStorage;
using Nerv.IIP.PlatformGateway.Web.Application.Auth;
using Nerv.IIP.PlatformGateway.Web.Application.FileStorage;
using Nerv.IIP.PlatformGateway.Web.Application.OpenApi;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.PlatformGateway.Web.Endpoints.Files;

public sealed record ConsoleListFilesRequest(
    string? FilePurpose,
    string? UploaderId,
    string? OwnerId,
    DateTimeOffset? CreatedFromUtc,
    DateTimeOffset? CreatedToUtc,
    string? Status,
    int? Skip = null,
    int? Take = null);

public sealed record ConsoleFileStorageUsageRequest(string? FilePurpose);

public sealed record ConsoleCreateUploadSessionRequest(
    OwnerReference Owner,
    string FilePurpose,
    string FileName,
    string ContentType,
    long ExpectedSizeBytes,
    string? Checksum);

public sealed record ConsoleCompleteUploadSessionRequest(
    string FilePurpose,
    string? Checksum = null,
    long? SizeBytes = null);

[Tags("Console Files")]
[HttpPost("/api/console/v1/files/upload-sessions")]
[GatewayOperationId("createConsoleFileUploadSession")]
[Authorize(Policy = GatewayPolicies.ConsoleAuthenticated)]
public sealed class CreateConsoleFileUploadSessionEndpoint(
    IGatewayIamAuthClient iam,
    IGatewayAuthorizationClient auth,
    IGatewayFileStorageClient files)
    : AuthorizedProxyEndpoint<ConsoleCreateUploadSessionRequest, CreateUploadSessionResponse>(
        iam,
        auth,
        GatewayPermissions.FilesUpload)
{
    protected override Task<CreateUploadSessionResponse> ForwardAsync(
        AuthorizedProxyRequestContext context,
        ConsoleCreateUploadSessionRequest request,
        CancellationToken cancellationToken)
    {
        // 将本地 request 映射到共享 Contracts，添加 principal 的 org/env
        var contractRequest = new CreateUploadSessionRequest(
            context.Principal.OrganizationId,
            context.Principal.EnvironmentId,
            request.Owner,
            request.FilePurpose,
            request.FileName,
            request.ContentType,
            request.ExpectedSizeBytes,
            request.Checksum);
        return files.CreateUploadSessionAsync(contractRequest, cancellationToken);
    }
}

[Tags("Console Files")]
[HttpPost("/api/console/v1/files/upload-sessions/{uploadSessionId}/complete")]
[GatewayOperationId("completeConsoleFileUploadSession")]
[Authorize(Policy = GatewayPolicies.ConsoleAuthenticated)]
public sealed class CompleteConsoleFileUploadSessionEndpoint(
    IGatewayIamAuthClient iam,
    IGatewayAuthorizationClient auth,
    IGatewayFileStorageClient files)
    : AuthorizedProxyEndpoint<ConsoleCompleteUploadSessionRequest, FileMetadataResponse>(
        iam,
        auth,
        GatewayPermissions.FilesUpload)
{
    protected override Task<FileMetadataResponse> ForwardAsync(
        AuthorizedProxyRequestContext context,
        ConsoleCompleteUploadSessionRequest request,
        CancellationToken cancellationToken)
    {
        // 将本地 request 映射到共享 Contracts，添加 principal 的 org/env
        var contractRequest = new CompleteUploadSessionRequest(
            context.Principal.OrganizationId,
            context.Principal.EnvironmentId,
            request.FilePurpose,
            request.Checksum,
            request.SizeBytes);
        return files.CompleteUploadSessionAsync(
            Route<string>("uploadSessionId")!,
            contractRequest,
            cancellationToken);
    }
}

[Tags("Console Files")]
[HttpGet("/api/console/v1/files")]
[GatewayOperationId("listConsoleFiles")]
[Authorize(Policy = GatewayPolicies.ConsoleAuthenticated)]
public sealed class ListConsoleFilesEndpoint(
    IGatewayAuthorizationClient auth,
    IGatewayFileStorageClient files)
    : Endpoint<ConsoleListFilesRequest, ResponseData<FileListResponse>>
{
    public override async Task HandleAsync(ConsoleListFilesRequest req, CancellationToken ct)
    {
        var organizationId = HttpContext.Request.Headers["X-Organization-Id"].ToString();
        var environmentId = HttpContext.Request.Headers["X-Environment-Id"].ToString();
        if (string.IsNullOrWhiteSpace(organizationId) || string.IsNullOrWhiteSpace(environmentId))
        {
            await ResponseDataEndpointResults.WriteErrorAsync(
                HttpContext,
                StatusCodes.Status400BadRequest,
                "X-Organization-Id and X-Environment-Id headers are required.",
                ct);
            return;
        }

        var requirement = new GatewayPermissionRequirement(
            GatewayPermissions.FilesRead,
            organizationId,
            environmentId,
            "file",
            null);
        var principal = await GatewayAuthorization.RequirePermissionAsync(HttpContext, auth, requirement, ct);
        if (principal is null)
        {
            return;
        }

        try
        {
            var response = await files.ListFilesAsync(
                new ListFilesRequest(
                    organizationId,
                    environmentId,
                    req.FilePurpose,
                    req.UploaderId,
                    req.OwnerId,
                    req.CreatedFromUtc,
                    req.CreatedToUtc,
                    req.Status,
                    req.Skip,
                    req.Take),
                ct);
            await ResponseDataEndpointResults.WriteDataAsync(HttpContext, StatusCodes.Status200OK, response, ct);
        }
        catch (GatewayAuthException ex)
        {
            await ResponseDataEndpointResults.WriteErrorAsync(
                HttpContext,
                (int)ex.StatusCode,
                ex.Reason,
                ct);
        }
    }
}

[Tags("Console Files")]
[HttpGet("/api/console/v1/files/usage")]
[GatewayOperationId("getConsoleFileStorageUsage")]
[Authorize(Policy = GatewayPolicies.ConsoleAuthenticated)]
public sealed class GetConsoleFileStorageUsageEndpoint(
    IGatewayAuthorizationClient auth,
    IGatewayFileStorageClient files)
    : Endpoint<ConsoleFileStorageUsageRequest, ResponseData<FileStorageUsageResponse>>
{
    public override async Task HandleAsync(ConsoleFileStorageUsageRequest req, CancellationToken ct)
    {
        var organizationId = HttpContext.Request.Headers["X-Organization-Id"].ToString();
        var environmentId = HttpContext.Request.Headers["X-Environment-Id"].ToString();
        if (string.IsNullOrWhiteSpace(organizationId) || string.IsNullOrWhiteSpace(environmentId))
        {
            await ResponseDataEndpointResults.WriteErrorAsync(
                HttpContext,
                StatusCodes.Status400BadRequest,
                "X-Organization-Id and X-Environment-Id headers are required.",
                ct);
            return;
        }

        var requirement = new GatewayPermissionRequirement(
            GatewayPermissions.FilesRead,
            organizationId,
            environmentId,
            "file-usage",
            req.FilePurpose);
        var principal = await GatewayAuthorization.RequirePermissionAsync(HttpContext, auth, requirement, ct);
        if (principal is null)
        {
            return;
        }

        try
        {
            var response = await files.GetUsageAsync(
                new FileStorageUsageRequest(organizationId, environmentId, req.FilePurpose),
                ct);
            await ResponseDataEndpointResults.WriteDataAsync(HttpContext, StatusCodes.Status200OK, response, ct);
        }
        catch (GatewayAuthException ex)
        {
            await ResponseDataEndpointResults.WriteErrorAsync(HttpContext, (int)ex.StatusCode, ex.Reason, ct);
        }
    }
}

[Tags("Console Files")]
[HttpGet("/api/console/v1/files/{fileId}")]
[GatewayOperationId("getConsoleFileMetadata")]
[Authorize(Policy = GatewayPolicies.ConsoleAuthenticated)]
public sealed class GetConsoleFileMetadataEndpoint(
    IGatewayIamAuthClient iam,
    IGatewayAuthorizationClient auth,
    IGatewayFileStorageClient files)
    : AuthorizedProxyEndpoint<FileMetadataResponse>(
        iam,
        auth,
        GatewayPermissions.FilesRead)
{
    protected override Task<FileMetadataResponse> ForwardAsync(
        string bearerToken,
        CancellationToken cancellationToken) =>
        files.GetFileMetadataAsync(Route<string>("fileId")!, cancellationToken);
}

[Tags("Console Files")]
[GatewayOperationId("getConsoleTusUploadOffset")]
[Authorize(Policy = GatewayPolicies.ConsoleAuthenticated)]
public sealed class GetConsoleTusUploadOffsetEndpoint(
    IGatewayIamAuthClient iam,
    IGatewayAuthorizationClient auth,
    IGatewayFileStorageClient files)
    : EndpointWithoutRequest
{
    public override void Configure()
    {
        Head("/api/console/v1/files/tus/{uploadSessionId}");
        Policies(GatewayPolicies.ConsoleAuthenticated);
        Options(x => x.WithTags("Console Files"));
    }

    public override Task HandleAsync(CancellationToken ct) =>
        AuthorizedProxyEndpointExecutor.ExecuteAsync(
            HttpContext,
            iam,
            auth,
            GatewayPermissions.FilesUpload,
            async (context, cancellationToken) =>
                await files.ProxyTusHeadAsync(
                    Route<string>("uploadSessionId")!,
                    context.Principal.OrganizationId,
                    context.Principal.EnvironmentId,
                    HttpContext.Response,
                    cancellationToken),
            ct);
}

[Tags("Console Files")]
[HttpPatch("/api/console/v1/files/tus/{uploadSessionId}")]
[GatewayOperationId("patchConsoleTusUpload")]
[Authorize(Policy = GatewayPolicies.ConsoleAuthenticated)]
public sealed class PatchConsoleTusUploadEndpoint(
    IGatewayIamAuthClient iam,
    IGatewayAuthorizationClient auth,
    IGatewayFileStorageClient files)
    : EndpointWithoutRequest
{
    public override Task HandleAsync(CancellationToken ct) =>
        AuthorizedProxyEndpointExecutor.ExecuteAsync(
            HttpContext,
            iam,
            auth,
            GatewayPermissions.FilesUpload,
            async (context, cancellationToken) =>
                await files.ProxyTusPatchAsync(
                    Route<string>("uploadSessionId")!,
                    context.Principal.OrganizationId,
                    context.Principal.EnvironmentId,
                    HttpContext.Request,
                    HttpContext.Response,
                    cancellationToken),
            ct);
}

// ---------------------------------------------------------------------------
// #3314 平台控制台的文件字节面。
//
// 这里**只有一条**字节路由、以 fileId 为入参：download grant 由网关在服务端签发并立即兑换，
// 调用方拿不到 grant id。曾经的 `POST /files/{fileId}/download-grants` +
// `GET /files/download-grants/{downloadGrantId}/content` 两跳形状把 grant id 交了出去，
// 而 FileStorage 的 grant id 是全服务共用命名空间、兑换面既不看用途也不看签发门面
// ——实测该 id 可以在权限口径不同的另一条网关路由上兑换成功（#3314）。
//
// **权限码要求与合并前的两跳一致，不因为合并成一跳而收窄**：旧形状下自助取字节必须先持
// `files.download-grants.create` 签发、再持 `files.read` 兑换。把两跳并成一跳后若只校验
// 其中一个码，只持 `files.read` 的角色就**新获得**了取字节能力——那是一次权限扩张，不是
// 本次修复的内容（#3314 第 1 轮审核 P1）。
//
// **本路由不做用途复核**，因此持这两个码即可读本租户任意用途的文件字节；与 BusinessGateway
// 的按用途分面不同。该缺口由 #3663 以「purpose 结构性归属」承接，不在本路由内解决。
//
// 组织/环境取自 principal，不收调用方入参：字节面没有 JSON 请求体可校验，从头部读等于让
// 调用方自己声明租户范围。
// ---------------------------------------------------------------------------

[Tags("Console Files")]
[HttpGet("/api/console/v1/files/{fileId}/content")]
[GatewayOperationId("downloadConsoleFileContent")]
[Authorize(Policy = GatewayPolicies.ConsoleAuthenticated)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK, "application/octet-stream")]
public sealed class DownloadConsoleFileContentEndpoint(
    IGatewayIamAuthClient iam,
    IGatewayAuthorizationClient auth,
    IGatewayFileStorageClient files)
    : EndpointWithoutRequest
{
    /// <summary>
    /// 合并前两跳各自的门：签发要 <c>files.download-grants.create</c>、兑换要 <c>files.read</c>。
    /// 顺序即校验顺序，缺任一个都 403 且不触达 FileStorage。
    /// </summary>
    public static readonly string[] RequiredPermissionCodes =
    [
        GatewayPermissions.FilesDownloadGrantsCreate,
        GatewayPermissions.FilesRead,
    ];

    public override Task HandleAsync(CancellationToken ct) =>
        AuthorizedProxyEndpointExecutor.ExecuteAsync(
            HttpContext,
            iam,
            auth,
            RequiredPermissionCodes,
            async (context, cancellationToken) =>
                await files.StreamFileContentAsync(
                    Route<string>("fileId")!,
                    context.Principal.OrganizationId,
                    context.Principal.EnvironmentId,
                    HttpContext.Response,
                    cancellationToken),
            ct);
}
