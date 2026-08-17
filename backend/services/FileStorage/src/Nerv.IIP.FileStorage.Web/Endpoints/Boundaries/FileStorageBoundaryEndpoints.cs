using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.FileStorage.Web.Application.Files;
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

public sealed record FilePurposeBoundaryResponse(
    string Purpose,
    bool Allowed,
    string? ErrorCode,
    string? Message);

[HttpGet("/internal/file-storage/v1/purposes/{purpose}")]
[Authorize(Policy = InternalServiceAuthorizationPolicy.Name)]
public sealed class GetFilePurposeEndpoint(IConfiguration configuration) : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        var purpose = Route<string>("purpose")!;
        var registration = FileStoragePurposePolicies.ResolveRegistration(purpose, configuration);
        await HttpContext.Response.WriteAsJsonAsync(
            new FilePurposeBoundaryResponse(
                registration.Purpose,
                registration.IsRegistered,
                registration.ErrorCode,
                registration.Message),
            ct);
    }
}
