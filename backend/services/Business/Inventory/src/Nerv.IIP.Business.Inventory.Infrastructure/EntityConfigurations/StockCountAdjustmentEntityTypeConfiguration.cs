using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockCountAdjustmentAggregate;

namespace Nerv.IIP.Business.Inventory.Infrastructure.EntityConfigurations;

public sealed class StockCountAdjustmentEntityTypeConfiguration : IEntityTypeConfiguration<StockCountAdjustment>
{
    public void Configure(EntityTypeBuilder<StockCountAdjustment> builder)
    {
        builder.ToTable("stock_count_adjustments", tableBuilder =>
        {
            tableBuilder.HasComment("Inventory stock count adjustment facts, including pending approval, posted and voided variances.");
            InventoryCodeCheckConstraints.Add(tableBuilder, "ck_stock_count_adjustments_location_code_format", "location_code");
            InventoryCodeCheckConstraints.Add(tableBuilder, "ck_stock_count_adjustments_sku_code_format", "sku_code");
            InventoryCodeCheckConstraints.Add(tableBuilder, "ck_stock_count_adjustments_site_code_format", "site_code");
            tableBuilder.HasCheckConstraint("ck_stock_count_adjustments_quality_status", "quality_status in ('unrestricted','quality','restricted','blocked')");
            tableBuilder.HasCheckConstraint("ck_stock_count_adjustments_status", "status in ('pending-approval','posted','voided')");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Stock count adjustment aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id that owns the count adjustment.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id where the count adjustment was confirmed.");
        builder.Property(x => x.CountTaskCode).HasColumnName("count_task_code").IsRequired().HasMaxLength(100).HasComment("Business count task code that produced the adjustment.");
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").IsRequired().HasMaxLength(128).HasComment("Idempotency key supplied when confirming the count variance; expected to be a UUID-like or producer-stable token.");
        builder.Property(x => x.MovementId).HasColumnName("movement_id").HasMaxLength(150).HasComment("Stock movement id generated only after the count variance is posted.");
        builder.Property(x => x.ApprovalChainId).HasColumnName("approval_chain_id").HasMaxLength(150).HasComment("BusinessApproval chain id when the variance exceeds an approval threshold.");
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
        builder.Property(x => x.VarianceAmount).HasColumnName("variance_amount").IsRequired().HasPrecision(18, 6).HasComment("Absolute variance value at the ledger moving-average unit cost used for approval routing.");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasMaxLength(30).HasComment("Count adjustment lifecycle status: pending-approval, posted or voided.");
        builder.Property(x => x.ConfirmedAtUtc).HasColumnName("confirmed_at_utc").HasComment("UTC time when the count adjustment was posted after approval or auto-routing.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.CountTaskCode, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.ApprovalChainId })
            .IsUnique()
            .HasFilter("approval_chain_id is not null")
            .HasDatabaseName("ux_stock_count_adjustments_approval_chain");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.SkuCode, x.SiteCode, x.LocationCode, x.ConfirmedAtUtc });
    }
}
