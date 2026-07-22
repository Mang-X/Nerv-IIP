namespace Nerv.IIP.Contracts.FileStorage;

public static class VersionedArchiveLimits
{
    // MinIO 7 switches to multipart at 5 MiB. Conditional If-None-Match is
    // guaranteed on the single PutObject path, so compliance archives remain
    // strictly below that boundary.
    public const int MaximumConditionallyWritableBytes = (5 * 1024 * 1024) - 1;
}

public sealed record OwnerReference(string OwnerService, string OwnerType, string OwnerId);

public sealed record TransferInstructions(string Url, IReadOnlyDictionary<string, string> Headers);

public sealed record CreateUploadSessionRequest(
    string OrganizationId,
    string EnvironmentId,
    OwnerReference Owner,
    string FilePurpose,
    string FileName,
    string ContentType,
    long ExpectedSizeBytes,
    string? Checksum);

public sealed record CompleteUploadSessionRequest(
    string OrganizationId,
    string EnvironmentId,
    string FilePurpose,
    string? Checksum = null,
    long? SizeBytes = null);

public sealed record CreateDownloadGrantRequest(string OrganizationId, string EnvironmentId);

public sealed record FileStorageUsageRequest(
    string OrganizationId,
    string EnvironmentId,
    string? FilePurpose = null);

public sealed record ListFilesRequest(
    string OrganizationId,
    string EnvironmentId,
    string? FilePurpose,
    string? UploaderId,
    DateTimeOffset? CreatedFromUtc,
    DateTimeOffset? CreatedToUtc,
    string? Status,
    int? Skip = null,
    int? Take = null);

public sealed record CreateUploadSessionResponse(
    string UploadSessionId,
    string FileId,
    string UploadMode,
    string Provider,
    DateTimeOffset ExpiresAtUtc,
    TransferInstructions Upload);

public sealed record FileMetadataResponse(
    string FileId,
    string OrganizationId,
    string EnvironmentId,
    OwnerReference Owner,
    string FilePurpose,
    string FileName,
    string ContentType,
    long SizeBytes,
    string? Checksum,
    string ScanStatus,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset CompletedAtUtc);

public sealed record FileListResponse(int Total, IReadOnlyList<FileMetadataResponse> Items);

public sealed record FileStorageUsageResponse(
    string OrganizationId,
    string EnvironmentId,
    string? FilePurpose,
    long UsedBytes,
    long? QuotaBytes);

public sealed record DownloadGrantResponse(
    string FileId,
    DateTimeOffset ExpiresAtUtc,
    TransferInstructions Download);

public sealed record PutVersionedArchiveRequest(
    string OrganizationId,
    string EnvironmentId,
    string ArchiveKind,
    string BatchId,
    string ContentBase64,
    string ContentType,
    string Sha256,
    bool LegalHold);

public sealed record VersionedArchiveEvidence(
    string ObjectKey,
    string VersionId,
    string Sha256,
    long SizeBytes,
    DateTimeOffset VerifiedAtUtc);

public sealed record GetVersionedArchiveRequest(
    string OrganizationId,
    string EnvironmentId,
    string ObjectKey,
    string VersionId,
    string Sha256,
    long SizeBytes);

public sealed record GetVersionedArchiveResponse(
    VersionedArchiveEvidence Evidence,
    string ContentBase64);

public sealed record DeleteVersionedArchiveRequest(
    string OrganizationId,
    string EnvironmentId,
    string ObjectKey,
    string VersionId,
    string AuthorizationReference,
    string Actor,
    string Reason);
