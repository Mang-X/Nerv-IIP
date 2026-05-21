namespace Nerv.IIP.Contracts.FileStorage;

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

public sealed record DownloadGrantResponse(
    string FileId,
    DateTimeOffset ExpiresAtUtc,
    TransferInstructions Download);
