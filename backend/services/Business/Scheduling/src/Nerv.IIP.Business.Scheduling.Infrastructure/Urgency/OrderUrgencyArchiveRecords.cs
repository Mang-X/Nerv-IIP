namespace Nerv.IIP.Business.Scheduling.Infrastructure.Urgency;

public sealed class OrderUrgencyArchiveBatch
{
    private OrderUrgencyArchiveBatch()
    {
    }

    private OrderUrgencyArchiveBatch(
        string batchId,
        string organizationId,
        string environmentId,
        string snapshotIdsJson,
        int snapshotCount,
        DateTimeOffset minCalculatedAtUtc,
        DateTimeOffset maxCalculatedAtUtc,
        DateTimeOffset createdAtUtc)
    {
        Id = Guid.CreateVersion7();
        BatchId = batchId;
        OrganizationId = organizationId;
        EnvironmentId = environmentId;
        SnapshotIdsJson = snapshotIdsJson;
        SnapshotCount = snapshotCount;
        MinCalculatedAtUtc = minCalculatedAtUtc;
        MaxCalculatedAtUtc = maxCalculatedAtUtc;
        CreatedAtUtc = createdAtUtc;
        Status = "pending";
    }

    public Guid Id { get; private set; }
    public string BatchId { get; private set; } = string.Empty;
    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string SnapshotIdsJson { get; private set; } = "[]";
    public int SnapshotCount { get; private set; }
    public DateTimeOffset MinCalculatedAtUtc { get; private set; }
    public DateTimeOffset MaxCalculatedAtUtc { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public string? ObjectKey { get; private set; }
    public string? ObjectVersionId { get; private set; }
    public string? Sha256 { get; private set; }
    public long? SizeBytes { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? ArchivedAtUtc { get; private set; }
    public DateTimeOffset? SourceDeletedAtUtc { get; private set; }
    public DateTimeOffset? ArchiveDeletedAtUtc { get; private set; }
    public string? SourceDeletionAuthorizationReference { get; private set; }
    public string? SourceDeletionActor { get; private set; }
    public string? SourceDeletionReason { get; private set; }
    public string? ArchiveDeletionAuthorizationReference { get; private set; }
    public string? ArchiveDeletionActor { get; private set; }
    public string? ArchiveDeletionReason { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public int AttemptCount { get; private set; }

    public static OrderUrgencyArchiveBatch Create(
        string batchId,
        string organizationId,
        string environmentId,
        string snapshotIdsJson,
        int snapshotCount,
        DateTimeOffset minCalculatedAtUtc,
        DateTimeOffset maxCalculatedAtUtc,
        DateTimeOffset createdAtUtc) =>
        new(batchId, organizationId, environmentId, snapshotIdsJson, snapshotCount, minCalculatedAtUtc, maxCalculatedAtUtc, createdAtUtc);

    public void MarkArchived(
        string objectKey,
        string objectVersionId,
        string sha256,
        long sizeBytes,
        DateTimeOffset archivedAtUtc)
    {
        ObjectKey = objectKey;
        ObjectVersionId = objectVersionId;
        Sha256 = sha256;
        SizeBytes = sizeBytes;
        ArchivedAtUtc = archivedAtUtc;
        Status = "archived";
        ErrorCode = null;
        ErrorMessage = null;
        AttemptCount++;
    }

    public void MarkFailed(string errorCode, string errorMessage)
    {
        Status = "failed";
        ErrorCode = errorCode;
        ErrorMessage = errorMessage.Length <= 2000 ? errorMessage : errorMessage[..2000];
        AttemptCount++;
    }

    public void MarkSourceDeleted(
        string authorizationReference,
        string actor,
        string reason,
        DateTimeOffset deletedAtUtc)
    {
        Status = "source-deleted";
        SourceDeletedAtUtc = deletedAtUtc;
        SourceDeletionAuthorizationReference = authorizationReference;
        SourceDeletionActor = actor;
        SourceDeletionReason = reason;
    }

    public void MarkArchiveDeleted(
        string authorizationReference,
        string actor,
        string reason,
        DateTimeOffset deletedAtUtc)
    {
        Status = "archive-deleted";
        ArchiveDeletedAtUtc = deletedAtUtc;
        ArchiveDeletionAuthorizationReference = authorizationReference;
        ArchiveDeletionActor = actor;
        ArchiveDeletionReason = reason;
    }
}

public sealed class OrderUrgencyRetentionLease
{
    private OrderUrgencyRetentionLease()
    {
    }

    public OrderUrgencyRetentionLease(
        string organizationId,
        string environmentId,
        string ownerId,
        DateTimeOffset acquiredAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        Id = Guid.CreateVersion7();
        OrganizationId = organizationId;
        EnvironmentId = environmentId;
        OwnerId = ownerId;
        AcquiredAtUtc = acquiredAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        Revision = 1;
    }

    public Guid Id { get; private set; }
    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string OwnerId { get; private set; } = string.Empty;
    public DateTimeOffset AcquiredAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public long Revision { get; private set; }

    public bool IsActiveAt(DateTimeOffset now) => ExpiresAtUtc > now;

    public void Acquire(string ownerId, DateTimeOffset now, DateTimeOffset expiresAtUtc)
    {
        OwnerId = ownerId;
        AcquiredAtUtc = now;
        ExpiresAtUtc = expiresAtUtc;
        Revision++;
    }

    public void Release(string ownerId, DateTimeOffset now)
    {
        if (!string.Equals(OwnerId, ownerId, StringComparison.Ordinal)) return;
        ExpiresAtUtc = now;
        Revision++;
    }
}

public sealed class OrderUrgencyRestoreAudit
{
    private OrderUrgencyRestoreAudit()
    {
    }

    public OrderUrgencyRestoreAudit(
        string batchId,
        string organizationId,
        string environmentId,
        string objectVersionId,
        string actor,
        string reason,
        int restoredSnapshotCount,
        DateTimeOffset restoredAtUtc)
    {
        Id = Guid.CreateVersion7();
        BatchId = batchId;
        OrganizationId = organizationId;
        EnvironmentId = environmentId;
        ObjectVersionId = objectVersionId;
        Actor = actor;
        Reason = reason;
        RestoredSnapshotCount = restoredSnapshotCount;
        RestoredAtUtc = restoredAtUtc;
    }

    public Guid Id { get; private set; }
    public string BatchId { get; private set; } = string.Empty;
    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string ObjectVersionId { get; private set; } = string.Empty;
    public string Actor { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public int RestoredSnapshotCount { get; private set; }
    public DateTimeOffset RestoredAtUtc { get; private set; }
}
