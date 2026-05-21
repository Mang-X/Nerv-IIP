using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.FileStorage.Web.Application.Files;
using Nerv.IIP.FileStorage.Web.Application.Files.Tus;

namespace Nerv.IIP.FileStorage.Web.Endpoints.Files;

public sealed class GetTusUploadOffsetEndpoint(IFileStorageService files, ILocalTusFileStoreAccessor storeAccessor)
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
        if (!CanAcceptTusUpload(files, uploadSessionId) || !storeAccessor.TryGet(out var store))
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

    internal static bool CanAcceptTusUpload(IFileStorageService files, string uploadSessionId)
    {
        return files is ILocalTusUploadSessionIndex index && index.CanAcceptTusUpload(uploadSessionId);
    }
}

[Tags("Files")]
[HttpPatch("/api/files/v1/tus/{uploadSessionId}")]
[AllowAnonymous]
public sealed class PatchTusUploadEndpoint(IFileStorageService files, ILocalTusFileStoreAccessor storeAccessor)
    : EndpointWithoutRequest
{
    private const string TusVersion = "1.0.0";
    private const string OffsetOctetStreamContentType = "application/offset+octet-stream";

    public override async Task HandleAsync(CancellationToken ct)
    {
        var uploadSessionId = Route<string>("uploadSessionId")!;
        if (!GetTusUploadOffsetEndpoint.CanAcceptTusUpload(files, uploadSessionId)
            || !storeAccessor.TryGet(out var store))
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        if (!HasTusVersion(HttpContext.Request))
        {
            HttpContext.Response.Headers["Tus-Resumable"] = TusVersion;
            await SendStatusAsync(HttpContext.Response, StatusCodes.Status412PreconditionFailed, ct);
            return;
        }

        if (!HasOffsetOctetStreamContentType(HttpContext.Request))
        {
            HttpContext.Response.Headers["Tus-Resumable"] = TusVersion;
            await SendStatusAsync(HttpContext.Response, StatusCodes.Status415UnsupportedMediaType, ct);
            return;
        }

        if (!TryReadUploadOffset(HttpContext.Request, out var expectedOffset))
        {
            await SendStatusAsync(HttpContext.Response, StatusCodes.Status400BadRequest, ct);
            return;
        }

        var currentOffset = store.GetOffset(uploadSessionId);
        if (currentOffset != expectedOffset)
        {
            GetTusUploadOffsetEndpoint.SetTusHeaders(HttpContext.Response, currentOffset);
            await SendStatusAsync(HttpContext.Response, StatusCodes.Status409Conflict, ct);
            return;
        }

        var newOffset = await store.AppendAsync(uploadSessionId, expectedOffset, HttpContext.Request.Body, ct);
        HttpContext.Response.StatusCode = StatusCodes.Status204NoContent;
        GetTusUploadOffsetEndpoint.SetTusHeaders(HttpContext.Response, newOffset);
    }

    private static bool HasTusVersion(HttpRequest request)
    {
        return request.Headers.TryGetValue("Tus-Resumable", out var values)
            && string.Equals(values.FirstOrDefault(), TusVersion, StringComparison.Ordinal);
    }

    private static bool HasOffsetOctetStreamContentType(HttpRequest request)
    {
        var contentType = request.ContentType?.Split(';', 2)[0].Trim();
        return string.Equals(contentType, OffsetOctetStreamContentType, StringComparison.OrdinalIgnoreCase);
    }

    private static Task SendStatusAsync(HttpResponse response, int statusCode, CancellationToken ct)
    {
        return response.SendStatusCodeAsync(statusCode, ct);
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
public sealed class DownloadGrantContentEndpoint(IFileStorageService files, ILocalTusFileStoreAccessor storeAccessor)
    : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        if (files is not ILocalFileContentIndex index
            || !index.TryGetUploadSessionIdForDownloadGrant(Route<string>("downloadGrantId")!, out var uploadSessionId)
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
