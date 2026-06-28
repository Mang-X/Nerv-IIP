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
    DateTimeOffset? CreatedFromUtc,
    DateTimeOffset? CreatedToUtc,
    string? Status,
    int? Skip = null,
    int? Take = null);

[Tags("Console Files")]
[HttpPost("/api/console/v1/files/upload-sessions")]
[GatewayOperationId("createConsoleFileUploadSession")]
[Authorize(Policy = GatewayPolicies.ConsoleAuthenticated)]
public sealed class CreateConsoleFileUploadSessionEndpoint(
    IGatewayIamAuthClient iam,
    IGatewayAuthorizationClient auth,
    IGatewayFileStorageClient files)
    : AuthorizedProxyEndpoint<CreateUploadSessionRequest, CreateUploadSessionResponse>(
        iam,
        auth,
        GatewayPermissions.FilesUpload)
{
    protected override Task<CreateUploadSessionResponse> ForwardAsync(
        string bearerToken,
        CreateUploadSessionRequest request,
        CancellationToken cancellationToken) =>
        files.CreateUploadSessionAsync(request, cancellationToken);
}

[Tags("Console Files")]
[HttpPost("/api/console/v1/files/upload-sessions/{uploadSessionId}/complete")]
[GatewayOperationId("completeConsoleFileUploadSession")]
[Authorize(Policy = GatewayPolicies.ConsoleAuthenticated)]
public sealed class CompleteConsoleFileUploadSessionEndpoint(
    IGatewayIamAuthClient iam,
    IGatewayAuthorizationClient auth,
    IGatewayFileStorageClient files)
    : AuthorizedProxyEndpoint<CompleteUploadSessionRequest, FileMetadataResponse>(
        iam,
        auth,
        GatewayPermissions.FilesUpload)
{
    protected override Task<FileMetadataResponse> ForwardAsync(
        string bearerToken,
        CompleteUploadSessionRequest request,
        CancellationToken cancellationToken) =>
        files.CompleteUploadSessionAsync(Route<string>("uploadSessionId")!, request, cancellationToken);
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
[HttpPost("/api/console/v1/files/{fileId}/download-grants")]
[GatewayOperationId("createConsoleFileDownloadGrant")]
[Authorize(Policy = GatewayPolicies.ConsoleAuthenticated)]
public sealed class CreateConsoleFileDownloadGrantEndpoint(
    IGatewayIamAuthClient iam,
    IGatewayAuthorizationClient auth,
    IGatewayFileStorageClient files)
    : AuthorizedProxyEndpoint<CreateDownloadGrantRequest, DownloadGrantResponse>(
        iam,
        auth,
        GatewayPermissions.FilesDownloadGrantsCreate)
{
    protected override Task<DownloadGrantResponse> ForwardAsync(
        string bearerToken,
        CreateDownloadGrantRequest request,
        CancellationToken cancellationToken) =>
        files.CreateDownloadGrantAsync(Route<string>("fileId")!, request, cancellationToken);
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
            async (_, cancellationToken) =>
                await files.ProxyTusHeadAsync(Route<string>("uploadSessionId")!, HttpContext.Response, cancellationToken),
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
            async (_, cancellationToken) =>
                await files.ProxyTusPatchAsync(
                    Route<string>("uploadSessionId")!,
                    HttpContext.Request,
                    HttpContext.Response,
                    cancellationToken),
            ct);
}

[Tags("Console Files")]
[HttpGet("/api/console/v1/files/download-grants/{downloadGrantId}/content")]
[GatewayOperationId("downloadConsoleFileGrantContent")]
[Authorize(Policy = GatewayPolicies.ConsoleAuthenticated)]
public sealed class DownloadConsoleFileGrantContentEndpoint(
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
            GatewayPermissions.FilesRead,
            async (_, cancellationToken) =>
                await files.ProxyDownloadGrantContentAsync(
                    Route<string>("downloadGrantId")!,
                    HttpContext.Request,
                    HttpContext.Response,
                    cancellationToken),
            ct);
}
