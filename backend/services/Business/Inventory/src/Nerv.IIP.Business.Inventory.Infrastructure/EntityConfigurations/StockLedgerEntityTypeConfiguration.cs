using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLedgerAggregate;

namespace Nerv.IIP.Business.Inventory.Infrastructure.EntityConfigurations;

public sealed class StockLedgerEntityTypeConfiguration : IEntityTypeConfiguration<StockLedger>
{
    public void Configure(EntityTypeBuilder<StockLedger> builder)
    {
        builder.ToTable("stock_ledgers", tableBuilder =>
        {
            tableBuilder.HasComment("Inventory current stock ledger balances by SKU, UOM, site, location, lot, serial, quality and owner dimensions.");
            InventoryCodeCheckConstraints.Add(tableBuilder, "ck_stock_ledgers_location_code_format", "location_code");
            InventoryCodeCheckConstraints.Add(tableBuilder, "ck_stock_ledgers_sku_code_format", "sku_code");
            InventoryCodeCheckConstraints.Add(tableBuilder, "ck_stock_ledgers_site_code_format", "site_code");
            tableBuilder.HasCheckConstraint("ck_stock_ledgers_quality_status", "quality_status in ('unrestricted','quality','restricted','blocked')");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Stock ledger aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id that owns the ledger balance.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id where the balance is valid.");
        builder.Property(x => x.SkuCode).HasColumnName("sku_code").IsRequired().HasMaxLength(100).HasComment("MasterData SKU code.");
        builder.Property(x => x.UomCode).HasColumnName("uom_code").IsRequired().HasMaxLength(50).HasComment("MasterData unit of measure code for the quantity.");
        builder.Property(x => x.SiteCode).HasColumnName("site_code").IsRequired().HasMaxLength(100).HasComment("MasterData site code.");
        builder.Property(x => x.LocationCode).HasColumnName("location_code").IsRequired().HasMaxLength(100).HasComment("Inventory stock location code.");
        builder.Property(x => x.LotNo).HasColumnName("lot_no").HasMaxLength(100).HasComment("Optional lot or batch number dimension.");
        builder.Property(x => x.SerialNo).HasColumnName("serial_no").HasMaxLength(100).HasComment("Optional serial number dimension.");
        builder.Property(x => x.QualityStatus).HasColumnName("quality_status").IsRequired().HasMaxLength(50).HasComment("Quality status carried by stock facts: unrestricted, quality, restricted or blocked.");
        builder.Property(x => x.OwnerType).HasColumnName("owner_type").IsRequired().HasMaxLength(50).HasComment("Stock ownership type such as company, customer or supplier.");
        builder.Property(x => x.OwnerId).HasColumnName("owner_id").HasMaxLength(100).HasComment("Optional public owner reference id.");
        builder.Property(x => x.OnHandQuantity).HasColumnName("on_hand_quantity").IsRequired().HasPrecision(18, 6).HasComment("Current on-hand stock quantity.");
        builder.Property(x => x.ReservedQuantity).HasColumnName("reserved_quantity").IsRequired().HasPrecision(18, 6).HasComment("Current reserved stock quantity held by Inventory reservations.");
        builder.Property(x => x.MovingAverageUnitCost).HasColumnName("moving_average_unit_cost").IsRequired().HasPrecision(18, 6).HasComment("Current moving-average unit cost for this ledger dimension.");
        builder.Property(x => x.InventoryValue).HasColumnName("inventory_value").IsRequired().HasPrecision(18, 6).HasComment("Current inventory value for this ledger dimension.");
        builder.Property(x => x.IsFrozenForCount).HasColumnName("is_frozen_for_count").IsRequired().HasComment("Flag indicating regular movements are blocked while an open count task owns this ledger snapshot.");
        builder.Property(x => x.FrozenCountTaskCode).HasColumnName("frozen_count_task_code").HasMaxLength(100).HasComment("Open count task code that currently freezes this ledger, when any.");
        builder.Ignore(x => x.AvailableQuantity);
        builder.Property(x => x.LedgerVersion).HasColumnName("ledger_version").IsRequired().HasComment("Monotonic ledger version incremented when movements are applied.");
        builder.Property(x => x.RowVersion).HasColumnName("row_version").HasConversion(x => x.VersionNumber, x => new RowVersion(x)).IsConcurrencyToken().HasComment("Optimistic row version for concurrent stock balance updates.");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired().HasComment("UTC time when the ledger was last changed.");
        builder.Ignore(x => x.AppliedMovements);
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.SkuCode, x.UomCode, x.SiteCode, x.LocationCode, x.LotNo, x.SerialNo, x.QualityStatus, x.OwnerType, x.OwnerId }).IsUnique();
    }
}
