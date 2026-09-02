using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Files;

[Tags("Business Console Files")]
[HttpPost("/api/business-console/v1/files/{fileId}/download-grants")]
[BusinessGatewayOperationId("createBusinessConsoleSopFileDownloadGrant")]
public sealed class CreateBusinessConsoleSopFileDownloadGrantEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessFileStorageClient files,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateSopFileDownloadGrantRequest, BusinessConsoleSopFileDownloadGrantResponse>(
        auth,
        BusinessGatewayPermissions.EngineeringDocumentsRead)
{
    protected override string OrganizationId(BusinessConsoleCreateSopFileDownloadGrantRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateSopFileDownloadGrantRequest request) => request.EnvironmentId;

    protected override string ResourceType(BusinessConsoleCreateSopFileDownloadGrantRequest request) => "engineering-sop-file";

    protected override string? ResourceId(BusinessConsoleCreateSopFileDownloadGrantRequest request) => Route<string>("fileId");

    protected override Task<BusinessConsoleSopFileDownloadGrantResponse> ForwardAsync(
        BusinessConsoleCreateSopFileDownloadGrantRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        files.CreateSopFileDownloadGrantAsync(tokenProvider.BearerToken, Route<string>("fileId")!, request, cancellationToken);
}

public sealed class BusinessConsoleCreateSopFileDownloadGrantRequestValidator : Validator<BusinessConsoleCreateSopFileDownloadGrantRequest>
{
    public BusinessConsoleCreateSopFileDownloadGrantRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
    }
}

[Tags("Business Console Files")]
[HttpGet("/api/business-console/v1/files/download-grants/{downloadGrantId}/content")]
[BusinessGatewayOperationId("downloadBusinessConsoleSopFileContent")]
[Authorize(Policy = BusinessGatewayPolicies.BusinessConsoleAuthenticated)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK, "application/octet-stream")]
public sealed class DownloadBusinessConsoleSopFileContentEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessFileStorageClient files,
    IInternalServiceTokenProvider tokenProvider)
    : EndpointWithoutRequest
{
    private const string OrganizationHeader = "X-Organization-Id";
    private const string EnvironmentHeader = "X-Environment-Id";

    public override async Task HandleAsync(CancellationToken ct)
    {
        var organizationId = FirstHeaderOrQuery(OrganizationHeader, "organizationId");
        var environmentId = FirstHeaderOrQuery(EnvironmentHeader, "environmentId");
        if (string.IsNullOrWhiteSpace(organizationId) || string.IsNullOrWhiteSpace(environmentId))
        {
            await ResponseDataEndpointResults.WriteErrorAsync(HttpContext, StatusCodes.Status400BadRequest, "Download grant headers are required.", ct);
            return;
        }

        var downloadGrantId = Route<string>("downloadGrantId")!;
        var bearerToken = await BusinessGatewayAuthorization.RequirePermissionAsync(
            HttpContext,
            auth,
            new BusinessGatewayPermissionRequirement(
                BusinessGatewayPermissions.EngineeringDocumentsRead,
                organizationId,
                environmentId,
                "engineering-sop-download-grant",
                downloadGrantId),
            ct);
        if (bearerToken is null)
        {
            return;
        }

        try
        {
            var response = await files.DownloadSopFileContentAsync(
                tokenProvider.BearerToken,
                downloadGrantId,
                new Dictionary<string, string>
                {
                    [OrganizationHeader] = organizationId,
                    [EnvironmentHeader] = environmentId,
                },
                ct);
            HttpContext.Response.ContentType = response.ContentType;
            if (response.ContentLength is not null)
            {
                HttpContext.Response.ContentLength = response.ContentLength.Value;
            }

            await HttpContext.Response.Body.WriteAsync(response.Content, ct);
        }
        catch (BusinessServiceProxyException ex)
        {
            await ResponseDataEndpointResults.WriteErrorAsync(HttpContext, ex, ct);
        }
    }

    private string? FirstHeaderOrQuery(string headerName, string queryName)
    {
        var header = HttpContext.Request.Headers[headerName].ToString();
        return string.IsNullOrWhiteSpace(header)
            ? HttpContext.Request.Query[queryName].ToString()
            : header;
    }
}

// ---------------------------------------------------------------------------
// #3085 交接班附件门面。
//
// 交接班照片和工程 SOP 文件共用 FileStorage，但不共用权限口径：上传归
// business.mes.handovers.manage，下载归 business.mes.handovers.read，两者都不授予
// business.engineering.documents.read 的 SOP 面，反之亦然。用途（shift-handover-photo）、
// owner 和 tus 代理路径由门面固定，调用方拿不到 FileStorage 内部 URL（ADR 0023 决策 1.3）。
// ---------------------------------------------------------------------------

[Tags("Business Console Files")]
[HttpPost("/api/business-console/v1/files/shift-handover-attachments/upload-sessions")]
[BusinessGatewayOperationId("createBusinessConsoleShiftHandoverAttachmentUploadSession")]
public sealed class CreateBusinessConsoleShiftHandoverAttachmentUploadSessionEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessFileStorageClient files,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<
        BusinessConsoleCreateShiftHandoverAttachmentUploadSessionRequest,
        BusinessConsoleShiftHandoverAttachmentUploadSessionResponse>(
        auth,
        BusinessGatewayPermissions.MesHandoversManage)
{
    protected override string OrganizationId(BusinessConsoleCreateShiftHandoverAttachmentUploadSessionRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateShiftHandoverAttachmentUploadSessionRequest request) => request.EnvironmentId;

    protected override string ResourceType(BusinessConsoleCreateShiftHandoverAttachmentUploadSessionRequest request) => "mes-shift-handover-attachment";

    protected override Task<BusinessConsoleShiftHandoverAttachmentUploadSessionResponse> ForwardAsync(
        BusinessConsoleCreateShiftHandoverAttachmentUploadSessionRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        // owner 绑定到认证 principal：附件在交接单建立之前上传，此刻唯一稳定的归属事实就是上传者本人。
        files.CreateShiftHandoverAttachmentUploadSessionAsync(
            tokenProvider.BearerToken,
            RequireAuthorizedPrincipalId(),
            request,
            cancellationToken);
}

public sealed class BusinessConsoleCreateShiftHandoverAttachmentUploadSessionRequestValidator
    : Validator<BusinessConsoleCreateShiftHandoverAttachmentUploadSessionRequest>
{
    public BusinessConsoleCreateShiftHandoverAttachmentUploadSessionRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(512);
        RuleFor(x => x.ContentType).NotEmpty().MaximumLength(256);
        RuleFor(x => x.ExpectedSizeBytes).GreaterThan(0);
    }
}

[Tags("Business Console Files")]
[HttpPost("/api/business-console/v1/files/shift-handover-attachments/upload-sessions/{uploadSessionId}/complete")]
[BusinessGatewayOperationId("completeBusinessConsoleShiftHandoverAttachmentUpload")]
public sealed class CompleteBusinessConsoleShiftHandoverAttachmentUploadEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessFileStorageClient files,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<
        BusinessConsoleCompleteShiftHandoverAttachmentUploadRequest,
        BusinessConsoleMesShiftHandoverAttachment>(
        auth,
        BusinessGatewayPermissions.MesHandoversManage)
{
    protected override string OrganizationId(BusinessConsoleCompleteShiftHandoverAttachmentUploadRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCompleteShiftHandoverAttachmentUploadRequest request) => request.EnvironmentId;

    protected override string ResourceType(BusinessConsoleCompleteShiftHandoverAttachmentUploadRequest request) => "mes-shift-handover-attachment";

    protected override string? ResourceId(BusinessConsoleCompleteShiftHandoverAttachmentUploadRequest request) => Route<string>("uploadSessionId");

    protected override Task<BusinessConsoleMesShiftHandoverAttachment> ForwardAsync(
        BusinessConsoleCompleteShiftHandoverAttachmentUploadRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        files.CompleteShiftHandoverAttachmentUploadAsync(
            tokenProvider.BearerToken,
            Route<string>("uploadSessionId")!,
            request,
            cancellationToken);
}

public sealed class BusinessConsoleCompleteShiftHandoverAttachmentUploadRequestValidator
    : Validator<BusinessConsoleCompleteShiftHandoverAttachmentUploadRequest>
{
    public BusinessConsoleCompleteShiftHandoverAttachmentUploadRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
    }
}

[Tags("Business Console Files")]
[BusinessGatewayOperationId("getBusinessConsoleShiftHandoverAttachmentTusOffset")]
[Authorize(Policy = BusinessGatewayPolicies.BusinessConsoleAuthenticated)]
public sealed class GetBusinessConsoleShiftHandoverAttachmentTusOffsetEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessFileStorageClient files,
    IInternalServiceTokenProvider tokenProvider)
    : EndpointWithoutRequest
{
    public override void Configure()
    {
        Head("/api/business-console/v1/files/shift-handover-attachments/tus/{uploadSessionId}");
        Policies(BusinessGatewayPolicies.BusinessConsoleAuthenticated);
        Options(x => x.WithTags("Business Console Files"));
    }

    public override Task HandleAsync(CancellationToken ct) =>
        BusinessConsoleShiftHandoverAttachmentTransfer.ProxyAsync(
            HttpContext,
            auth,
            BusinessGatewayPermissions.MesHandoversManage,
            "mes-shift-handover-attachment-upload",
            Route<string>("uploadSessionId")!,
            (organizationId, environmentId, cancellationToken) => files.ProxyShiftHandoverAttachmentTusAsync(
                tokenProvider.BearerToken,
                Route<string>("uploadSessionId")!,
                null,
                HttpContext.Response,
                cancellationToken),
            ct);
}

[Tags("Business Console Files")]
[HttpPatch("/api/business-console/v1/files/shift-handover-attachments/tus/{uploadSessionId}")]
[BusinessGatewayOperationId("patchBusinessConsoleShiftHandoverAttachmentTusUpload")]
[Authorize(Policy = BusinessGatewayPolicies.BusinessConsoleAuthenticated)]
public sealed class PatchBusinessConsoleShiftHandoverAttachmentTusUploadEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessFileStorageClient files,
    IInternalServiceTokenProvider tokenProvider)
    : EndpointWithoutRequest
{
    public override Task HandleAsync(CancellationToken ct) =>
        BusinessConsoleShiftHandoverAttachmentTransfer.ProxyAsync(
            HttpContext,
            auth,
            BusinessGatewayPermissions.MesHandoversManage,
            "mes-shift-handover-attachment-upload",
            Route<string>("uploadSessionId")!,
            (organizationId, environmentId, cancellationToken) => files.ProxyShiftHandoverAttachmentTusAsync(
                tokenProvider.BearerToken,
                Route<string>("uploadSessionId")!,
                HttpContext.Request,
                HttpContext.Response,
                cancellationToken),
            ct);
}

[Tags("Business Console Files")]
[HttpPost("/api/business-console/v1/files/shift-handover-attachments/{fileId}/download-grants")]
[BusinessGatewayOperationId("createBusinessConsoleShiftHandoverAttachmentDownloadGrant")]
public sealed class CreateBusinessConsoleShiftHandoverAttachmentDownloadGrantEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessFileStorageClient files,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<
        BusinessConsoleCreateShiftHandoverAttachmentDownloadGrantRequest,
        BusinessConsoleShiftHandoverAttachmentDownloadGrantResponse>(
        auth,
        BusinessGatewayPermissions.MesHandoversRead)
{
    protected override string OrganizationId(BusinessConsoleCreateShiftHandoverAttachmentDownloadGrantRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateShiftHandoverAttachmentDownloadGrantRequest request) => request.EnvironmentId;

    protected override string ResourceType(BusinessConsoleCreateShiftHandoverAttachmentDownloadGrantRequest request) => "mes-shift-handover-attachment";

    protected override string? ResourceId(BusinessConsoleCreateShiftHandoverAttachmentDownloadGrantRequest request) => Route<string>("fileId");

    protected override Task<BusinessConsoleShiftHandoverAttachmentDownloadGrantResponse> ForwardAsync(
        BusinessConsoleCreateShiftHandoverAttachmentDownloadGrantRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        files.CreateShiftHandoverAttachmentDownloadGrantAsync(
            tokenProvider.BearerToken,
            Route<string>("fileId")!,
            request,
            cancellationToken);
}

public sealed class BusinessConsoleCreateShiftHandoverAttachmentDownloadGrantRequestValidator
    : Validator<BusinessConsoleCreateShiftHandoverAttachmentDownloadGrantRequest>
{
    public BusinessConsoleCreateShiftHandoverAttachmentDownloadGrantRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
    }
}

[Tags("Business Console Files")]
[HttpGet("/api/business-console/v1/files/shift-handover-attachments/download-grants/{downloadGrantId}/content")]
[BusinessGatewayOperationId("downloadBusinessConsoleShiftHandoverAttachmentContent")]
[Authorize(Policy = BusinessGatewayPolicies.BusinessConsoleAuthenticated)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK, "application/octet-stream")]
public sealed class DownloadBusinessConsoleShiftHandoverAttachmentContentEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessFileStorageClient files,
    IInternalServiceTokenProvider tokenProvider)
    : EndpointWithoutRequest
{
    public override Task HandleAsync(CancellationToken ct) =>
        BusinessConsoleShiftHandoverAttachmentTransfer.ProxyAsync(
            HttpContext,
            auth,
            BusinessGatewayPermissions.MesHandoversRead,
            "mes-shift-handover-attachment-download-grant",
            Route<string>("downloadGrantId")!,
            (organizationId, environmentId, cancellationToken) => files.ProxyShiftHandoverAttachmentContentAsync(
                tokenProvider.BearerToken,
                Route<string>("downloadGrantId")!,
                organizationId,
                environmentId,
                HttpContext.Response,
                cancellationToken),
            ct);
}

/// <summary>
/// tus HEAD/PATCH 与下载 content 三条字节路径共用的门：它们都不是 JSON 门面，不能走
/// <see cref="AuthorizedBusinessProxyEndpoint{TRequest,TResponse}"/>，组织/环境只能从 header 取
/// （tus 客户端可以带自定义 header，但不方便带请求体）。
/// </summary>
internal static class BusinessConsoleShiftHandoverAttachmentTransfer
{
    private const string OrganizationHeader = "X-Organization-Id";
    private const string EnvironmentHeader = "X-Environment-Id";

    public static async Task ProxyAsync(
        HttpContext httpContext,
        IBusinessGatewayAuthorizationClient auth,
        string permissionCode,
        string resourceType,
        string resourceId,
        Func<string, string, CancellationToken, Task> proxyAsync,
        CancellationToken ct)
    {
        var organizationId = FirstHeaderOrQuery(httpContext, OrganizationHeader, "organizationId");
        var environmentId = FirstHeaderOrQuery(httpContext, EnvironmentHeader, "environmentId");
        if (string.IsNullOrWhiteSpace(organizationId) || string.IsNullOrWhiteSpace(environmentId))
        {
            await ResponseDataEndpointResults.WriteErrorAsync(
                httpContext,
                StatusCodes.Status400BadRequest,
                "Shift handover attachment transfer headers are required.",
                ct);
            return;
        }

        var bearerToken = await BusinessGatewayAuthorization.RequirePermissionAsync(
            httpContext,
            auth,
            new BusinessGatewayPermissionRequirement(
                permissionCode,
                organizationId,
                environmentId,
                resourceType,
                resourceId),
            ct);
        if (bearerToken is null)
        {
            return;
        }

        try
        {
            await proxyAsync(organizationId, environmentId, ct);
        }
        catch (BusinessServiceProxyException ex)
        {
            await ResponseDataEndpointResults.WriteErrorAsync(httpContext, ex, ct);
        }
    }

    private static string? FirstHeaderOrQuery(HttpContext httpContext, string headerName, string queryName)
    {
        var header = httpContext.Request.Headers[headerName].ToString();
        return string.IsNullOrWhiteSpace(header)
            ? httpContext.Request.Query[queryName].ToString()
            : header;
    }
}
