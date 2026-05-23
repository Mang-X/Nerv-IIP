using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockCountAdjustmentAggregate;

namespace Nerv.IIP.Business.Inventory.Infrastructure.EntityConfigurations;

public sealed class StockCountAdjustmentEntityTypeConfiguration : IEntityTypeConfiguration<StockCountAdjustment>
{
    public void Configure(EntityTypeBuilder<StockCountAdjustment> builder)
    {
        builder.ToTable("stock_count_adjustments", tableBuilder =>
            tableBuilder.HasComment("Inventory stock count adjustment facts generated from confirmed count variances."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Stock count adjustment aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id that owns the count adjustment.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id where the count adjustment was confirmed.");
        builder.Property(x => x.CountTaskCode).HasColumnName("count_task_code").IsRequired().HasMaxLength(100).HasComment("Business count task code that produced the adjustment.");
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").IsRequired().HasMaxLength(128).HasComment("Idempotency key supplied when confirming the count variance; expected to be a UUID-like or producer-stable token.");
        builder.Property(x => x.MovementId).HasColumnName("movement_id").IsRequired().HasMaxLength(150).HasComment("Stock movement id generated for the count variance.");
        builder.Property(x => x.SkuCode).HasColumnName("sku_code").IsRequired().HasMaxLength(100).HasComment("MasterData SKU code.");
        builder.Property(x => x.UomCode).HasColumnName("uom_code").IsRequired().HasMaxLength(50).HasComment("MasterData unit of measure code for counted quantity.");
        builder.Property(x => x.SiteCode).HasColumnName("site_code").IsRequired().HasMaxLength(100).HasComment("MasterData site code.");
        builder.Property(x => x.LocationCode).HasColumnName("location_code").IsRequired().HasMaxLength(100).HasComment("Inventory stock location code.");
        builder.Property(x => x.LotNo).HasColumnName("lot_no").HasMaxLength(100).HasComment("Optional lot or batch number dimension.");
        builder.Property(x => x.SerialNo).HasColumnName("serial_no").HasMaxLength(100).HasComment("Optional serial number dimension.");
        builder.Property(x => x.QualityStatus).HasColumnName("quality_status").IsRequired().HasMaxLength(50).HasComment("Quality status carried by adjusted stock.");
        builder.Property(x => x.OwnerType).HasColumnName("owner_type").IsRequired().HasMaxLength(50).HasComment("Stock ownership type such as company, customer or supplier.");
        builder.Property(x => x.OwnerId).HasColumnName("owner_id").HasMaxLength(100).HasComment("Optional public owner reference id.");
        builder.Property(x => x.CountedQuantity).HasColumnName("counted_quantity").IsRequired().HasPrecision(18, 6).HasComment("Confirmed physical counted quantity.");
        builder.Property(x => x.VarianceQuantity).HasColumnName("variance_quantity").IsRequired().HasPrecision(18, 6).HasComment("Confirmed variance quantity against ledger on-hand.");
        builder.Property(x => x.ConfirmedAtUtc).HasColumnName("confirmed_at_utc").IsRequired().HasComment("UTC time when the count adjustment was confirmed.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.CountTaskCode, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.SkuCode, x.SiteCode, x.LocationCode, x.ConfirmedAtUtc });
    }
}
