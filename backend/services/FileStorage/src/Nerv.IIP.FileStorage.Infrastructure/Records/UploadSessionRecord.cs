namespace Nerv.IIP.FileStorage.Infrastructure.Records;

public sealed class UploadSessionRecord
{
    private UploadSessionRecord()
    {
    }

    public string UploadSessionId { get; private set; } = string.Empty;
    public string FileId { get; private set; } = string.Empty;
    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string OwnerService { get; private set; } = string.Empty;
    public string OwnerType { get; private set; } = string.Empty;
    public string OwnerId { get; private set; } = string.Empty;
    public string FilePurpose { get; private set; } = string.Empty;
    public string FileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long ExpectedSizeBytes { get; private set; }
    public string? Checksum { get; private set; }
    public string ObjectKey { get; private set; } = string.Empty;
    public string Provider { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public bool Completed { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public static UploadSessionRecord Create(
        string uploadSessionId,
        string fileId,
        string organizationId,
        string environmentId,
        string ownerService,
        string ownerType,
        string ownerId,
        string filePurpose,
        string fileName,
        string contentType,
        long expectedSizeBytes,
        string? checksum,
        string objectKey,
        string provider,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        return new UploadSessionRecord
        {
            UploadSessionId = uploadSessionId,
            FileId = fileId,
            OrganizationId = organizationId,
            EnvironmentId = environmentId,
            OwnerService = ownerService,
            OwnerType = ownerType,
            OwnerId = ownerId,
            FilePurpose = filePurpose,
            FileName = fileName,
            ContentType = contentType,
            ExpectedSizeBytes = expectedSizeBytes,
            Checksum = checksum,
            ObjectKey = objectKey,
            Provider = provider,
            CreatedAtUtc = createdAtUtc,
            ExpiresAtUtc = expiresAtUtc,
            Completed = false
        };
    }

    public void MarkCompleted(DateTimeOffset completedAtUtc)
    {
        Completed = true;
        CompletedAtUtc = completedAtUtc;
    }
}
