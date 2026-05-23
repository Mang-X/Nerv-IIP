using Nerv.IIP.Business.Mes.Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;

namespace Nerv.IIP.Business.Mes.Infrastructure.EntityConfigurations;

public sealed class FinishedGoodsReceiptRequestEntityTypeConfiguration : IEntityTypeConfiguration<FinishedGoodsReceiptRequest>
{
    public void Configure(EntityTypeBuilder<FinishedGoodsReceiptRequest> builder)
    {
        builder.ToTable("finished_goods_receipt_requests", tableBuilder =>
            tableBuilder.HasComment("MES finished goods receipt request facts exposed for WMS or inventory movement boundaries."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Finished goods receipt request aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id for the receipt request.");
        builder.Property(x => x.WorkOrderId).HasColumnName("work_order_id").IsRequired().HasMaxLength(100).HasComment("MES business work order id that produced finished goods.");
        builder.Property(x => x.SkuId).HasColumnName("sku_id").IsRequired().HasMaxLength(100).HasComment("MasterData SKU public id to receive.");
        builder.Property(x => x.Quantity).HasColumnName("quantity").HasPrecision(18, 6).IsRequired().HasComment("Finished goods quantity requested for receipt.");
        builder.Property(x => x.UomCode).HasColumnName("uom_code").IsRequired().HasMaxLength(50).HasComment("MasterData unit of measure code for the receipt quantity.");
        builder.Property(x => x.RequestedAtUtc).HasColumnName("requested_at_utc").IsRequired().HasComment("UTC time when MES requested finished goods receipt.");
        builder.HasOne<WorkOrder>()
            .WithMany()
            .HasPrincipalKey(x => new { x.OrganizationId, x.EnvironmentId, x.WorkOrderIdValue })
            .HasForeignKey(x => new { x.OrganizationId, x.EnvironmentId, x.WorkOrderId })
            .HasConstraintName("fk_receipt_requests_work_orders")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.WorkOrderId).HasDatabaseName("ix_receipt_requests_work_order_id");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.WorkOrderId })
            .HasDatabaseName("ix_receipt_requests_scope_work_order_fk");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.WorkOrderId, x.SkuId, x.RequestedAtUtc })
            .HasDatabaseName("ix_receipt_requests_scope_work_order_sku_time");
    }
}
