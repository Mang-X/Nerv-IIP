namespace Nerv.IIP.FileStorage.Infrastructure.Records;

public sealed class StoredFileRecord
{
    private StoredFileRecord()
    {
    }

    public string FileId { get; private set; } = string.Empty;
    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string OwnerService { get; private set; } = string.Empty;
    public string OwnerType { get; private set; } = string.Empty;
    public string OwnerId { get; private set; } = string.Empty;
    public string FilePurpose { get; private set; } = string.Empty;
    public string FileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public string? Checksum { get; private set; }
    public string ObjectKey { get; private set; } = string.Empty;
    public string ScanStatus { get; private set; } = string.Empty;
    public DateTimeOffset? ScannedAtUtc { get; private set; }
    public string? ScanDetail { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset CompletedAtUtc { get; private set; }
    public DateTimeOffset? DeletedAtUtc { get; private set; }
    public DateTimeOffset? PhysicalDeleteAfterUtc { get; private set; }
    public string? DeletionReason { get; private set; }

    public static StoredFileRecord Create(
        string fileId,
        string organizationId,
        string environmentId,
        string ownerService,
        string ownerType,
        string ownerId,
        string filePurpose,
        string fileName,
        string contentType,
        long sizeBytes,
        string? checksum,
        string objectKey,
        string scanStatus,
        string status,
        DateTimeOffset createdAtUtc,
        DateTimeOffset completedAtUtc)
    {
        return new StoredFileRecord
        {
            FileId = fileId,
            OrganizationId = organizationId,
            EnvironmentId = environmentId,
            OwnerService = ownerService,
            OwnerType = ownerType,
            OwnerId = ownerId,
            FilePurpose = filePurpose,
            FileName = fileName,
            ContentType = contentType,
            SizeBytes = sizeBytes,
            Checksum = checksum,
            ObjectKey = objectKey,
            ScanStatus = scanStatus,
            Status = status,
            CreatedAtUtc = createdAtUtc,
            CompletedAtUtc = completedAtUtc
        };
    }

    public void MarkScanned(string scanStatus, DateTimeOffset scannedAtUtc, string? scanDetail)
    {
        ScanStatus = scanStatus;
        ScannedAtUtc = scannedAtUtc;
        ScanDetail = scanDetail;
    }

    public void MarkDeleted(DateTimeOffset deletedAtUtc, string reason, TimeSpan? physicalDeleteGrace = null)
    {
        Status = "deleted";
        DeletedAtUtc = deletedAtUtc;
        DeletionReason = reason;
        PhysicalDeleteAfterUtc = deletedAtUtc.Add(physicalDeleteGrace ?? TimeSpan.Zero);
    }
}
