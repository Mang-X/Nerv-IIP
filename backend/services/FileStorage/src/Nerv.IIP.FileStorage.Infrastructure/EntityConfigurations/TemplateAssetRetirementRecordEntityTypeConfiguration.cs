using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nerv.IIP.FileStorage.Infrastructure.Records;

namespace Nerv.IIP.FileStorage.Infrastructure.EntityConfigurations;

public sealed class TemplateAssetRetirementRecordEntityTypeConfiguration : IEntityTypeConfiguration<TemplateAssetRetirementRecord>
{
    public void Configure(EntityTypeBuilder<TemplateAssetRetirementRecord> builder)
    {
        builder.ToTable("template_asset_retirements", table => table.HasComment("Durable label-template retirement receipts and frozen replay inputs."));
        builder.HasKey(x => x.DecisionId);
        builder.Property(x => x.DecisionId).HasColumnName("decision_id").IsRequired().HasMaxLength(64).ValueGeneratedNever().HasComment("Upstream retirement decision and audit reference.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(128).ValueGeneratedNever().HasComment("Owning organization.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(128).ValueGeneratedNever().HasComment("Owning environment.");
        builder.Property(x => x.FileId).HasColumnName("file_id").IsRequired().HasMaxLength(64).ValueGeneratedNever().HasComment("Retired file identity; retained after file metadata removal.");
        builder.Property(x => x.Checksum).HasColumnName("checksum").IsRequired().HasMaxLength(256).HasComment("Frozen canonical SHA-256 of the retired asset.");
        builder.Property(x => x.OwnerService).HasColumnName("owner_service").IsRequired().HasMaxLength(128).HasComment("Frozen owning service.");
        builder.Property(x => x.OwnerType).HasColumnName("owner_type").IsRequired().HasMaxLength(128).HasComment("Frozen owner resource type.");
        builder.Property(x => x.OwnerId).HasColumnName("owner_id").IsRequired().HasMaxLength(128).ValueGeneratedNever().HasComment("Frozen owner resource identity.");
        builder.Property(x => x.Purpose).HasColumnName("purpose").IsRequired().HasMaxLength(128).HasComment("Authorized file purpose.");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasMaxLength(32).HasComment("Physical lifecycle state; acceptance alone is physical-hold.");
        builder.Property(x => x.SizeBytes).HasColumnName("size_bytes").HasComment("Business quota bytes released by acceptance.");
        builder.Property(x => x.AcceptedAtUtc).HasColumnName("accepted_at_utc").HasComment("UTC acceptance, physical hold and quota release timestamp.");
        builder.Property(x => x.ReplayPolicyVersion).HasColumnName("replay_policy_version").HasComment("Version of the frozen horizon policy.");
        builder.Property(x => x.ClientWindowSeconds).HasColumnName("client_window_seconds").HasComment("Frozen upstream client replay request, in seconds.");
        builder.Property(x => x.BarcodeLeaseSeconds).HasColumnName("barcode_lease_seconds").HasComment("Frozen BarcodeLabel retirement lease, in seconds.");
        builder.Property(x => x.BarcodeMaxBackoffSeconds).HasColumnName("barcode_max_backoff_seconds").HasComment("Frozen BarcodeLabel retirement maximum backoff, in seconds.");
        builder.Property(x => x.PhysicalGraceSeconds).HasColumnName("physical_grace_seconds").HasComment("Frozen physical grace, in seconds.");
        builder.Property(x => x.GcIntervalSeconds).HasColumnName("gc_interval_seconds").HasComment("Frozen FileStorage collector interval, in seconds.");
        builder.Property(x => x.StorageLeaseSeconds).HasColumnName("storage_lease_seconds").HasComment("Frozen FileStorage retirement executor lease, in seconds.");
        builder.Property(x => x.StorageMaxBackoffSeconds).HasColumnName("storage_max_backoff_seconds").HasComment("Frozen FileStorage retirement executor maximum backoff, in seconds.");
        builder.Property(x => x.ReplayHorizonSeconds).HasColumnName("replay_horizon_seconds").HasComment("Frozen shared replay duration H; terminal deadline is assigned by physical completion.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.FileId }).IsUnique();
        // No FK: the receipt must outlive stored_files and must never be cascade-deleted by legacy GC.
    }
}
