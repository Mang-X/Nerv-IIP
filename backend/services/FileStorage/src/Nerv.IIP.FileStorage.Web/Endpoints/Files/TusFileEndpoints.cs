using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.FileStorage.Web.Application.Files;
using Nerv.IIP.FileStorage.Web.Application.Files.Tus;

namespace Nerv.IIP.FileStorage.Web.Endpoints.Files;

public sealed class GetTusUploadOffsetEndpoint(IFileStorageService files, LocalTusFileStore store)
    : EndpointWithoutRequest
{
    public override void Configure()
    {
        Head("/api/files/v1/tus/{uploadSessionId}");
        AllowAnonymous();
        Options(x => x.WithTags("Files"));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var uploadSessionId = Route<string>("uploadSessionId")!;
        if (!UploadSessionExists(files, uploadSessionId))
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var offset = store.GetOffset(uploadSessionId);
        SetTusHeaders(HttpContext.Response, offset);
    }

    internal static void SetTusHeaders(HttpResponse response, long offset)
    {
        response.Headers["Tus-Resumable"] = "1.0.0";
        response.Headers["Upload-Offset"] = offset.ToString(System.Globalization.CultureInfo.InvariantCulture);
        response.Headers.CacheControl = "no-store";
    }

    internal static bool UploadSessionExists(IFileStorageService files, string uploadSessionId)
    {
        return files is ILocalTusUploadSessionIndex index && index.UploadSessionExists(uploadSessionId);
    }
}

[Tags("Files")]
[HttpPatch("/api/files/v1/tus/{uploadSessionId}")]
[AllowAnonymous]
public sealed class PatchTusUploadEndpoint(IFileStorageService files, LocalTusFileStore store)
    : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        var uploadSessionId = Route<string>("uploadSessionId")!;
        if (!GetTusUploadOffsetEndpoint.UploadSessionExists(files, uploadSessionId))
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        if (!TryReadUploadOffset(HttpContext.Request, out var expectedOffset))
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var currentOffset = store.GetOffset(uploadSessionId);
        if (currentOffset != expectedOffset)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status409Conflict;
            GetTusUploadOffsetEndpoint.SetTusHeaders(HttpContext.Response, currentOffset);
            return;
        }

        var newOffset = await store.AppendAsync(uploadSessionId, expectedOffset, HttpContext.Request.Body, ct);
        HttpContext.Response.StatusCode = StatusCodes.Status204NoContent;
        GetTusUploadOffsetEndpoint.SetTusHeaders(HttpContext.Response, newOffset);
    }

    private static bool TryReadUploadOffset(HttpRequest request, out long offset)
    {
        offset = 0;
        if (!request.Headers.TryGetValue("Upload-Offset", out var values))
        {
            return false;
        }

        return long.TryParse(values.FirstOrDefault(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out offset)
            && offset >= 0;
    }
}

[Tags("Files")]
[HttpGet("/api/files/v1/download-grants/{downloadGrantId}/content")]
[AllowAnonymous]
public sealed class DownloadGrantContentEndpoint(IFileStorageService files, LocalTusFileStore store)
    : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        if (files is not ILocalFileContentIndex index
            || !index.TryGetUploadSessionIdForDownloadGrant(Route<string>("downloadGrantId")!, out var uploadSessionId))
        {
            HttpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await using var stream = store.OpenRead(uploadSessionId);
        HttpContext.Response.ContentType = "application/octet-stream";
        HttpContext.Response.ContentLength = stream.Length;
        await stream.CopyToAsync(HttpContext.Response.Body, ct);
    }
}
