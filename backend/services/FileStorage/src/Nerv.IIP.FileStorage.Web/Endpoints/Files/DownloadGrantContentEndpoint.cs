using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Net.Http.Headers;
using Nerv.IIP.FileStorage.Web.Application.Files;
using Nerv.IIP.FileStorage.Web.Application.Files.Tus;

namespace Nerv.IIP.FileStorage.Web.Endpoints.Files;

[Tags("Files")]
[HttpGet("/api/files/v1/download-grants/{downloadGrantId}/content")]
[Authorize(Policy = FileDownloadAuthorization.Policy)]
public sealed class DownloadGrantContentEndpoint(
    IFileStorageService files, ILocalTusFileStoreAccessor storeAccessor, S3FinalFileStore s3)
    : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        var organizationId = HttpContext.Request.Headers[FileStorageTransferHeaders.OrganizationId].ToString();
        var environmentId = HttpContext.Request.Headers[FileStorageTransferHeaders.EnvironmentId].ToString();
        var content = files is ILocalFileContentIndex index
                && !string.IsNullOrWhiteSpace(organizationId)
                && !string.IsNullOrWhiteSpace(environmentId)
            ? await index.RedeemDownloadGrantAsync(
                Route<string>("downloadGrantId")!, organizationId, environmentId, ct,
                FileDownloadAuthorization.GetCaller(User))
            : null;

        if (content is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        if (s3.IsEnabled)
        {
            await SendS3ContentAsync(content, ct);
            return;
        }

        await using var stream = storeAccessor.TryGet(out var store) ? store.TryOpenFinal(content.ObjectKey) : null;
        if (stream is null || stream.Length != content.SizeBytes)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await SendContentAsync(content, stream, ct);
    }

    private async Task SendS3ContentAsync(LocalDownloadContent content, CancellationToken ct)
    {
        try
        {
            using var response = await s3.TryGetAsync(content.ObjectKey, ct);
            if (response is null || response.Content.Headers.ContentLength != content.SizeBytes)
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            await SendContentAsync(content, stream, ct);
        }
        catch (Exception error) when (!ct.IsCancellationRequested && error is IOException or HttpRequestException)
        {
            if (HttpContext.Response.HasStarted)
            {
                HttpContext.Abort();
                return;
            }

            HttpContext.Response.Clear();
            await Send.StatusCodeAsync(StatusCodes.Status503ServiceUnavailable, ct);
        }
    }

    private async Task SendContentAsync(LocalDownloadContent content, Stream stream, CancellationToken ct)
    {
        var fileName = Path.GetFileName(content.FileName.Replace('\\', '/'));
        fileName = string.Concat(fileName.Select(character =>
            char.IsControl(character) || char.GetUnicodeCategory(character) == System.Globalization.UnicodeCategory.Format
                || "<>:\"|?*".Contains(character) ? '_' : character)).Trim().Trim('.');
        var disposition = new ContentDispositionHeaderValue("attachment");
        disposition.SetHttpFileName(string.IsNullOrEmpty(fileName) ? "download" : fileName);
        HttpContext.Response.Headers.ContentDisposition = disposition.ToString();
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        HttpContext.Response.Headers.XContentTypeOptions = "nosniff";
        HttpContext.Response.ContentType = "application/octet-stream";
        HttpContext.Response.ContentLength = content.SizeBytes;
        await stream.CopyToAsync(HttpContext.Response.Body, ct);
    }
}
