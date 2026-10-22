using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderDemandChangeAggregate;

namespace Nerv.IIP.Business.Mes.Infrastructure.EntityConfigurations;

public sealed class WorkOrderDemandChangeEntityTypeConfiguration : IEntityTypeConfiguration<WorkOrderDemandChange>
{
    public void Configure(EntityTypeBuilder<WorkOrderDemandChange> builder)
    {
        builder.ToTable("work_order_demand_changes", table =>
            table.HasComment("Latest sales demand change fact for each MES work order and source demand reference."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("MES demand change marker identifier.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization that owns the affected work order.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment that owns the affected work order.");
        builder.Property(x => x.WorkOrderId).HasColumnName("work_order_id").IsRequired().HasMaxLength(100).HasComment("MES business work order id affected by the source demand change.");
        builder.Property(x => x.SuggestionId).HasColumnName("suggestion_id").IsRequired().HasMaxLength(100).HasComment("DemandPlanning suggestion from which the MES work order was converted.");
        builder.Property(x => x.DemandSourceReference).HasColumnName("demand_source_reference").IsRequired().HasMaxLength(100).HasComment("Exact demand source reference pegged to the affected work order.");
        builder.Property(x => x.SalesOrderId).HasColumnName("sales_order_id").IsRequired().HasMaxLength(100).HasComment("ERP sales order public id carried by the demand change event.");
        builder.Property(x => x.OrderVersion).HasColumnName("order_version").IsRequired().IsConcurrencyToken().HasComment("Latest ERP sales order version applied to this demand marker; older events cannot overwrite it.");
        builder.Property(x => x.Cancelled).HasColumnName("cancelled").IsRequired().HasComment("Whether the latest source demand change cancelled the demand.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.WorkOrderId, x.DemandSourceReference })
            .IsUnique()
            .HasDatabaseName("ux_work_order_demand_changes_scope_order_demand");
    }
}
