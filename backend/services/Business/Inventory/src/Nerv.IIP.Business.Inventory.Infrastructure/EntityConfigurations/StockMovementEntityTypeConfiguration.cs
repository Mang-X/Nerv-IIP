using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockMovementAggregate;

namespace Nerv.IIP.Business.Inventory.Infrastructure.EntityConfigurations;

public sealed class StockMovementEntityTypeConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("stock_movements", tableBuilder =>
        {
            tableBuilder.HasComment("Append-only Inventory stock movement facts with source document and idempotency key.");
            InventoryCodeCheckConstraints.Add(tableBuilder, "ck_stock_movements_location_code_format", "location_code");
            InventoryCodeCheckConstraints.Add(tableBuilder, "ck_stock_movements_sku_code_format", "sku_code");
            InventoryCodeCheckConstraints.Add(tableBuilder, "ck_stock_movements_site_code_format", "site_code");
            tableBuilder.HasCheckConstraint("ck_stock_movements_quality_status", "quality_status in ('unrestricted','quality','restricted','blocked')");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Stock movement aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id that owns the movement.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id where the movement was posted.");
        builder.Property(x => x.MovementType).HasColumnName("movement_type").IsRequired().HasMaxLength(50).HasComment("Movement type: inbound, outbound, transfer, adjustment or count-adjustment.");
        builder.Property(x => x.SourceService).HasColumnName("source_service").IsRequired().HasMaxLength(100).HasComment("Source service that requested the movement.");
        builder.Property(x => x.SourceDocumentId).HasColumnName("source_document_id").IsRequired().HasMaxLength(150).HasComment("Source document id supplied by the producer.");
        builder.Property(x => x.SourceDocumentLineId).HasColumnName("source_document_line_id").HasMaxLength(150).HasComment("Optional source document line id supplied by the producer.");
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").IsRequired().HasMaxLength(128).HasComment("Idempotency key unique within organization, environment, source service and source document; expected to be a UUID-like or producer-stable token.");
        builder.Property(x => x.SkuCode).HasColumnName("sku_code").IsRequired().HasMaxLength(100).HasComment("MasterData SKU code.");
        builder.Property(x => x.UomCode).HasColumnName("uom_code").IsRequired().HasMaxLength(50).HasComment("MasterData unit of measure code for the quantity.");
        builder.Property(x => x.SiteCode).HasColumnName("site_code").IsRequired().HasMaxLength(100).HasComment("MasterData site code.");
        builder.Property(x => x.LocationCode).HasColumnName("location_code").IsRequired().HasMaxLength(100).HasComment("Inventory stock location code.");
        builder.Property(x => x.LotNo).HasColumnName("lot_no").HasMaxLength(100).HasComment("Optional lot or batch number dimension.");
        builder.Property(x => x.SerialNo).HasColumnName("serial_no").HasMaxLength(100).HasComment("Optional serial number dimension.");
        builder.Property(x => x.QualityStatus).HasColumnName("quality_status").IsRequired().HasMaxLength(50).HasComment("Canonical stock status: unrestricted, quality, restricted or blocked.");
        builder.Property(x => x.OwnerType).HasColumnName("owner_type").IsRequired().HasMaxLength(50).HasComment("Stock ownership type such as company, customer or supplier.");
        builder.Property(x => x.OwnerId).HasColumnName("owner_id").HasMaxLength(100).HasComment("Optional public owner reference id.");
        builder.Property(x => x.ProductionDate).HasColumnName("production_date").HasComment("Optional batch production date captured with the movement.");
        builder.Property(x => x.ExpiryDate).HasColumnName("expiry_date").HasComment("Optional batch expiry date carried by the movement.");
        builder.Property(x => x.Quantity).HasColumnName("quantity").IsRequired().HasPrecision(18, 6).HasComment("Signed movement quantity.");
        builder.Property(x => x.UnitCost).HasColumnName("unit_cost").HasPrecision(18, 6).HasComment("Optional movement unit cost used for moving-average valuation.");
        builder.Property(x => x.MovementAmount).HasColumnName("movement_amount").HasPrecision(18, 6).HasComment("Signed movement amount derived from quantity and unit cost.");
        builder.Property(x => x.PostedAtUtc).HasColumnName("posted_at_utc").IsRequired().HasComment("UTC time when the movement was posted.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.SourceService, x.SourceDocumentId, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.SkuCode, x.SiteCode, x.LocationCode, x.PostedAtUtc });
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.SiteCode, x.SkuCode, x.ExpiryDate });
    }
}
