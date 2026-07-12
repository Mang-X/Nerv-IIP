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
        builder.Property(x => x.ShelfLifeDays).HasColumnName("shelf_life_days").HasComment("Optional SKU default shelf life in calendar days used to derive batch expiry dates.");
        builder.Property(x => x.NearExpiryThresholdDays).HasColumnName("near_expiry_threshold_days").HasComment("Optional SKU near-expiry threshold in calendar days used by Inventory expiry alerts.");
        builder.Property(x => x.StorageConditionCode).HasColumnName("storage_condition_code").IsRequired().HasMaxLength(100).HasComment("Storage condition code such as ambient, cold or hazardous.");
        builder.Property(x => x.DefaultBarcodeRuleCode).HasColumnName("default_barcode_rule_code").IsRequired().HasMaxLength(100).HasComment("Default barcode rule code consumed by BarcodeLabel.");
        builder.Property(x => x.QualityRequired).HasColumnName("quality_required").IsRequired().HasComment("Flag that indicates Quality must inspect or release this SKU before unrestricted use.");
        builder.Property(x => x.ProcurementType).HasColumnName("procurement_type").IsRequired().HasMaxLength(50).HasComment("Default procurement type such as make, buy or subcontract for planning snapshots.");
        builder.Property(x => x.MrpType).HasColumnName("mrp_type").IsRequired().HasMaxLength(50).HasComment("Default MRP type consumed as shared SKU planning master data.");
        builder.Property(x => x.LotSizingPolicy).HasColumnName("lot_sizing_policy").IsRequired().HasMaxLength(80).HasComment("Default lot sizing policy used by planning services when no site-specific override exists.");
        builder.Property(x => x.MinimumLotSize).HasColumnName("minimum_lot_size").HasPrecision(24, 6).HasComment("Optional minimum planned lot size in the SKU base unit of measure.");
        builder.Property(x => x.MaximumLotSize).HasColumnName("maximum_lot_size").HasPrecision(24, 6).HasComment("Optional maximum planned lot size in the SKU base unit of measure.");
        builder.Property(x => x.LotSizeMultiple).HasColumnName("lot_size_multiple").HasPrecision(24, 6).HasComment("Optional planned lot size multiple in the SKU base unit of measure.");
        builder.Property(x => x.SafetyStockQuantity).HasColumnName("safety_stock_quantity").HasPrecision(24, 6).HasComment("Optional default safety stock quantity in the SKU base unit of measure.");
        builder.Property(x => x.ReorderPointQuantity).HasColumnName("reorder_point_quantity").HasPrecision(24, 6).HasComment("Optional default reorder point quantity in the SKU base unit of measure.");
        builder.Property(x => x.PlannedDeliveryTimeDays).HasColumnName("planned_delivery_time_days").HasComment("Optional planned delivery lead time in calendar days for externally procured SKU planning.");
        builder.Property(x => x.InHouseProductionTimeDays).HasColumnName("in_house_production_time_days").HasComment("Optional in-house production lead time in calendar days for manufactured SKU planning.");
        builder.Property(x => x.GoodsReceiptProcessingTimeDays).HasColumnName("goods_receipt_processing_time_days").HasComment("Optional goods receipt processing time in calendar days for planning snapshots.");
        builder.Property(x => x.AbcClass).HasColumnName("abc_class").IsRequired().HasMaxLength(20).HasComment("Optional ABC planning classification code.");
        builder.Property(x => x.LifecycleStatus).HasColumnName("lifecycle_status").IsRequired().HasMaxLength(30).HasDefaultValue("active").HasComment("SKU lifecycle status such as draft, active, blocked or obsolete.");
        builder.Property(x => x.PurchasingEnabled).HasColumnName("purchasing_enabled").IsRequired().HasDefaultValue(true).HasComment("Whether purchasing documents may use this SKU by default.");
        builder.Property(x => x.ManufacturingEnabled).HasColumnName("manufacturing_enabled").IsRequired().HasDefaultValue(true).HasComment("Whether manufacturing and MES processes may use this SKU by default.");
        builder.Property(x => x.SalesEnabled).HasColumnName("sales_enabled").IsRequired().HasDefaultValue(true).HasComment("Whether sales documents may use this SKU by default.");
        builder.Ignore(x => x.ComplianceTags);
        builder.Property(x => x.Disabled).HasColumnName("disabled").IsRequired().HasComment("Disabled flag that hides the SKU from active use.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC time when the SKU was created.");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired().HasComment("UTC time when the SKU was last updated.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.Code }).IsUnique();
        builder.HasIndex(x => new { x.Category, x.Disabled });
    }
}
