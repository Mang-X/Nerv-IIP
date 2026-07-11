using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.DeviceControlChannelBindingAggregate;

namespace Nerv.IIP.Business.IndustrialTelemetry.Infrastructure.EntityConfigurations;

public sealed class DeviceControlChannelBindingEntityTypeConfiguration : IEntityTypeConfiguration<DeviceControlChannelBinding>
{
    public void Configure(EntityTypeBuilder<DeviceControlChannelBinding> builder)
    {
        builder.ToTable("device_control_channel_bindings", table => table.HasComment("BusinessIndustrialTelemetry device control channel routing binding (device to connector host/instance)."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).UseGuidVersion7ValueGenerator().HasComment("Device control channel binding identifier.");
        builder.Property(x => x.OrganizationId).IsRequired().HasMaxLength(100).HasColumnName("organization_id").HasComment("Owning organization identifier.");
        builder.Property(x => x.EnvironmentId).IsRequired().HasMaxLength(100).HasColumnName("environment_id").HasComment("Owning environment identifier.");
        builder.Property(x => x.DeviceAssetId).IsRequired().HasMaxLength(150).HasColumnName("device_asset_id").HasComment("Referenced MasterData device asset identifier bound to a control channel.");
        builder.Property(x => x.ConnectorHostId).IsRequired().HasMaxLength(128).HasColumnName("connector_host_id").HasComment("Connector host that owns the device's control channel.");
        builder.Property(x => x.InstanceKey).IsRequired().HasMaxLength(150).HasColumnName("instance_key").HasComment("Connector instance key routed by the Ops operation task for this device.");
        builder.Property(x => x.IsActive).HasColumnName("is_active").HasComment("Whether the control channel binding is active and usable for dispatch.");
        builder.Property(x => x.DisabledReason).HasMaxLength(300).HasColumnName("disabled_reason").HasComment("Reason captured when the binding was disabled, retained for audit.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasComment("UTC time when the binding was created.");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").HasComment("UTC time when the binding was last updated.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.DeviceAssetId }).IsUnique();
    }
}
