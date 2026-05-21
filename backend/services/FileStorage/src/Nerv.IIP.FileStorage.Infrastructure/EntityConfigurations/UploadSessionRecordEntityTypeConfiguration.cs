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
        builder.Property(x => x.Completed)
            .HasColumnName("completed")
            .HasComment("Whether the upload session has been completed.");
        builder.Property(x => x.CompletedAtUtc)
            .HasColumnName("completed_at_utc")
            .HasComment("UTC timestamp when the upload session was completed.");

        builder.HasIndex(x => x.FileId).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.ExpiresAtUtc });
        builder.HasIndex(x => x.ObjectKey).IsUnique();
    }
}
