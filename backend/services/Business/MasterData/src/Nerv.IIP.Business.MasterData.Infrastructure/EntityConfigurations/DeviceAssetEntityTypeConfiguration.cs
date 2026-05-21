using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.DeviceAssetAggregate;

namespace Nerv.IIP.Business.MasterData.Infrastructure.EntityConfigurations;

public sealed class DeviceAssetEntityTypeConfiguration : IEntityTypeConfiguration<DeviceAsset>
{
    public void Configure(EntityTypeBuilder<DeviceAsset> builder)
    {
        builder.ToTable("device_assets", tableBuilder =>
            tableBuilder.HasComment("Business master data device assets assigned to production lines and work centers."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Device asset aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id that owns the device asset.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id where the device asset is valid.");
        builder.Property(x => x.Code).HasColumnName("code").IsRequired().HasMaxLength(100).HasComment("Business unique device asset code.");
        builder.Property(x => x.Model).HasColumnName("model").IsRequired().HasMaxLength(160).HasComment("Device model or equipment type.");
        builder.Property(x => x.LineCode).HasColumnName("line_code").IsRequired().HasMaxLength(100).HasComment("Production line code where the device asset is installed.");
        builder.Property(x => x.WorkCenterCode).HasColumnName("work_center_code").IsRequired().HasMaxLength(100).HasComment("Work center code where the device asset is assigned.");
        builder.Property(x => x.Disabled).HasColumnName("disabled").IsRequired().HasComment("Disabled flag that hides the device asset from active use.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC time when the device asset was created.");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired().HasComment("UTC time when the device asset was last updated.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.Code }).IsUnique();
        builder.HasIndex(x => new { x.WorkCenterCode, x.Disabled });
    }
}
