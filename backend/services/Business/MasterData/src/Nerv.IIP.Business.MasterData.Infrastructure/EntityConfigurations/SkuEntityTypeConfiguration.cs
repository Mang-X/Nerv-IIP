using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SkuAggregate;

namespace Nerv.IIP.Business.MasterData.Infrastructure.EntityConfigurations;

public sealed class SkuEntityTypeConfiguration : IEntityTypeConfiguration<Sku>
{
    public void Configure(EntityTypeBuilder<Sku> builder)
    {
        builder.ToTable("skus", tableBuilder =>
            tableBuilder.HasComment("Business master data stock keeping units used for material and product identification."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("SKU aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id that owns the SKU.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id where the SKU is valid.");
        builder.Property(x => x.Code).HasColumnName("code").IsRequired().HasMaxLength(100).HasComment("Business unique SKU code.");
        builder.Property(x => x.Name).HasColumnName("name").IsRequired().HasMaxLength(200).HasComment("SKU display name.");
        builder.Property(x => x.Unit).HasColumnName("unit").IsRequired().HasMaxLength(50).HasComment("Default inventory or production unit of measure.");
        builder.Property(x => x.BaseUomCode).HasColumnName("base_uom_code").IsRequired().HasMaxLength(50).HasComment("Base unit of measure code used for material master identity.");
        builder.Property(x => x.InventoryUomCode).HasColumnName("inventory_uom_code").IsRequired().HasMaxLength(50).HasComment("Default inventory unit of measure code.");
        builder.Property(x => x.PurchaseUomCode).HasColumnName("purchase_uom_code").IsRequired().HasMaxLength(50).HasComment("Default purchasing unit of measure code.");
        builder.Property(x => x.SalesUomCode).HasColumnName("sales_uom_code").IsRequired().HasMaxLength(50).HasComment("Default sales unit of measure code.");
        builder.Property(x => x.ManufacturingUomCode).HasColumnName("manufacturing_uom_code").IsRequired().HasMaxLength(50).HasComment("Default manufacturing unit of measure code.");
        builder.Property(x => x.Category).HasColumnName("category").IsRequired().HasMaxLength(100).HasComment("SKU category for list filtering and planning.");
        builder.Property(x => x.MaterialType).HasColumnName("material_type").IsRequired().HasMaxLength(100).HasComment("Material type such as raw material, finished good, packaging or service.");
        builder.Property(x => x.BatchTrackingPolicy).HasColumnName("batch_tracking_policy").IsRequired().HasMaxLength(100).HasComment("Policy that states whether lot, heat, date code or expiry tracking is required.");
        builder.Property(x => x.SerialTrackingPolicy).HasColumnName("serial_tracking_policy").IsRequired().HasMaxLength(100).HasComment("Policy that states whether serial number tracking is required.");
        builder.Property(x => x.ShelfLifePolicyCode).HasColumnName("shelf_life_policy_code").IsRequired().HasMaxLength(100).HasComment("Shelf life policy code used by Inventory and Quality.");
        builder.Property(x => x.StorageConditionCode).HasColumnName("storage_condition_code").IsRequired().HasMaxLength(100).HasComment("Storage condition code such as ambient, cold or hazardous.");
        builder.Property(x => x.DefaultBarcodeRuleCode).HasColumnName("default_barcode_rule_code").IsRequired().HasMaxLength(100).HasComment("Default barcode rule code consumed by BarcodeLabel.");
        builder.Property(x => x.QualityRequired).HasColumnName("quality_required").IsRequired().HasComment("Flag that indicates Quality must inspect or release this SKU before unrestricted use.");
        builder.Ignore(x => x.ComplianceTags);
        builder.Property(x => x.Disabled).HasColumnName("disabled").IsRequired().HasComment("Disabled flag that hides the SKU from active use.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC time when the SKU was created.");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired().HasComment("UTC time when the SKU was last updated.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.Code }).IsUnique();
        builder.HasIndex(x => new { x.Category, x.Disabled });
    }
}
