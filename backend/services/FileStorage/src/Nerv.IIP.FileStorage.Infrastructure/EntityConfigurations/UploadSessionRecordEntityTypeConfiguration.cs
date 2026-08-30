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
        builder.Property(x => x.LegacyCompleted)
            .HasColumnName("completed")
            .HasComment("Expand-window compatibility flag written by both the legacy and durable commit protocols.");
        builder.Ignore(x => x.Completed);
        builder.Property(x => x.State)
            .HasColumnName("state")
            .IsRequired()
            .HasMaxLength(32)
            .HasComment("Durable upload lifecycle state: open, committing, or completed.");
        builder.Property(x => x.CommitId)
            .HasColumnName("commit_id")
            .HasMaxLength(64)
            .HasComment("Immutable unique commit ownership identifier created by Tx1.");
        builder.Property(x => x.CommitChecksum)
            .HasColumnName("commit_checksum")
            .HasMaxLength(71)
            .HasComment("Immutable expected canonical SHA-256 evidence; null when final storage must compute it.");
        builder.Property(x => x.CommittingAtUtc)
            .HasColumnName("committing_at_utc")
            .HasComment("UTC timestamp when Tx1 durably moved the upload session to committing.");
        builder.Property(x => x.StorageActionStartedAtUtc)
            .HasColumnName("storage_action_started_at_utc")
            .HasComment("Durable UTC marker written before any storage action that may establish final bytes.");
        builder.Property(x => x.RecoveryAttemptCount)
            .HasColumnName("recovery_attempt_count")
            .HasComment("Storage recovery failure count for the immutable commit intent.");
        builder.Property(x => x.NextRecoveryAtUtc)
            .HasColumnName("next_recovery_at_utc")
            .HasComment("UTC timestamp before which recovery must not retry this commit intent.");
        builder.Property(x => x.LastRecoveryErrorCode)
            .HasColumnName("last_recovery_error_code")
            .HasMaxLength(64)
            .HasComment("Stable non-sensitive diagnostic code from the latest recovery attempt.");
        builder.Property(x => x.RecoveryTerminalAtUtc)
            .HasColumnName("recovery_terminal_at_utc")
            .HasComment("UTC timestamp when automatic recovery stopped after a permanent evidence failure.");
        builder.Property(x => x.ConcurrencyVersion)
            .HasColumnName("concurrency_version")
            .IsConcurrencyToken()
            .HasComment("Application-managed optimistic concurrency version for upload state transitions.");
        builder.Property(x => x.ExecutionOwnerId)
            .HasColumnName("execution_owner_id")
            .HasMaxLength(64)
            .HasComment("Short-lived durable owner authorized to execute storage I/O for the commit intent.");
        builder.Property(x => x.ExecutionLeaseUntilUtc)
            .HasColumnName("execution_lease_until_utc")
            .HasComment("UTC expiration timestamp of the current storage execution lease.");
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
            "(completed AND state = 'open' AND commit_id IS NULL AND committing_at_utc IS NULL AND storage_action_started_at_utc IS NULL AND completed_at_utc IS NOT NULL AND recovery_attempt_count = 0 AND next_recovery_at_utc IS NULL AND last_recovery_error_code IS NULL AND recovery_terminal_at_utc IS NULL AND execution_owner_id IS NULL AND execution_lease_until_utc IS NULL) OR " +
            "(NOT completed AND state = 'open' AND commit_id IS NULL AND committing_at_utc IS NULL AND storage_action_started_at_utc IS NULL AND completed_at_utc IS NULL AND recovery_attempt_count = 0 AND next_recovery_at_utc IS NULL AND last_recovery_error_code IS NULL AND recovery_terminal_at_utc IS NULL AND execution_owner_id IS NULL AND execution_lease_until_utc IS NULL) OR " +
            "(state = 'committing' AND commit_id IS NOT NULL AND committing_at_utc IS NOT NULL AND completed_at_utc IS NULL AND recovery_attempt_count >= 0 AND (recovery_terminal_at_utc IS NULL OR (next_recovery_at_utc IS NULL AND last_recovery_error_code IS NOT NULL AND execution_owner_id IS NULL AND execution_lease_until_utc IS NULL)) AND ((execution_owner_id IS NULL AND execution_lease_until_utc IS NULL) OR (execution_owner_id IS NOT NULL AND execution_lease_until_utc IS NOT NULL))) OR " +
            "(completed AND state = 'completed' AND commit_id IS NOT NULL AND committing_at_utc IS NOT NULL AND completed_at_utc IS NOT NULL AND recovery_attempt_count >= 0 AND next_recovery_at_utc IS NULL AND last_recovery_error_code IS NULL AND recovery_terminal_at_utc IS NULL AND execution_owner_id IS NULL AND execution_lease_until_utc IS NULL)"));
    }
}
