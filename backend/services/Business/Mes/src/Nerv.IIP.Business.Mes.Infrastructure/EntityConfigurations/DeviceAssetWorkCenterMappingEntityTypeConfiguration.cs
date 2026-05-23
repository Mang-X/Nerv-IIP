using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ScheduleAggregate;

namespace Nerv.IIP.Business.Mes.Infrastructure.EntityConfigurations;

public sealed class DeviceAssetWorkCenterMappingEntityTypeConfiguration : IEntityTypeConfiguration<DeviceAssetWorkCenterMapping>
{
    public void Configure(EntityTypeBuilder<DeviceAssetWorkCenterMapping> builder)
    {
        builder.ToTable("device_asset_work_center_mappings", tableBuilder =>
            tableBuilder.HasComment("MES local mapping from Maintenance device asset public ids to MasterData work center public ids."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Device asset work center mapping aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").HasMaxLength(100).HasComment("Organization tenant id; null means the mapping is global.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").HasMaxLength(100).HasComment("Environment id; null means the mapping is global.");
        builder.Property(x => x.DeviceAssetId).HasColumnName("device_asset_id").IsRequired().HasMaxLength(100).HasComment("Maintenance device asset public id.");
        builder.Property(x => x.WorkCenterId).HasColumnName("work_center_id").IsRequired().HasMaxLength(100).HasComment("MasterData work center public id used by MES scheduling.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.DeviceAssetId }).IsUnique();
    }
}
