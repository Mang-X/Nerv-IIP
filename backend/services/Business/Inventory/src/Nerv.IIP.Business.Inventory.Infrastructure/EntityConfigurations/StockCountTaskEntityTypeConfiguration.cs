using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockCountTaskAggregate;

namespace Nerv.IIP.Business.Inventory.Infrastructure.EntityConfigurations;

public sealed class StockCountTaskEntityTypeConfiguration : IEntityTypeConfiguration<StockCountTask>
{
    public void Configure(EntityTypeBuilder<StockCountTask> builder)
    {
        builder.ToTable("stock_count_tasks", tableBuilder =>
        {
            tableBuilder.HasComment("Inventory stock count tasks, expected ledger version snapshots and confirmed variances.");
            InventoryCodeCheckConstraints.Add(tableBuilder, "ck_stock_count_tasks_location_code_format", "location_code");
            InventoryCodeCheckConstraints.Add(tableBuilder, "ck_stock_count_tasks_sku_code_format", "sku_code");
            InventoryCodeCheckConstraints.Add(tableBuilder, "ck_stock_count_tasks_site_code_format", "site_code");
            tableBuilder.HasCheckConstraint("ck_stock_count_tasks_quality_status", "quality_status in ('unrestricted','quality','restricted','blocked')");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Stock count task aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id that owns the count task.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id where the count task was created.");
        builder.Property(x => x.CountTaskCode).HasColumnName("count_task_code").IsRequired().HasMaxLength(100).HasComment("Business count task code.");
        builder.Property(x => x.LedgerOrganizationId).HasColumnName("ledger_organization_id").IsRequired().HasMaxLength(100).HasComment("Organization id of the ledger snapshot.");
        builder.Property(x => x.LedgerEnvironmentId).HasColumnName("ledger_environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id of the ledger snapshot.");
        builder.Property(x => x.SkuCode).HasColumnName("sku_code").IsRequired().HasMaxLength(100).HasComment("MasterData SKU code.");
        builder.Property(x => x.UomCode).HasColumnName("uom_code").IsRequired().HasMaxLength(50).HasComment("MasterData unit of measure code for counted quantity.");
        builder.Property(x => x.SiteCode).HasColumnName("site_code").IsRequired().HasMaxLength(100).HasComment("MasterData site code.");
        builder.Property(x => x.LocationCode).HasColumnName("location_code").IsRequired().HasMaxLength(100).HasComment("Inventory stock location code.");
        builder.Property(x => x.LotNo).HasColumnName("lot_no").HasMaxLength(100).HasComment("Optional lot or batch number dimension.");
        builder.Property(x => x.SerialNo).HasColumnName("serial_no").HasMaxLength(100).HasComment("Optional serial number dimension.");
        builder.Property(x => x.QualityStatus).HasColumnName("quality_status").IsRequired().HasMaxLength(50).HasComment("Quality status carried by counted stock.");
        builder.Property(x => x.OwnerType).HasColumnName("owner_type").IsRequired().HasMaxLength(50).HasComment("Stock ownership type such as company, customer or supplier.");
        builder.Property(x => x.OwnerId).HasColumnName("owner_id").HasMaxLength(100).HasComment("Optional public owner reference id.");
        builder.Property(x => x.ExpectedLedgerVersion).HasColumnName("expected_ledger_version").IsRequired().HasComment("Ledger version captured when the count task was created.");
        builder.Property(x => x.CountedQuantity).HasColumnName("counted_quantity").HasPrecision(18, 6).HasComment("Confirmed physical counted quantity.");
        builder.Property(x => x.VarianceQuantity).HasColumnName("variance_quantity").HasPrecision(18, 6).HasComment("Confirmed variance quantity against ledger on-hand.");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasMaxLength(30).HasComment("Stock count task lifecycle status.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC time when the count task was created.");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired().HasComment("UTC time when the count task was last changed.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.CountTaskCode }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.Status, x.SiteCode, x.LocationCode });
    }
}
