using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.FileStorage.Web.Application.Files;
using Nerv.IIP.FileStorage.Web.Application.Files.Tus;
using Nerv.IIP.ServiceAuth;
using System.Globalization;
using System.Security.Cryptography;

namespace Nerv.IIP.FileStorage.Web.Endpoints.Files;

public sealed class GetTusUploadOffsetEndpoint(IFileStorageService files, ILocalTusFileStoreAccessor storeAccessor)
    : EndpointWithoutRequest
{
    public override void Configure()
    {
        Head("/api/files/v1/tus/{uploadSessionId}");
        Policies(InternalServiceAuthorizationPolicy.Name);
        Options(x => x.WithTags("Files"));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var uploadSessionId = Route<string>("uploadSessionId")!;
        if (!storeAccessor.TryGet(out var store)
            || await GetTusUploadSessionAsync(files, uploadSessionId, ct) is not { } session)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        if (IsExpired(session))
        {
            store.Delete(uploadSessionId);
            await Send.NotFoundAsync(ct);
            return;
        }

        var offset = store.GetOffset(uploadSessionId);
        SetTusHeaders(HttpContext.Response, offset, session);
    }

    internal static void SetTusHeaders(HttpResponse response, long offset, LocalTusUploadSession? session = null)
    {
        response.Headers["Tus-Resumable"] = "1.0.0";
        response.Headers["Upload-Offset"] = offset.ToString(CultureInfo.InvariantCulture);
        if (session is not null)
        {
            response.Headers["Upload-Length"] = session.ExpectedSizeBytes.ToString(CultureInfo.InvariantCulture);
            response.Headers["Upload-Expires"] = session.ExpiresAtUtc.ToString("R", CultureInfo.InvariantCulture);
        }

        response.Headers.CacheControl = "no-store";
    }

    internal static Task<bool> CanAcceptTusUploadAsync(
        IFileStorageService files,
        string uploadSessionId,
        CancellationToken cancellationToken)
    {
        return files is ILocalTusUploadSessionIndex index
            ? index.CanAcceptTusUploadAsync(uploadSessionId, cancellationToken)
            : Task.FromResult(false);
    }

    internal static Task<LocalTusUploadSession?> GetTusUploadSessionAsync(
        IFileStorageService files,
        string uploadSessionId,
        CancellationToken cancellationToken)
    {
        return files is ILocalTusUploadSessionIndex index
            ? index.GetTusUploadSessionAsync(uploadSessionId, cancellationToken)
            : Task.FromResult<LocalTusUploadSession?>(null);
    }

    internal static bool IsExpired(LocalTusUploadSession session)
    {
        return session.ExpiresAtUtc <= DateTimeOffset.UtcNow;
    }
}

[Tags("Files")]
[HttpPatch("/api/files/v1/tus/{uploadSessionId}")]
[Authorize(Policy = InternalServiceAuthorizationPolicy.Name)]
public sealed class PatchTusUploadEndpoint(IFileStorageService files, ILocalTusFileStoreAccessor storeAccessor)
    : EndpointWithoutRequest
{
    private const string TusVersion = "1.0.0";
    private const string OffsetOctetStreamContentType = "application/offset+octet-stream";

    public override async Task HandleAsync(CancellationToken ct)
    {
        var uploadSessionId = Route<string>("uploadSessionId")!;
        if (!storeAccessor.TryGet(out var store)
            || await GetTusUploadOffsetEndpoint.GetTusUploadSessionAsync(files, uploadSessionId, ct) is not { } session)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        if (GetTusUploadOffsetEndpoint.IsExpired(session))
        {
            store.Delete(uploadSessionId);
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
            GetTusUploadOffsetEndpoint.SetTusHeaders(HttpContext.Response, currentOffset, session);
            await SendStatusAsync(HttpContext.Response, StatusCodes.Status409Conflict, ct);
            return;
        }

        if (HttpContext.Request.ContentLength is { } contentLength
            && expectedOffset + contentLength > session.ExpectedSizeBytes)
        {
            GetTusUploadOffsetEndpoint.SetTusHeaders(HttpContext.Response, currentOffset, session);
            await SendStatusAsync(HttpContext.Response, StatusCodes.Status413PayloadTooLarge, ct);
            return;
        }

        await using var body = new MemoryStream();
        await HttpContext.Request.Body.CopyToAsync(body, ct);
        if (expectedOffset + body.Length > session.ExpectedSizeBytes)
        {
            GetTusUploadOffsetEndpoint.SetTusHeaders(HttpContext.Response, currentOffset, session);
            await SendStatusAsync(HttpContext.Response, StatusCodes.Status413PayloadTooLarge, ct);
            return;
        }

        if (!IsUploadChecksumValid(HttpContext.Request, body.ToArray()))
        {
            GetTusUploadOffsetEndpoint.SetTusHeaders(HttpContext.Response, currentOffset, session);
            await SendStatusAsync(HttpContext.Response, 460, ct);
            return;
        }

        body.Position = 0;
        var newOffset = await store.AppendAsync(uploadSessionId, expectedOffset, body, ct);
        HttpContext.Response.StatusCode = StatusCodes.Status204NoContent;
        GetTusUploadOffsetEndpoint.SetTusHeaders(HttpContext.Response, newOffset, session);
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

        return long.TryParse(values.FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out offset)
            && offset >= 0;
    }

    private static bool IsUploadChecksumValid(HttpRequest request, byte[] bytes)
    {
        if (!request.Headers.TryGetValue("Upload-Checksum", out var values))
        {
            return true;
        }

        var parts = values.FirstOrDefault()?.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts is not { Length: 2 }
            || !string.Equals(parts[0], "sha256", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.Equals(parts[1], Convert.ToBase64String(SHA256.HashData(bytes)), StringComparison.Ordinal);
    }
}

[Tags("Files")]
[HttpGet("/api/files/v1/download-grants/{downloadGrantId}/content")]
[Authorize(Policy = InternalServiceAuthorizationPolicy.Name)]
public sealed class DownloadGrantContentEndpoint(IFileStorageService files, ILocalTusFileStoreAccessor storeAccessor)
    : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        var uploadSessionId = files is ILocalFileContentIndex index
            ? await index.GetUploadSessionIdForDownloadGrantAsync(Route<string>("downloadGrantId")!, ct)
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
