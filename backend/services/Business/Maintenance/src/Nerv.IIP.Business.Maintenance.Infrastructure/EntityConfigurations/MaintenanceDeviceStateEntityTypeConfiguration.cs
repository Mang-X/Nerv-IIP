using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nerv.IIP.Business.Maintenance.Infrastructure.IntegrationEvents;

namespace Nerv.IIP.Business.Maintenance.Infrastructure.EntityConfigurations;

public sealed class MaintenanceDeviceStateEntityTypeConfiguration : IEntityTypeConfiguration<MaintenanceDeviceState>
{
    public void Configure(EntityTypeBuilder<MaintenanceDeviceState> builder)
    {
        builder.ToTable("maintenance_device_states", table => table.HasComment("Latest MasterData device status projected for Maintenance scheduling gates."));
        builder.HasKey(x => new { x.OrganizationId, x.EnvironmentId, x.DeviceAssetId });
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").HasMaxLength(100).IsRequired().HasComment("Organization boundary from the MasterData device event.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").HasMaxLength(100).IsRequired().HasComment("Environment boundary from the MasterData device event.");
        builder.Property(x => x.DeviceAssetId).HasColumnName("device_asset_id").HasMaxLength(150).IsRequired().HasComment("MasterData device asset code referenced by Maintenance plans.");
        builder.Property(x => x.Disabled).HasColumnName("disabled").HasComment("Whether the latest accepted MasterData event marks the device disabled.");
        builder.Property(x => x.ChangedAtUtc).HasColumnName("changed_at_utc").HasComment("UTC time of the latest accepted MasterData device status change.");
        builder.Property(x => x.SourceEventId).HasColumnName("source_event_id").HasMaxLength(256).IsRequired().HasComment("Latest accepted MasterData integration event identifier for traceability.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.Disabled });
    }
}
