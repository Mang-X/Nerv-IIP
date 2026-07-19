using Nerv.IIP.Business.DemandPlanning.Infrastructure.IntegrationEvents;

namespace Nerv.IIP.Business.DemandPlanning.Infrastructure.EntityConfigurations;

public sealed class SalesOrderDemandProjectionEntityTypeConfiguration : IEntityTypeConfiguration<SalesOrderDemandProjection>
{
    public void Configure(EntityTypeBuilder<SalesOrderDemandProjection> builder)
    {
        builder.ToTable("sales_order_demand_projections", table => table.HasComment("Per-sales-order lifecycle watermark used to reject duplicate and out-of-order ERP events."));
        builder.HasKey(x => new { x.OrganizationId, x.EnvironmentId, x.SalesOrderId });
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").HasMaxLength(64).HasComment("Tenant organization id.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").HasMaxLength(64).HasComment("Planning environment id.");
        builder.Property(x => x.SalesOrderId).HasColumnName("sales_order_id").HasMaxLength(128).HasComment("Stable ERP sales order public id.");
        builder.Property(x => x.SalesOrderNo).HasColumnName("sales_order_no").HasMaxLength(128).HasComment("ERP sales order number for traceability.");
        builder.Property(x => x.CustomerCode).HasColumnName("customer_code").HasMaxLength(100).HasComment("Customer code snapshot.");
        builder.Property(x => x.SiteCode).HasColumnName("site_code").HasMaxLength(64).HasComment("Planning site code snapshot.");
        builder.Property(x => x.OrderVersion).HasColumnName("order_version").IsConcurrencyToken().HasComment("Latest accepted ERP sales order business version and optimistic concurrency token.");
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).HasComment("Latest accepted ERP sales order lifecycle status.");
        builder.Property(x => x.LastEventId).HasColumnName("last_event_id").HasMaxLength(256).HasComment("Latest accepted integration event identifier.");
        builder.Property(x => x.OccurredAtUtc).HasColumnName("occurred_at_utc").HasComment("Source event occurrence time for audit.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.SalesOrderNo }).IsUnique().HasDatabaseName("ux_sales_order_demand_projection_scope_order_no");
    }
}
