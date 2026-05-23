using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;

namespace Nerv.IIP.Business.Mes.Infrastructure.EntityConfigurations;

public sealed class WorkOrderEntityTypeConfiguration : IEntityTypeConfiguration<WorkOrder>
{
    public void Configure(EntityTypeBuilder<WorkOrder> builder)
    {
        builder.ToTable("work_orders", tableBuilder =>
            tableBuilder.HasComment("MES durable work orders created from business demand and ProductEngineering production version references."));
        builder.HasKey(x => x.Id);
        builder.Ignore(x => x.WorkOrderId);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Work order aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id for the work order execution context.");
        builder.Property(x => x.WorkOrderIdValue).HasColumnName("work_order_id").IsRequired().HasMaxLength(100).HasComment("Business work order id unique within organization and environment.");
        builder.Property(x => x.SkuId).HasColumnName("sku_id").IsRequired().HasMaxLength(100).HasComment("MasterData SKU public id for the item being produced.");
        builder.Property(x => x.ProductionVersionId).HasColumnName("production_version_id").HasMaxLength(100).HasComment("ProductEngineering production version public id; MES does not duplicate engineering facts.");
        builder.Property(x => x.Quantity).HasColumnName("quantity").HasPrecision(18, 6).IsRequired().HasComment("Planned production quantity.");
        builder.Property(x => x.Priority).HasColumnName("priority").IsRequired().HasComment("Scheduling priority; rush work orders use a high priority value.");
        builder.Property(x => x.DueUtc).HasColumnName("due_utc").IsRequired().HasComment("UTC due time used by the deterministic rule scheduler.");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasMaxLength(30).HasComment("MES work order lifecycle status.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC time when the MES work order fact was created.");
        builder.HasAlternateKey(x => new { x.OrganizationId, x.EnvironmentId, x.WorkOrderIdValue })
            .HasName("ak_work_orders_scope_work_order");
        builder.HasIndex(x => x.WorkOrderIdValue).HasDatabaseName("ix_work_orders_work_order_id");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.SkuId, x.DueUtc })
            .HasDatabaseName("ix_work_orders_scope_sku_due");
    }
}
