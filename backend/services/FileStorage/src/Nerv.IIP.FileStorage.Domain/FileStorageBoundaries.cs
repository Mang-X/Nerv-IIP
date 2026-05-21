namespace Nerv.IIP.FileStorage.Domain;

public sealed record OwnerReference(string OwnerService, string OwnerType, string OwnerId);

public sealed record FileMetadata(
    string FileId,
    string OrganizationId,
    string EnvironmentId,
    OwnerReference Owner,
    string FilePurpose,
    string FileName,
    string ContentType,
    long SizeBytes,
    string? Checksum,
    string ObjectKey,
    string ScanStatus,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset CompletedAtUtc);

public sealed record UploadSession(
    string UploadSessionId,
    string FileId,
    string OrganizationId,
    string EnvironmentId,
    OwnerReference Owner,
    string FilePurpose,
    string FileName,
    string ContentType,
    long ExpectedSizeBytes,
    string? Checksum,
    string Provider,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    bool Completed);

public sealed record UploadInstruction(string UploadSessionId, Uri UploadUri, IReadOnlyDictionary<string, string> Headers);
public sealed record DownloadGrant(string FileId, Uri DownloadUri, DateTimeOffset ExpiresAtUtc);

public interface IUploadProvider
{
    string ProviderName { get; }
    UploadInstruction CreateInstruction(UploadSession session);
}

public interface IObjectStorageAdapter
{
    string BuildObjectKey(string organizationId, string fileId);
}

public static class FilePurposePolicy
{
    public static readonly string[] SupportedPurposes = ["application-package", "avatar", "attachment", "diagnostic-log"];
    public static bool IsAllowed(string purpose) => SupportedPurposes.Contains(purpose, StringComparer.Ordinal);
}
