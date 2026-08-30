using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Nerv.IIP.Contracts.FileStorage;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public interface IBusinessFileStorageClient
{
    Task<BusinessConsoleSopFileDownloadGrantResponse> CreateSopFileDownloadGrantAsync(
        string internalBearerToken,
        string fileId,
        BusinessConsoleCreateSopFileDownloadGrantRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleSopFileContentResponse> DownloadSopFileContentAsync(
        string internalBearerToken,
        string downloadGrantId,
        IReadOnlyDictionary<string, string> downloadHeaders,
        CancellationToken cancellationToken);
}

public sealed class HttpBusinessFileStorageClient(HttpClient httpClient) : IBusinessFileStorageClient
{
    public async Task<BusinessConsoleSopFileDownloadGrantResponse> CreateSopFileDownloadGrantAsync(
        string internalBearerToken,
        string fileId,
        BusinessConsoleCreateSopFileDownloadGrantRequest request,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/files/v1/files/{Uri.EscapeDataString(fileId)}/download-grants");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", internalBearerToken);
        message.Content = JsonContent.Create(new CreateDownloadGrantRequest(request.OrganizationId, request.EnvironmentId));
        using var response = await httpClient.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(response.StatusCode, "filestorage-download-grant-failed");
        }

        var grant = await response.Content.ReadFromJsonAsync<DownloadGrantResponse>(cancellationToken: cancellationToken)
            ?? throw BusinessServiceProxyException.FromSafeDownstreamMessage(HttpStatusCode.BadGateway, "filestorage-empty-response");
        return new BusinessConsoleSopFileDownloadGrantResponse(
            grant.FileId,
            grant.ExpiresAtUtc,
            RewriteDownloadGrantContentUrl(grant.Download.Url),
            grant.Download.Headers);
    }

    public async Task<BusinessConsoleSopFileContentResponse> DownloadSopFileContentAsync(
        string internalBearerToken,
        string downloadGrantId,
        IReadOnlyDictionary<string, string> downloadHeaders,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/files/v1/download-grants/{Uri.EscapeDataString(downloadGrantId)}/content");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", internalBearerToken);
        foreach (var (key, value) in downloadHeaders)
        {
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
            {
                message.Headers.TryAddWithoutValidation(key, value);
            }
        }

        using var response = await httpClient.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(response.StatusCode, "filestorage-download-content-failed");
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return new BusinessConsoleSopFileContentResponse(
            response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream",
            response.Content.Headers.ContentLength,
            bytes);
    }

    private static string RewriteDownloadGrantContentUrl(string downloadUrl)
    {
        const string fileStoragePrefix = "/api/files/v1/download-grants/";
        const string businessConsolePrefix = "/api/business-console/v1/files/download-grants/";
        return downloadUrl.StartsWith(fileStoragePrefix, StringComparison.Ordinal)
            ? businessConsolePrefix + downloadUrl[fileStoragePrefix.Length..]
            : downloadUrl;
    }
}
