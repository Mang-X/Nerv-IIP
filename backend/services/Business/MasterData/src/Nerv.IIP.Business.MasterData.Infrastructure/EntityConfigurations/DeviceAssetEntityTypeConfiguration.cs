using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.DeviceAssetAggregate;
using Microsoft.EntityFrameworkCore.Metadata;

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
        builder.Property(x => x.SiteCode).HasColumnName("site_code").IsRequired().HasMaxLength(100).HasComment("Site code where the device asset is installed.");
        builder.Property(x => x.WorkshopCode).HasColumnName("workshop_code").IsRequired().HasMaxLength(100).HasComment("Workshop code where the device asset is installed.");
        builder.Property(x => x.StationCode).HasColumnName("station_code").IsRequired().HasMaxLength(100).HasComment("Station code or local position inside the production line.");
        builder.Property(x => x.AssetClassCode).HasColumnName("asset_class_code").IsRequired().HasMaxLength(100).HasComment("Asset class code used for equipment grouping and maintenance policy.");
        builder.Property(x => x.Manufacturer).HasColumnName("manufacturer").IsRequired().HasMaxLength(160).HasComment("Equipment manufacturer name.");
        builder.Property(x => x.SerialNo).HasColumnName("serial_no").IsRequired().HasMaxLength(160).HasComment("Manufacturer serial number or asset serial reference.");
        builder.Property(x => x.PurchaseDate).HasColumnName("purchase_date").HasComment("Business date when the asset was purchased.");
        builder.Property(x => x.PurchaseCost).HasColumnName("purchase_cost").HasPrecision(18, 2).HasComment("Original purchase cost amount in purchase_currency_code.");
        builder.Property(x => x.PurchaseCurrencyCode).HasColumnName("purchase_currency_code").IsRequired().HasMaxLength(20).HasComment("Currency code for purchase_cost.");
        builder.Property(x => x.WarrantyExpiresOn).HasColumnName("warranty_expires_on").HasComment("Business date when supplier warranty expires.");
        builder.Property(x => x.SupplierPartnerCode).HasColumnName("supplier_partner_code").IsRequired().HasMaxLength(100).HasComment("BusinessPartner code for the equipment supplier.");
        builder.Property(x => x.ParentDeviceId).HasColumnName("parent_device_id").IsRequired().HasMaxLength(100).HasComment("Parent device asset public id when this asset is a child component or sub-asset.");
        builder.Property(x => x.RetiredOn).HasColumnName("retired_on").HasComment("Business date when the asset was retired from active use.");
        builder.Ignore(x => x.Retired);
        builder.Property(x => x.MinimumCapacity).HasColumnName("minimum_capacity").HasPrecision(18, 6).HasComment("Minimum static processing capacity in capacity_uom_code.");
        builder.Property(x => x.MaximumCapacity).HasColumnName("maximum_capacity").HasPrecision(18, 6).HasComment("Maximum static processing capacity in capacity_uom_code.");
        builder.Property(x => x.CapacityUomCode).HasColumnName("capacity_uom_code").IsRequired().HasMaxLength(50).HasComment("Unit of measure code for static equipment capacity.");
        builder.Property(x => x.Criticality).HasColumnName("criticality").IsRequired().HasMaxLength(80).HasComment("Maintenance and planning criticality code.");
        builder.Property(x => x.Maintainable).HasColumnName("maintainable").IsRequired().HasComment("Flag that indicates Maintenance can create work orders for this asset.");
        builder.Property(x => x.TelemetryEnabled).HasColumnName("telemetry_enabled").IsRequired().HasComment("Flag that indicates IndustrialTelemetry may map tags to this asset.");
        builder.Ignore(x => x.ExternalReferences);
        builder.Property(x => x.Disabled).HasColumnName("disabled").IsRequired().HasComment("Disabled flag that hides the device asset from active use.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC time when the device asset was created.");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired().HasComment("UTC time when the device asset was last updated.");
        builder.HasMany(x => x.Components).WithOne().HasForeignKey("DeviceAssetId").OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(DeviceAsset.Components))!.SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.Code }).IsUnique();
        builder.HasIndex(x => new { x.SiteCode, x.WorkshopCode, x.LineCode, x.Disabled });
        builder.HasIndex(x => new { x.WorkCenterCode, x.Disabled });
    }
}

public sealed class DeviceAssetComponentEntityTypeConfiguration : IEntityTypeConfiguration<DeviceAssetComponent>
{
    public void Configure(EntityTypeBuilder<DeviceAssetComponent> builder)
    {
        builder.ToTable("device_asset_components", tableBuilder =>
            tableBuilder.HasComment("Child component rows owned by a business master data device asset."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Device asset component row id.");
        builder.Property<DeviceAssetId>("DeviceAssetId").HasColumnName("device_asset_id").IsRequired().HasComment("Owning device asset id.");
        builder.Property(x => x.ComponentCode).HasColumnName("component_code").IsRequired().HasMaxLength(100).HasComment("Component code within the parent device asset.");
        builder.Property(x => x.ComponentName).HasColumnName("component_name").IsRequired().HasMaxLength(200).HasComment("Operator-readable component name.");
        builder.Property(x => x.Quantity).HasColumnName("quantity").HasPrecision(18, 6).HasComment("Component quantity installed in the parent asset.");
        builder.Property(x => x.Critical).HasColumnName("critical").IsRequired().HasComment("Whether the component is critical for maintenance decisions.");
        builder.HasIndex("DeviceAssetId", nameof(DeviceAssetComponent.ComponentCode)).IsUnique();
    }
}
