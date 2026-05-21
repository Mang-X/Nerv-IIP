using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nerv.IIP.FileStorage.Infrastructure.Records;

namespace Nerv.IIP.FileStorage.Infrastructure.EntityConfigurations;

public sealed class DownloadGrantRecordEntityTypeConfiguration : IEntityTypeConfiguration<DownloadGrantRecord>
{
    public void Configure(EntityTypeBuilder<DownloadGrantRecord> builder)
    {
        builder.ToTable("download_grants", table => table.HasComment("FileStorage short-lived download grant metadata for server-proxy downloads."));
        builder.HasKey(x => x.DownloadGrantId);

        builder.Property(x => x.DownloadGrantId)
            .HasColumnName("download_grant_id")
            .ValueGeneratedNever()
            .HasMaxLength(64)
            .HasComment("Stable download grant identifier used to serve a short-lived download.");
        builder.Property(x => x.FileId)
            .HasColumnName("file_id")
            .ValueGeneratedNever()
            .IsRequired()
            .HasMaxLength(64)
            .HasComment("File identifier the download grant authorizes.");
        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .ValueGeneratedNever()
            .IsRequired()
            .HasMaxLength(128)
            .HasComment("Organization identifier that owns the download grant.");
        builder.Property(x => x.EnvironmentId)
            .HasColumnName("environment_id")
            .ValueGeneratedNever()
            .IsRequired()
            .HasMaxLength(128)
            .HasComment("Environment identifier that scopes the download grant.");
        builder.Property(x => x.Provider)
            .HasColumnName("provider")
            .IsRequired()
            .HasMaxLength(64)
            .HasComment("Download provider used for this grant.");
        builder.Property(x => x.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasComment("UTC timestamp when the download grant was created.");
        builder.Property(x => x.ExpiresAtUtc)
            .HasColumnName("expires_at_utc")
            .HasComment("UTC timestamp when the download grant expires.");

        builder.HasOne<StoredFileRecord>()
            .WithMany()
            .HasForeignKey(x => x.FileId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.FileId, x.ExpiresAtUtc });
    }
}
