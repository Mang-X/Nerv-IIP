using Nerv.IIP.Contracts.FileStorage;

namespace Nerv.IIP.FileStorage.Web.Application.Files;

public interface IFileStorageService
{
    Task<FileStorageResult<CreateUploadSessionResponse>> CreateUploadSessionAsync(
        CreateUploadSessionRequest request,
        CancellationToken cancellationToken);

    Task<FileStorageResult<FileMetadataResponse>> CompleteUploadSessionAsync(
        string uploadSessionId,
        CompleteUploadSessionRequest request,
        CancellationToken cancellationToken);

    Task<FileStorageResult<FileMetadataResponse>> GetFileMetadataAsync(
        string fileId,
        CancellationToken cancellationToken);

    Task<FileStorageResult<FileListResponse>> ListFilesAsync(
        ListFilesRequest request,
        CancellationToken cancellationToken);

    Task<FileStorageResult<FileStorageUsageResponse>> GetUsageAsync(
        FileStorageUsageRequest request,
        CancellationToken cancellationToken);

    Task<FileStorageResult<DownloadGrantResponse>> CreateDownloadGrantAsync(
        string fileId,
        CreateDownloadGrantRequest request,
        CancellationToken cancellationToken);
}

public interface ILocalFileContentIndex
{
    Task<string?> GetUploadSessionIdForDownloadGrantAsync(
        string downloadGrantId,
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken);
}
