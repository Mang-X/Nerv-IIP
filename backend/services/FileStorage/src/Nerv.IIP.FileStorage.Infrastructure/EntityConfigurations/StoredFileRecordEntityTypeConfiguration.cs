using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nerv.IIP.FileStorage.Infrastructure.Records;

namespace Nerv.IIP.FileStorage.Infrastructure.EntityConfigurations;

public sealed class StoredFileRecordEntityTypeConfiguration : IEntityTypeConfiguration<StoredFileRecord>
{
    public void Configure(EntityTypeBuilder<StoredFileRecord> builder)
    {
        builder.ToTable("stored_files", table => table.HasComment("FileStorage completed file metadata for internally stored objects."));
        builder.HasKey(x => x.FileId);

        builder.Property(x => x.FileId)
            .HasColumnName("file_id")
            .ValueGeneratedNever()
            .HasMaxLength(64)
            .HasComment("Stable file identifier returned by the public FileStorage API.");
        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .ValueGeneratedNever()
            .IsRequired()
            .HasMaxLength(128)
            .HasComment("Organization identifier that owns the file.");
        builder.Property(x => x.EnvironmentId)
            .HasColumnName("environment_id")
            .ValueGeneratedNever()
            .IsRequired()
            .HasMaxLength(128)
            .HasComment("Environment identifier that scopes the file.");
        builder.Property(x => x.OwnerService)
            .HasColumnName("owner_service")
            .IsRequired()
            .HasMaxLength(128)
            .HasComment("Service that owns the file metadata.");
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
            .HasComment("Purpose policy key used to validate and route file usage.");
        builder.Property(x => x.FileName)
            .HasColumnName("file_name")
            .IsRequired()
            .HasMaxLength(512)
            .HasComment("Original file name supplied by the caller.");
        builder.Property(x => x.ContentType)
            .HasColumnName("content_type")
            .IsRequired()
            .HasMaxLength(256)
            .HasComment("Media type declared for the stored file.");
        builder.Property(x => x.SizeBytes)
            .HasColumnName("size_bytes")
            .HasComment("Stored object size in bytes.");
        builder.Property(x => x.Checksum)
            .HasColumnName("checksum")
            .HasMaxLength(256)
            .HasComment("Optional caller-provided checksum for integrity tracking.");
        builder.Property(x => x.ObjectKey)
            .HasColumnName("object_key")
            .ValueGeneratedNever()
            .IsRequired()
            .HasMaxLength(1024)
            .HasComment("Internal object storage key; never exposed through public FileStorage responses.");
        builder.Property(x => x.ScanStatus)
            .HasColumnName("scan_status")
            .IsRequired()
            .HasMaxLength(32)
            .HasComment("Malware or content scan status for the stored file.");
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .IsRequired()
            .HasMaxLength(32)
            .HasComment("File lifecycle status visible through metadata responses.");
        builder.Property(x => x.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasComment("UTC timestamp when the file metadata was created.");
        builder.Property(x => x.CompletedAtUtc)
            .HasColumnName("completed_at_utc")
            .HasComment("UTC timestamp when the file became available.");

        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.OwnerService, x.OwnerType, x.OwnerId });
        builder.HasIndex(x => x.ObjectKey).IsUnique();
    }
}
