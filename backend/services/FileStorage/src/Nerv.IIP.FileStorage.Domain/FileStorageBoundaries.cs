namespace Nerv.IIP.FileStorage.Domain;

public sealed record FileMetadata(string FileId, string Purpose, string ObjectKey, string ScanStatus);
public sealed record UploadSession(string UploadSessionId, string FileId, string Provider, DateTimeOffset ExpiresAtUtc);
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
