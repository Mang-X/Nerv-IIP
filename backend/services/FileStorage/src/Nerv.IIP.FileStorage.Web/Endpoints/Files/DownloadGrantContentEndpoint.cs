using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.FileStorage.Web.Application.Files;
using Nerv.IIP.FileStorage.Web.Application.Files.Tus;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.FileStorage.Web.Endpoints.Files;

[Tags("Files")]
[HttpGet("/api/files/v1/download-grants/{downloadGrantId}/content")]
[Authorize(Policy = InternalServiceAuthorizationPolicy.Name)]
public sealed class DownloadGrantContentEndpoint(IFileStorageService files, ILocalTusFileStoreAccessor storeAccessor)
    : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        var organizationId = HttpContext.Request.Headers[FileStorageTransferHeaders.OrganizationId].ToString();
        var environmentId = HttpContext.Request.Headers[FileStorageTransferHeaders.EnvironmentId].ToString();
        var uploadSessionId = files is ILocalFileContentIndex index
                && !string.IsNullOrWhiteSpace(organizationId)
                && !string.IsNullOrWhiteSpace(environmentId)
            ? await index.GetUploadSessionIdForDownloadGrantAsync(
                Route<string>("downloadGrantId")!,
                organizationId,
                environmentId,
                ct)
            : null;

        if (uploadSessionId is null
            || !storeAccessor.TryGet(out var store)
            || !store.Exists(uploadSessionId))
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await using var stream = store.OpenRead(uploadSessionId);
        HttpContext.Response.ContentType = "application/octet-stream";
        HttpContext.Response.ContentLength = stream.Length;
        await stream.CopyToAsync(HttpContext.Response.Body, ct);
    }
}
