using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.FileStorage.Domain;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.FileStorage.Web.Endpoints.Boundaries;

public sealed record FileStorageBoundaryResponse(IReadOnlyList<string> DomainFacts, IReadOnlyList<string> ProviderBoundaries);

[HttpGet("/internal/file-storage/v1/boundaries")]
[Authorize(Policy = InternalServiceAuthorizationPolicy.Name)]
public sealed class GetFileStorageBoundariesEndpoint : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        await HttpContext.Response.WriteAsJsonAsync(new FileStorageBoundaryResponse(
            ["FileMetadata", "UploadSession", "UploadInstruction", "DownloadGrant", "FilePurposePolicy", "scanStatus"],
            ["UploadProvider", "tus", "s3-multipart", "server-proxy", "ObjectStorageAdapter", "MinIO"]), ct);
    }
}

[HttpGet("/internal/file-storage/v1/purposes/{purpose}")]
[Authorize(Policy = InternalServiceAuthorizationPolicy.Name)]
public sealed class GetFilePurposeEndpoint : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        var purpose = Route<string>("purpose")!;
        await HttpContext.Response.WriteAsJsonAsync(new { purpose, allowed = FilePurposePolicy.IsAllowed(purpose) }, ct);
    }
}
