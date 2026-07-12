using System.Net.Http.Json;
using Nerv.IIP.Contracts.FileStorage;

namespace Nerv.IIP.Sdk.FileStorage;

public interface IFileStorageClient
{
    Task<CreateUploadSessionResponse> CreateUploadSessionAsync(CreateUploadSessionRequest request, CancellationToken cancellationToken = default);

    Task<FileMetadataResponse> CompleteUploadSessionAsync(string uploadSessionId, CompleteUploadSessionRequest request, CancellationToken cancellationToken = default);

    Task<FileMetadataResponse> GetFileMetadataAsync(string fileId, CancellationToken cancellationToken = default);

    Task<DownloadGrantResponse> CreateDownloadGrantAsync(string fileId, CreateDownloadGrantRequest request, CancellationToken cancellationToken = default);

    Task<FileStorageUsageResponse> GetUsageAsync(FileStorageUsageRequest request, CancellationToken cancellationToken = default);
}

public sealed class HttpFileStorageClient(HttpClient httpClient) : IFileStorageClient
{
    public async Task<CreateUploadSessionResponse> CreateUploadSessionAsync(CreateUploadSessionRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("/api/files/v1/upload-sessions", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CreateUploadSessionResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("FileStorage returned an empty upload session response.");
    }

    public async Task<FileMetadataResponse> CompleteUploadSessionAsync(string uploadSessionId, CompleteUploadSessionRequest request, CancellationToken cancellationToken = default)
    {
        var escapedUploadSessionId = Uri.EscapeDataString(uploadSessionId);
        using var response = await httpClient.PostAsJsonAsync($"/api/files/v1/upload-sessions/{escapedUploadSessionId}/complete", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<FileMetadataResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("FileStorage returned an empty file metadata response.");
    }

    public async Task<FileMetadataResponse> GetFileMetadataAsync(string fileId, CancellationToken cancellationToken = default)
    {
        var escapedFileId = Uri.EscapeDataString(fileId);
        return await httpClient.GetFromJsonAsync<FileMetadataResponse>($"/api/files/v1/files/{escapedFileId}", cancellationToken)
            ?? throw new InvalidOperationException("FileStorage returned an empty file metadata response.");
    }

    public async Task<DownloadGrantResponse> CreateDownloadGrantAsync(string fileId, CreateDownloadGrantRequest request, CancellationToken cancellationToken = default)
    {
        var escapedFileId = Uri.EscapeDataString(fileId);
        using var response = await httpClient.PostAsJsonAsync($"/api/files/v1/files/{escapedFileId}/download-grants", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DownloadGrantResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("FileStorage returned an empty download grant response.");
    }

    public async Task<FileStorageUsageResponse> GetUsageAsync(FileStorageUsageRequest request, CancellationToken cancellationToken = default)
    {
        var path = "/api/files/v1/usage" +
            $"?organizationId={Uri.EscapeDataString(request.OrganizationId)}" +
            $"&environmentId={Uri.EscapeDataString(request.EnvironmentId)}";

        if (!string.IsNullOrWhiteSpace(request.FilePurpose))
        {
            path += $"&filePurpose={Uri.EscapeDataString(request.FilePurpose)}";
        }

        return await httpClient.GetFromJsonAsync<FileStorageUsageResponse>(path, cancellationToken)
            ?? throw new InvalidOperationException("FileStorage returned an empty usage response.");
    }
}
