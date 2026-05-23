using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseReceiptAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseRequisitionAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.RequestForQuotationAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SupplierQuotationAggregate;

namespace Nerv.IIP.Business.Erp.Infrastructure.EntityConfigurations;

public sealed class PurchaseRequisitionEntityTypeConfiguration : IEntityTypeConfiguration<PurchaseRequisition>
{
    public void Configure(EntityTypeBuilder<PurchaseRequisition> builder)
    {
        builder.ToTable("purchase_requisitions", table => table.HasComment("ERP procurement purchase requisition from planning or manual demand."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Purchase requisition aggregate id.");
        AddTenantColumns(builder);
        builder.Property(x => x.RequisitionNo).HasColumnName("requisition_no").IsRequired().HasMaxLength(100).HasComment("Purchase requisition number.");
        builder.Property(x => x.SuggestionId).HasColumnName("suggestion_id").IsRequired().HasMaxLength(150).HasComment("DemandPlanning suggestion reference id.");
        builder.Property(x => x.SkuCode).HasColumnName("sku_code").IsRequired().HasMaxLength(100).HasComment("MasterData SKU code.");
        builder.Property(x => x.UomCode).HasColumnName("uom_code").IsRequired().HasMaxLength(50).HasComment("MasterData unit of measure code.");
        builder.Property(x => x.SiteCode).HasColumnName("site_code").IsRequired().HasMaxLength(100).HasComment("MasterData site code.");
        builder.Property(x => x.Quantity).HasColumnName("quantity").IsRequired().HasPrecision(18, 6).HasComment("Required procurement quantity.");
        builder.Property(x => x.RequiredDate).HasColumnName("required_date").IsRequired().HasComment("Required date from planning.");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasConversion<string>().HasMaxLength(50).HasComment("Purchase requisition status.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC creation time.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.SuggestionId }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.RequisitionNo }).IsUnique();
    }

    internal static void AddTenantColumns<T>(EntityTypeBuilder<T> builder)
        where T : class
    {
        builder.Property<string>("OrganizationId").HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id.");
        builder.Property<string>("EnvironmentId").HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id.");
    }
}

public sealed class RequestForQuotationEntityTypeConfiguration : IEntityTypeConfiguration<RequestForQuotation>
{
    public void Configure(EntityTypeBuilder<RequestForQuotation> builder)
    {
        builder.ToTable("request_for_quotations", table => table.HasComment("ERP request for quotation header."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Request for quotation aggregate id.");
        PurchaseRequisitionEntityTypeConfiguration.AddTenantColumns(builder);
        builder.Property(x => x.RfqNo).HasColumnName("rfq_no").IsRequired().HasMaxLength(100).HasComment("RFQ number.");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasConversion<string>().HasMaxLength(50).HasComment("RFQ status.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC creation time.");
        builder.HasMany(x => x.Lines).WithOne().HasForeignKey("RequestForQuotationId").OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Suppliers).WithOne().HasForeignKey("RequestForQuotationId").OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(x => x.Suppliers).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.RfqNo }).IsUnique();
    }
}

public sealed class RfqLineEntityTypeConfiguration : IEntityTypeConfiguration<RfqLine>
{
    public void Configure(EntityTypeBuilder<RfqLine> builder)
    {
        builder.ToTable("request_for_quotation_lines", table => table.HasComment("ERP request for quotation lines."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("RFQ line id.");
        builder.Property<RequestForQuotationId>("RequestForQuotationId").HasColumnName("request_for_quotation_id").IsRequired().HasComment("Owning RFQ id.");
        builder.Property(x => x.LineNo).HasColumnName("line_no").IsRequired().HasMaxLength(100).HasComment("RFQ line number.");
        builder.Property(x => x.SkuCode).HasColumnName("sku_code").IsRequired().HasMaxLength(100).HasComment("MasterData SKU code.");
        builder.Property(x => x.UomCode).HasColumnName("uom_code").IsRequired().HasMaxLength(50).HasComment("MasterData unit of measure code.");
        builder.Property(x => x.Quantity).HasColumnName("quantity").IsRequired().HasPrecision(18, 6).HasComment("Requested quotation quantity.");
        builder.Property(x => x.SiteCode).HasColumnName("site_code").IsRequired().HasMaxLength(100).HasComment("MasterData site code.");
        builder.Property(x => x.RequiredDate).HasColumnName("required_date").IsRequired().HasComment("Required date.");
    }
}

public sealed class RfqSupplierEntityTypeConfiguration : IEntityTypeConfiguration<RfqSupplier>
{
    public void Configure(EntityTypeBuilder<RfqSupplier> builder)
    {
        builder.ToTable("request_for_quotation_suppliers", table => table.HasComment("ERP RFQ invited supplier references."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseIdentityByDefaultColumn().HasComment("RFQ supplier row id.");
        builder.Property<RequestForQuotationId>("RequestForQuotationId").HasColumnName("request_for_quotation_id").IsRequired().HasComment("Owning RFQ id.");
        builder.Property(x => x.SupplierCode).HasColumnName("supplier_code").IsRequired().HasMaxLength(100).HasComment("MasterData supplier code.");
    }
}

public sealed class SupplierQuotationEntityTypeConfiguration : IEntityTypeConfiguration<SupplierQuotation>
{
    public void Configure(EntityTypeBuilder<SupplierQuotation> builder)
    {
        builder.ToTable("supplier_quotations", table => table.HasComment("ERP supplier quotation header."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Supplier quotation aggregate id.");
        PurchaseRequisitionEntityTypeConfiguration.AddTenantColumns(builder);
        builder.Property(x => x.QuotationNo).HasColumnName("quotation_no").IsRequired().HasMaxLength(100).HasComment("Supplier quotation number.");
        builder.Property(x => x.RfqNo).HasColumnName("rfq_no").IsRequired().HasMaxLength(100).HasComment("Referenced RFQ number.");
        builder.Property(x => x.SupplierCode).HasColumnName("supplier_code").IsRequired().HasMaxLength(100).HasComment("MasterData supplier code.");
        builder.Property(x => x.ReceivedAtUtc).HasColumnName("received_at_utc").IsRequired().HasComment("UTC quotation receipt time.");
        builder.HasMany(x => x.Lines).WithOne().HasForeignKey("SupplierQuotationId").OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.QuotationNo }).IsUnique();
    }
}

public sealed class SupplierQuotationLineEntityTypeConfiguration : IEntityTypeConfiguration<SupplierQuotationLine>
{
    public void Configure(EntityTypeBuilder<SupplierQuotationLine> builder)
    {
        builder.ToTable("supplier_quotation_lines", table => table.HasComment("ERP supplier quotation lines."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Supplier quotation line id.");
        builder.Property<SupplierQuotationId>("SupplierQuotationId").HasColumnName("supplier_quotation_id").IsRequired().HasComment("Owning supplier quotation id.");
        builder.Property(x => x.LineNo).HasColumnName("line_no").IsRequired().HasMaxLength(100).HasComment("Quotation line number.");
        builder.Property(x => x.SkuCode).HasColumnName("sku_code").IsRequired().HasMaxLength(100).HasComment("MasterData SKU code.");
        builder.Property(x => x.UomCode).HasColumnName("uom_code").IsRequired().HasMaxLength(50).HasComment("MasterData unit of measure code.");
        builder.Property(x => x.Quantity).HasColumnName("quantity").IsRequired().HasPrecision(18, 6).HasComment("Quoted quantity.");
        builder.Property(x => x.UnitPrice).HasColumnName("unit_price").IsRequired().HasPrecision(18, 6).HasComment("Quoted unit price.");
        builder.Property(x => x.PromisedDate).HasColumnName("promised_date").IsRequired().HasComment("Supplier promised date.");
    }
}

public sealed class PurchaseOrderEntityTypeConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> builder)
    {
        builder.ToTable("purchase_orders", table => table.HasComment("ERP purchase order header."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Purchase order aggregate id.");
        PurchaseRequisitionEntityTypeConfiguration.AddTenantColumns(builder);
        builder.Property(x => x.PurchaseOrderNo).HasColumnName("purchase_order_no").IsRequired().HasMaxLength(100).HasComment("Purchase order number.");
        builder.Property(x => x.SupplierCode).HasColumnName("supplier_code").IsRequired().HasMaxLength(100).HasComment("MasterData supplier code.");
        builder.Property(x => x.SiteCode).HasColumnName("site_code").IsRequired().HasMaxLength(100).HasComment("MasterData site code.");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasConversion<string>().HasMaxLength(50).HasComment("Purchase order status.");
        builder.Property(x => x.TotalAmount).HasColumnName("total_amount").IsRequired().HasPrecision(18, 6).HasComment("Purchase order total amount.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC creation time.");
        builder.HasMany(x => x.Lines).WithOne().HasForeignKey("PurchaseOrderId").OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.PurchaseOrderNo }).IsUnique();
    }
}

public sealed class PurchaseOrderLineEntityTypeConfiguration : IEntityTypeConfiguration<PurchaseOrderLine>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderLine> builder)
    {
        builder.ToTable("purchase_order_lines", table => table.HasComment("ERP purchase order lines and received quantity fact."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Purchase order line id.");
        builder.Property<PurchaseOrderId>("PurchaseOrderId").HasColumnName("purchase_order_id").IsRequired().HasComment("Owning purchase order id.");
        builder.Property(x => x.LineNo).HasColumnName("line_no").IsRequired().HasMaxLength(100).HasComment("Purchase order line number.");
        builder.Property(x => x.SkuCode).HasColumnName("sku_code").IsRequired().HasMaxLength(100).HasComment("MasterData SKU code.");
        builder.Property(x => x.UomCode).HasColumnName("uom_code").IsRequired().HasMaxLength(50).HasComment("MasterData unit of measure code.");
        builder.Property(x => x.OrderedQuantity).HasColumnName("ordered_quantity").IsRequired().HasPrecision(18, 6).HasComment("Ordered quantity.");
        builder.Property(x => x.ReceivedQuantity).HasColumnName("received_quantity").IsRequired().HasPrecision(18, 6).HasComment("ERP recorded receipt quantity.");
        builder.Property(x => x.UnitPrice).HasColumnName("unit_price").IsRequired().HasPrecision(18, 6).HasComment("Purchase unit price.");
        builder.Property(x => x.PromisedDate).HasColumnName("promised_date").IsRequired().HasComment("Promised receipt date.");
        builder.Ignore(x => x.OpenQuantity);
        builder.Ignore(x => x.LineAmount);
    }
}

public sealed class PurchaseReceiptEntityTypeConfiguration : IEntityTypeConfiguration<PurchaseReceipt>
{
    public void Configure(EntityTypeBuilder<PurchaseReceipt> builder)
    {
        builder.ToTable("purchase_receipts", table => table.HasComment("ERP purchase receipt header."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Purchase receipt aggregate id.");
        PurchaseRequisitionEntityTypeConfiguration.AddTenantColumns(builder);
        builder.Property(x => x.PurchaseReceiptNo).HasColumnName("purchase_receipt_no").IsRequired().HasMaxLength(100).HasComment("Purchase receipt number.");
        builder.Property(x => x.PurchaseOrderNo).HasColumnName("purchase_order_no").IsRequired().HasMaxLength(100).HasComment("Referenced purchase order number.");
        builder.Property(x => x.SupplierCode).HasColumnName("supplier_code").IsRequired().HasMaxLength(100).HasComment("MasterData supplier code.");
        builder.Property(x => x.SiteCode).HasColumnName("site_code").IsRequired().HasMaxLength(100).HasComment("MasterData site code.");
        builder.Property(x => x.QualityStatus).HasColumnName("quality_status").IsRequired().HasMaxLength(50).HasComment("Receipt quality state summary.");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasConversion<string>().HasMaxLength(50).HasComment("Purchase receipt status.");
        builder.Property(x => x.RecordedAtUtc).HasColumnName("recorded_at_utc").IsRequired().HasComment("UTC recording time.");
        builder.HasMany(x => x.Lines).WithOne().HasForeignKey("PurchaseReceiptId").OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.PurchaseReceiptNo }).IsUnique();
    }
}

public sealed class PurchaseReceiptLineEntityTypeConfiguration : IEntityTypeConfiguration<PurchaseReceiptLine>
{
    public void Configure(EntityTypeBuilder<PurchaseReceiptLine> builder)
    {
        builder.ToTable("purchase_receipt_lines", table => table.HasComment("ERP purchase receipt lines."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Purchase receipt line id.");
        builder.Property<PurchaseReceiptId>("PurchaseReceiptId").HasColumnName("purchase_receipt_id").IsRequired().HasComment("Owning purchase receipt id.");
        builder.Property(x => x.PurchaseOrderLineNo).HasColumnName("purchase_order_line_no").IsRequired().HasMaxLength(100).HasComment("Referenced purchase order line number.");
        builder.Property(x => x.ReceivedQuantity).HasColumnName("received_quantity").IsRequired().HasPrecision(18, 6).HasComment("Received quantity.");
        builder.Property(x => x.QualityStatus).HasColumnName("quality_status").IsRequired().HasMaxLength(50).HasComment("Line quality status.");
    }
}
