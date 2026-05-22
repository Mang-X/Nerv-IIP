using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.Contracts.FileStorage;
using Nerv.IIP.FileStorage.Web.Application.Files;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.FileStorage.Web.Endpoints.Files;

[Tags("Files")]
[HttpPost("/api/files/v1/upload-sessions")]
[Authorize(Policy = InternalServiceAuthorizationPolicy.Name)]
public sealed class CreateUploadSessionEndpoint(IFileStorageService files)
    : Endpoint<CreateUploadSessionRequest, CreateUploadSessionResponse>
{
    public override async Task HandleAsync(CreateUploadSessionRequest req, CancellationToken ct)
    {
        await this.SendResultAsync(files.CreateUploadSession(req), ct);
    }
}

[Tags("Files")]
[HttpPost("/api/files/v1/upload-sessions/{uploadSessionId}/complete")]
[Authorize(Policy = InternalServiceAuthorizationPolicy.Name)]
public sealed class CompleteUploadSessionEndpoint(IFileStorageService files)
    : Endpoint<CompleteUploadSessionRequest, FileMetadataResponse>
{
    public override async Task HandleAsync(CompleteUploadSessionRequest req, CancellationToken ct)
    {
        await this.SendResultAsync(files.CompleteUploadSession(Route<string>("uploadSessionId")!, req), ct);
    }
}

[Tags("Files")]
[HttpGet("/api/files/v1/files/{fileId}")]
[Authorize(Policy = InternalServiceAuthorizationPolicy.Name)]
public sealed class GetFileMetadataEndpoint(IFileStorageService files)
    : EndpointWithoutRequest<FileMetadataResponse>
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        await this.SendResultAsync(files.GetFileMetadata(Route<string>("fileId")!), ct);
    }
}

[Tags("Files")]
[HttpPost("/api/files/v1/files/{fileId}/download-grants")]
[Authorize(Policy = InternalServiceAuthorizationPolicy.Name)]
public sealed class CreateDownloadGrantEndpoint(IFileStorageService files)
    : Endpoint<CreateDownloadGrantRequest, DownloadGrantResponse>
{
    public override async Task HandleAsync(CreateDownloadGrantRequest req, CancellationToken ct)
    {
        await this.SendResultAsync(files.CreateDownloadGrant(Route<string>("fileId")!, req), ct);
    }
}

internal static class FileStorageEndpointResults
{
    public static async Task SendResultAsync<T>(this IEndpoint endpoint, FileStorageResult<T> result, CancellationToken ct)
    {
        if (result.Value is not null)
        {
            await endpoint.HttpContext.Response.WriteAsJsonAsync(result.Value, ct);
            return;
        }

        endpoint.HttpContext.Response.StatusCode = result.StatusCode;
        await endpoint.HttpContext.Response.WriteAsJsonAsync(result.Error, ct);
    }
}
