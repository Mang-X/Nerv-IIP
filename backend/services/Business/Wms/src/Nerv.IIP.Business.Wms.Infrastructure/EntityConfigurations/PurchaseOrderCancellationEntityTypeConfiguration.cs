using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nerv.IIP.Business.Wms.Infrastructure.IntegrationEvents;

namespace Nerv.IIP.Business.Wms.Infrastructure.EntityConfigurations;

public sealed class PurchaseOrderCancellationEntityTypeConfiguration : IEntityTypeConfiguration<PurchaseOrderCancellation>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderCancellation> builder)
    {
        builder.ToTable("purchase_order_cancellations", table =>
            table.HasComment("Terminal ERP cancellation projection for WMS-owned purchase orders."));
        builder.HasKey(x => new { x.OrganizationId, x.EnvironmentId, x.PurchaseOrderNo });
        InboundOrderEntityTypeConfiguration.AddTenantColumns(builder);
        builder.Property(x => x.PurchaseOrderNo).HasColumnName("purchase_order_no").HasMaxLength(100)
            .HasComment("ERP purchase order number unique within the organization and environment.");
        builder.Property(x => x.PurchaseOrderId).HasColumnName("purchase_order_id").HasMaxLength(150)
            .HasComment("Stable ERP purchase order identity for consistency checks.");
        builder.Property(x => x.SiteCode).HasColumnName("site_code").HasMaxLength(100)
            .HasComment("Site owning the cancelled purchase order.");
        builder.Property(x => x.PurchaseOrderVersion).HasColumnName("purchase_order_version")
            .HasComment("ERP aggregate version at the committed cancellation.");
        builder.Property(x => x.CancelledAtUtc).HasColumnName("cancelled_at_utc")
            .HasComment("UTC time of the committed ERP cancellation.");
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(1000)
            .HasComment("Reason for closing the purchase order receiving source.");
    }
}
