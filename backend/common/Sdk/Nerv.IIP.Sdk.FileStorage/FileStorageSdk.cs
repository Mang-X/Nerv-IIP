namespace Nerv.IIP.Sdk.FileStorage;

public sealed record FileReference(string FileId, string Purpose, string DisplayName);

public sealed record UploadInstruction(string UploadSessionId, Uri UploadUri, IReadOnlyDictionary<string, string> Headers);

public sealed record DownloadGrant(string FileId, Uri DownloadUri, DateTimeOffset ExpiresAtUtc);
