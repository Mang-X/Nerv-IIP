using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.Contracts.FileStorage;
using Nerv.IIP.PlatformGateway.Web.Application.Auth;
using Nerv.IIP.PlatformGateway.Web.Application.FileStorage;
using Nerv.IIP.PlatformGateway.Web.Application.OpenApi;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.PlatformGateway.Web.Endpoints.Files;

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
                    HttpContext.Response,
                    cancellationToken),
            ct);
}
