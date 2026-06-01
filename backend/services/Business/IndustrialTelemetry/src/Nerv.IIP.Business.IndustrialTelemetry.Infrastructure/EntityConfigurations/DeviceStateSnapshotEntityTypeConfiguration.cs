using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.DeviceStateSnapshotAggregate;

namespace Nerv.IIP.Business.IndustrialTelemetry.Infrastructure.EntityConfigurations;

public sealed class DeviceStateSnapshotEntityTypeConfiguration : IEntityTypeConfiguration<DeviceStateSnapshot>
{
    public void Configure(EntityTypeBuilder<DeviceStateSnapshot> builder)
    {
        builder.ToTable("device_state_snapshots", table => table.HasComment("BusinessIndustrialTelemetry controlled device state snapshots."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).UseGuidVersion7ValueGenerator().HasComment("Device state snapshot identifier.");
        builder.Property(x => x.OrganizationId).IsRequired().HasMaxLength(100).HasColumnName("organization_id").HasComment("Owning organization identifier.");
        builder.Property(x => x.EnvironmentId).IsRequired().HasMaxLength(100).HasColumnName("environment_id").HasComment("Owning environment identifier.");
        builder.Property(x => x.DeviceAssetId).IsRequired().HasMaxLength(150).HasColumnName("device_asset_id").HasComment("Referenced MasterData device asset identifier.");
        builder.Property(x => x.State).IsRequired().HasMaxLength(80).HasColumnName("state").HasComment("Normalized device state fact.");
        builder.Property(x => x.OccurredAtUtc).HasColumnName("occurred_at_utc").HasComment("UTC time when the device state was observed.");
        builder.Property(x => x.SourceSequence).IsRequired().HasMaxLength(150).HasColumnName("source_sequence").HasComment("Source sequence used for idempotent state ingestion.");
        builder.Property(x => x.SourceSystem).HasMaxLength(100).HasColumnName("source_system").HasComment("External source system that observed the device state.");
        builder.Property(x => x.SourceConnector).HasMaxLength(150).HasColumnName("source_connector").HasComment("Connector instance or adapter that delivered the device state.");
        builder.Property(x => x.RecordedAtUtc).HasColumnName("recorded_at_utc").HasComment("UTC time when the state snapshot was recorded.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.SourceSystem, x.SourceConnector, x.DeviceAssetId, x.SourceSequence })
            .IsUnique()
            .AreNullsDistinct(false);
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.DeviceAssetId, x.OccurredAtUtc });
    }
}
