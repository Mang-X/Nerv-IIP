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
    public string State { get; private set; } = UploadSessionState.Open;
    public string? CommitId { get; private set; }
    public string? CommitChecksum { get; private set; }
    public DateTimeOffset? CommittingAtUtc { get; private set; }
    public DateTimeOffset? StorageActionStartedAtUtc { get; private set; }
    public int RecoveryAttemptCount { get; private set; }
    public DateTimeOffset? NextRecoveryAtUtc { get; private set; }
    public string? LastRecoveryErrorCode { get; private set; }
    public DateTimeOffset? RecoveryTerminalAtUtc { get; private set; }
    public string? ExecutionOwnerId { get; private set; }
    public DateTimeOffset? ExecutionLeaseUntilUtc { get; private set; }
    public long ConcurrencyVersion { get; private set; }
    public bool LegacyCompleted { get; private set; }
    public bool Completed => string.Equals(State, UploadSessionState.Completed, StringComparison.Ordinal)
        || (LegacyCompleted && string.Equals(State, UploadSessionState.Open, StringComparison.Ordinal));
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
            State = UploadSessionState.Open,
            LegacyCompleted = false
        };
    }

    public void BeginCommit(string commitId, string? commitChecksum, DateTimeOffset committingAtUtc)
    {
        if (!string.Equals(State, UploadSessionState.Open, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("只有 open 状态的上传会话才能开始提交。");
        }

        State = UploadSessionState.Committing;
        LegacyCompleted = false;
        CommitId = commitId;
        CommitChecksum = commitChecksum;
        CommittingAtUtc = committingAtUtc;
        ConcurrencyVersion++;
    }

    public void MarkStorageActionStarted(DateTimeOffset startedAtUtc)
    {
        if (!string.Equals(State, UploadSessionState.Committing, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("执行存储操作要求上传会话处于 committing 状态。");
        }

        StorageActionStartedAtUtc ??= startedAtUtc;
        ConcurrencyVersion++;
    }

    public void RecordRecoveryFailure(string errorCode, DateTimeOffset nextRecoveryAtUtc)
    {
        RecoveryAttemptCount++;
        LastRecoveryErrorCode = errorCode;
        NextRecoveryAtUtc = nextRecoveryAtUtc;
        ExecutionOwnerId = null;
        ExecutionLeaseUntilUtc = null;
        ConcurrencyVersion++;
    }

    public void RecordTerminalRecoveryFailure(string errorCode, DateTimeOffset terminalAtUtc)
    {
        RecoveryAttemptCount++;
        LastRecoveryErrorCode = errorCode;
        NextRecoveryAtUtc = null;
        RecoveryTerminalAtUtc = terminalAtUtc;
        ExecutionOwnerId = null;
        ExecutionLeaseUntilUtc = null;
        ConcurrencyVersion++;
    }

    public void ClaimExecution(string ownerId, DateTimeOffset leaseUntilUtc)
    {
        ExecutionOwnerId = ownerId;
        ExecutionLeaseUntilUtc = leaseUntilUtc;
        ConcurrencyVersion++;
    }

    public void RenewExecutionLease(DateTimeOffset leaseUntilUtc)
    {
        if (!string.Equals(State, UploadSessionState.Committing, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(ExecutionOwnerId))
        {
            throw new InvalidOperationException("只有当前 committing 执行所有者才能续租。");
        }

        ExecutionLeaseUntilUtc = leaseUntilUtc;
    }

    public void ReopenAfterStorageProvedNotStarted()
    {
        if (!string.Equals(State, UploadSessionState.Committing, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("只有 committing 状态的上传会话才能安全重新打开。");
        }

        State = UploadSessionState.Open;
        LegacyCompleted = false;
        CommitId = null;
        CommitChecksum = null;
        CommittingAtUtc = null;
        StorageActionStartedAtUtc = null;
        RecoveryAttemptCount = 0;
        NextRecoveryAtUtc = null;
        LastRecoveryErrorCode = null;
        RecoveryTerminalAtUtc = null;
        ExecutionOwnerId = null;
        ExecutionLeaseUntilUtc = null;
        ConcurrencyVersion++;
    }

    public void MarkCompleted(DateTimeOffset completedAtUtc)
    {
        State = UploadSessionState.Completed;
        LegacyCompleted = true;
        CompletedAtUtc = completedAtUtc;
        NextRecoveryAtUtc = null;
        LastRecoveryErrorCode = null;
        RecoveryTerminalAtUtc = null;
        ExecutionOwnerId = null;
        ExecutionLeaseUntilUtc = null;
        ConcurrencyVersion++;
    }
}

public static class UploadSessionState
{
    public const string Open = "open";
    public const string Committing = "committing";
    public const string Completed = "completed";
}
