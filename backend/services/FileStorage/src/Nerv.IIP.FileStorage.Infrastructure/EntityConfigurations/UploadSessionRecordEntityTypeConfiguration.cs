using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nerv.IIP.FileStorage.Infrastructure.Records;

namespace Nerv.IIP.FileStorage.Infrastructure.EntityConfigurations;

public sealed class UploadSessionRecordEntityTypeConfiguration : IEntityTypeConfiguration<UploadSessionRecord>
{
    public void Configure(EntityTypeBuilder<UploadSessionRecord> builder)
    {
        builder.ToTable("upload_sessions", table => table.HasComment("FileStorage upload session metadata created before object bytes are completed."));
        builder.HasKey(x => x.UploadSessionId);

        builder.Property(x => x.UploadSessionId)
            .HasColumnName("upload_session_id")
            .ValueGeneratedNever()
            .HasMaxLength(64)
            .HasComment("Stable upload session identifier returned by the public FileStorage API.");
        builder.Property(x => x.FileId)
            .HasColumnName("file_id")
            .ValueGeneratedNever()
            .IsRequired()
            .HasMaxLength(64)
            .HasComment("File identifier reserved for the upload session.");
        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .ValueGeneratedNever()
            .IsRequired()
            .HasMaxLength(128)
            .HasComment("Organization identifier that owns the upload session.");
        builder.Property(x => x.EnvironmentId)
            .HasColumnName("environment_id")
            .ValueGeneratedNever()
            .IsRequired()
            .HasMaxLength(128)
            .HasComment("Environment identifier that scopes the upload session.");
        builder.Property(x => x.OwnerService)
            .HasColumnName("owner_service")
            .IsRequired()
            .HasMaxLength(128)
            .HasComment("Service that owns the eventual file metadata.");
        builder.Property(x => x.OwnerType)
            .HasColumnName("owner_type")
            .IsRequired()
            .HasMaxLength(128)
            .HasComment("Owner resource type within the owning service.");
        builder.Property(x => x.OwnerId)
            .HasColumnName("owner_id")
            .ValueGeneratedNever()
            .IsRequired()
            .HasMaxLength(128)
            .HasComment("Owner resource identifier within the owning service.");
        builder.Property(x => x.FilePurpose)
            .HasColumnName("file_purpose")
            .IsRequired()
            .HasMaxLength(128)
            .HasComment("Purpose policy key used to validate and route the upload.");
        builder.Property(x => x.FileName)
            .HasColumnName("file_name")
            .IsRequired()
            .HasMaxLength(512)
            .HasComment("Original file name supplied by the caller.");
        builder.Property(x => x.ContentType)
            .HasColumnName("content_type")
            .IsRequired()
            .HasMaxLength(256)
            .HasComment("Media type declared for the upload.");
        builder.Property(x => x.ExpectedSizeBytes)
            .HasColumnName("expected_size_bytes")
            .HasComment("Expected object size in bytes supplied during session creation.");
        builder.Property(x => x.Checksum)
            .HasColumnName("checksum")
            .HasMaxLength(256)
            .HasComment("Optional caller-provided checksum for integrity tracking.");
        builder.Property(x => x.ObjectKey)
            .HasColumnName("object_key")
            .ValueGeneratedNever()
            .IsRequired()
            .HasMaxLength(1024)
            .HasComment("Internal object storage key reserved for this upload session.");
        builder.Property(x => x.Provider)
            .HasColumnName("provider")
            .IsRequired()
            .HasMaxLength(64)
            .HasComment("Upload provider used for this session.");
        builder.Property(x => x.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasComment("UTC timestamp when the upload session was created.");
        builder.Property(x => x.ExpiresAtUtc)
            .HasColumnName("expires_at_utc")
            .HasComment("UTC timestamp when the upload session expires.");
        builder.Ignore(x => x.Completed);
        builder.Property(x => x.State)
            .HasColumnName("state")
            .IsRequired()
            .HasMaxLength(32)
            .HasComment("持久上传生命周期状态：open、committing 或 completed。");
        builder.Property(x => x.CommitId)
            .HasColumnName("commit_id")
            .HasMaxLength(64)
            .HasComment("首次持久提交 Tx1 时创建的不可变唯一所有权标识。");
        builder.Property(x => x.CommitChecksum)
            .HasColumnName("commit_checksum")
            .HasMaxLength(71)
            .HasComment("不可变的预期规范 SHA-256 证据；最终存储需要自行计算时为空。");
        builder.Property(x => x.CommittingAtUtc)
            .HasColumnName("committing_at_utc")
            .HasComment("Tx1 将上传会话持久转换为 committing 状态时的 UTC 时间戳。");
        builder.Property(x => x.StorageActionStartedAtUtc)
            .HasColumnName("storage_action_started_at_utc")
            .HasComment("任何可能建立最终字节的存储操作开始前写入的 UTC 持久标记。");
        builder.Property(x => x.RecoveryAttemptCount)
            .HasColumnName("recovery_attempt_count")
            .HasComment("此不可变提交意图的存储恢复失败次数。");
        builder.Property(x => x.NextRecoveryAtUtc)
            .HasColumnName("next_recovery_at_utc")
            .HasComment("恢复工作进程不得在此 UTC 时间戳之前重试此提交意图。");
        builder.Property(x => x.LastRecoveryErrorCode)
            .HasColumnName("last_recovery_error_code")
            .HasMaxLength(64)
            .HasComment("最近一次恢复尝试产生的稳定非敏感诊断码。");
        builder.Property(x => x.ConcurrencyVersion)
            .HasColumnName("concurrency_version")
            .IsConcurrencyToken()
            .HasComment("应用程序管理的上传状态转换乐观并发版本。");
        builder.Property(x => x.ExecutionOwnerId)
            .HasColumnName("execution_owner_id")
            .HasMaxLength(64)
            .HasComment("获准为提交意图执行存储 I/O 的短期持久所有者。");
        builder.Property(x => x.ExecutionLeaseUntilUtc)
            .HasColumnName("execution_lease_until_utc")
            .HasComment("当前存储执行租约的 UTC 到期时间。");
        builder.Property(x => x.CompletedAtUtc)
            .HasColumnName("completed_at_utc")
            .HasComment("UTC timestamp when the upload session was completed.");

        builder.HasIndex(x => x.FileId).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.ExpiresAtUtc });
        builder.HasIndex(x => x.ObjectKey).IsUnique();
        builder.HasIndex(x => x.CommitId).IsUnique();
        builder.HasIndex(x => new { x.State, x.NextRecoveryAtUtc });
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_upload_sessions_state_intent",
            "(state = 'open' AND commit_id IS NULL AND committing_at_utc IS NULL AND storage_action_started_at_utc IS NULL AND completed_at_utc IS NULL AND recovery_attempt_count = 0 AND next_recovery_at_utc IS NULL AND last_recovery_error_code IS NULL AND execution_owner_id IS NULL AND execution_lease_until_utc IS NULL) OR " +
            "(state = 'committing' AND commit_id IS NOT NULL AND committing_at_utc IS NOT NULL AND completed_at_utc IS NULL AND recovery_attempt_count >= 0 AND ((execution_owner_id IS NULL AND execution_lease_until_utc IS NULL) OR (execution_owner_id IS NOT NULL AND execution_lease_until_utc IS NOT NULL))) OR " +
            "(state = 'completed' AND commit_id IS NOT NULL AND committing_at_utc IS NOT NULL AND completed_at_utc IS NOT NULL AND recovery_attempt_count >= 0 AND next_recovery_at_utc IS NULL AND last_recovery_error_code IS NULL AND execution_owner_id IS NULL AND execution_lease_until_utc IS NULL)"));
    }
}
