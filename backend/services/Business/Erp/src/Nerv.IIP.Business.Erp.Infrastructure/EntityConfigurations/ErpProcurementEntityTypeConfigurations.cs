using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseReceiptAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseRequisitionAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.RequestForQuotationAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SupplierInvoiceAggregate;
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
        builder.Property(x => x.ConvertedPurchaseOrderNo).HasColumnName("converted_purchase_order_no").HasMaxLength(100).HasComment("Purchase order number generated from this requisition.");
        builder.Property(x => x.ConvertedAtUtc).HasColumnName("converted_at_utc").HasComment("UTC time when this requisition was converted to a purchase order.");
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
        builder.Property(x => x.CurrencyCode).HasColumnName("currency_code").IsRequired().HasMaxLength(10).HasComment("Purchase order currency code.");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasConversion<string>().HasMaxLength(50).HasComment("Purchase order status.");
        builder.Property(x => x.TotalAmount).HasColumnName("total_amount").IsRequired().HasPrecision(18, 6).HasComment("Purchase order total amount.");
        builder.Property(x => x.Version).HasColumnName("version").IsRequired().HasComment("Monotonic purchase order revision number.");
        builder.Property(x => x.ApprovalChainId).HasColumnName("approval_chain_id").HasMaxLength(150).HasComment("BusinessApproval chain id that gates purchase order release.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC creation time.");
        builder.HasMany(x => x.Lines).WithOne().HasForeignKey("PurchaseOrderId").OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.ChangeHistory).WithOne().HasForeignKey("PurchaseOrderId").OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(x => x.ChangeHistory).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.PurchaseOrderNo }).IsUnique();
    }
}

public sealed class PurchaseOrderChangeEntityTypeConfiguration : IEntityTypeConfiguration<PurchaseOrderChange>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderChange> builder)
    {
        builder.ToTable("purchase_order_changes", table => table.HasComment("Auditable purchase order amendment, final-delivery, and cancellation records."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseIdentityByDefaultColumn().HasComment("Purchase order change audit row id.");
        builder.Property<PurchaseOrderId>("PurchaseOrderId").HasColumnName("purchase_order_id").IsRequired().HasComment("Owning purchase order id.");
        builder.Property(x => x.ChangeType).HasColumnName("change_type").IsRequired().HasMaxLength(50).HasComment("Change category: amend, final-delivery, or cancel.");
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(1000).HasComment("Business reason for the order change.");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasConversion<string>().HasMaxLength(50).HasComment("Approval or application status for the purchase order change.");
        builder.Property(x => x.ApprovalChainId).HasColumnName("approval_chain_id").HasMaxLength(150).HasComment("BusinessApproval chain id for a pending purchase order amendment.");
        builder.Property(x => x.RequestedAtUtc).HasColumnName("requested_at_utc").IsRequired().HasComment("UTC time when the change was requested.");
        builder.Property(x => x.ResolvedAtUtc).HasColumnName("resolved_at_utc").HasComment("UTC time when the change was approved, rejected, or applied.");
        builder.HasMany(x => x.Lines).WithOne().HasForeignKey("PurchaseOrderChangeId").OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex("PurchaseOrderId", nameof(PurchaseOrderChange.ApprovalChainId)).IsUnique();
    }
}

public sealed class PurchaseOrderChangeLineEntityTypeConfiguration : IEntityTypeConfiguration<PurchaseOrderChangeLine>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderChangeLine> builder)
    {
        builder.ToTable("purchase_order_change_lines", table => table.HasComment("Auditable target values for a purchase order line amendment."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseIdentityByDefaultColumn().HasComment("Purchase order change line audit row id.");
        builder.Property<long>("PurchaseOrderChangeId").HasColumnName("purchase_order_change_id").IsRequired().HasComment("Owning purchase order change audit row id.");
        builder.Property(x => x.LineNo).HasColumnName("line_no").IsRequired().HasMaxLength(100).HasComment("Purchase order line number being changed.");
        builder.Property(x => x.OrderedQuantity).HasColumnName("ordered_quantity").IsRequired().HasPrecision(18, 6).HasComment("Approved target ordered quantity.");
        builder.Property(x => x.UnitPrice).HasColumnName("unit_price").IsRequired().HasPrecision(18, 6).HasComment("Approved target unit price.");
        builder.Property(x => x.PromisedDate).HasColumnName("promised_date").IsRequired().HasComment("Approved target promised receipt date.");
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
        builder.Property(x => x.OverReceiptTolerancePercent).HasColumnName("over_receipt_tolerance_percent").IsRequired().HasPrecision(9, 4).HasComment("Allowed over receipt tolerance percent for the line.");
        builder.Property(x => x.UnderReceiptTolerancePercent).HasColumnName("under_receipt_tolerance_percent").IsRequired().HasPrecision(9, 4).HasComment("Allowed under receipt tolerance percent for final delivery close.");
        builder.Property(x => x.FinalDelivery).HasColumnName("final_delivery").IsRequired().HasComment("Whether final delivery was declared and the line is closed despite remaining quantity.");
        builder.HasMany(x => x.SourceLinks).WithOne().HasForeignKey("PurchaseOrderLineId").OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.SourceLinks).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(x => x.OpenQuantity);
        builder.Ignore(x => x.LineAmount);
    }
}

public sealed class PurchaseOrderLineSourceLinkEntityTypeConfiguration : IEntityTypeConfiguration<PurchaseOrderLineSourceLink>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderLineSourceLink> builder)
    {
        builder.ToTable("purchase_order_line_sources", table => table.HasComment("ERP purchase order line source purchase requisition references."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseIdentityByDefaultColumn().HasComment("Purchase order line source row id.");
        builder.Property<PurchaseOrderLineId>("PurchaseOrderLineId").HasColumnName("purchase_order_line_id").IsRequired().HasComment("Owning purchase order line id.");
        builder.Property(x => x.PurchaseRequisitionNo).HasColumnName("purchase_requisition_no").IsRequired().HasMaxLength(100).HasComment("Source purchase requisition number.");
        builder.Property(x => x.PurchaseRequisitionLineNo).HasColumnName("purchase_requisition_line_no").IsRequired().HasMaxLength(100).HasComment("Source purchase requisition line number.");
        builder.Property(x => x.Quantity).HasColumnName("quantity").IsRequired().HasPrecision(18, 6).HasComment("Quantity pegged from the source requisition line.");
        builder.HasIndex("PurchaseOrderLineId", nameof(PurchaseOrderLineSourceLink.PurchaseRequisitionNo), nameof(PurchaseOrderLineSourceLink.PurchaseRequisitionLineNo)).IsUnique();
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
        builder.Property(x => x.CurrencyCode).HasColumnName("currency_code").IsRequired().HasMaxLength(10).HasComment("Receipt currency code copied from purchase order.");
        builder.Property(x => x.ExchangeRate).HasColumnName("exchange_rate").IsRequired().HasPrecision(18, 8).HasComment("Receipt exchange rate to local currency.");
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
        builder.Property(x => x.SkuCode).HasColumnName("sku_code").IsRequired().HasMaxLength(100).HasComment("MasterData SKU code copied from purchase order line for stock posting.");
        builder.Property(x => x.UomCode).HasColumnName("uom_code").IsRequired().HasMaxLength(50).HasComment("MasterData UOM code copied from purchase order line for stock posting.");
        builder.Property(x => x.LocationCode).HasColumnName("location_code").IsRequired().HasMaxLength(100).HasComment("Inventory receipt location code.");
        builder.Property(x => x.LotNo).HasColumnName("lot_no").HasMaxLength(100).HasComment("Optional received lot number.");
        builder.Property(x => x.ReceivedQuantity).HasColumnName("received_quantity").IsRequired().HasPrecision(18, 6).HasComment("Received quantity.");
        builder.Property(x => x.QualityStatus).HasColumnName("quality_status").IsRequired().HasMaxLength(50).HasComment("Line quality status.");
    }
}

public sealed class SupplierInvoiceEntityTypeConfiguration : IEntityTypeConfiguration<SupplierInvoice>
{
    public void Configure(EntityTypeBuilder<SupplierInvoice> builder)
    {
        builder.ToTable("supplier_invoices", table => table.HasComment("ERP supplier invoice header matched against purchase order and receipt."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Supplier invoice aggregate id.");
        PurchaseRequisitionEntityTypeConfiguration.AddTenantColumns(builder);
        builder.Property(x => x.InvoiceNo).HasColumnName("invoice_no").IsRequired().HasMaxLength(100).HasComment("Supplier invoice number.");
        builder.Property(x => x.PurchaseOrderNo).HasColumnName("purchase_order_no").IsRequired().HasMaxLength(100).HasComment("Matched purchase order number.");
        builder.Property(x => x.PurchaseReceiptNo).HasColumnName("purchase_receipt_no").IsRequired().HasMaxLength(100).HasComment("Matched purchase receipt number.");
        builder.Property(x => x.SupplierCode).HasColumnName("supplier_code").IsRequired().HasMaxLength(100).HasComment("MasterData supplier code.");
        builder.Property(x => x.InvoiceDate).HasColumnName("invoice_date").IsRequired().HasComment("Supplier invoice date.");
        builder.Property(x => x.DueDate).HasColumnName("due_date").IsRequired().HasComment("Payment due date.");
        builder.Property(x => x.CurrencyCode).HasColumnName("currency_code").IsRequired().HasMaxLength(10).HasComment("Invoice currency code.");
        builder.Property(x => x.ExchangeRate).HasColumnName("exchange_rate").IsRequired().HasPrecision(18, 8).HasComment("Invoice exchange rate to local currency.");
        builder.Property(x => x.TotalAmount).HasColumnName("total_amount").IsRequired().HasPrecision(18, 6).HasComment("Matched invoice total amount.");
        builder.Property(x => x.LocalTotalAmount).HasColumnName("local_total_amount").IsRequired().HasPrecision(18, 6).HasComment("Matched invoice total amount in local currency.");
        builder.Property(x => x.MatchStatus).HasColumnName("match_status").IsRequired().HasConversion<string>().HasMaxLength(50).HasComment("Three-way match status.");
        builder.Property(x => x.MatchedAtUtc).HasColumnName("matched_at_utc").IsRequired().HasComment("UTC match time.");
        builder.HasMany(x => x.Lines).WithOne().HasForeignKey("SupplierInvoiceId").OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.InvoiceNo }).IsUnique();
    }
}

public sealed class SupplierInvoiceLineEntityTypeConfiguration : IEntityTypeConfiguration<SupplierInvoiceLine>
{
    public void Configure(EntityTypeBuilder<SupplierInvoiceLine> builder)
    {
        builder.ToTable("supplier_invoice_lines", table => table.HasComment("ERP supplier invoice lines used for three-way match."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Supplier invoice line id.");
        builder.Property<SupplierInvoiceId>("SupplierInvoiceId").HasColumnName("supplier_invoice_id").IsRequired().HasComment("Owning supplier invoice id.");
        builder.Property(x => x.PurchaseOrderLineNo).HasColumnName("purchase_order_line_no").IsRequired().HasMaxLength(100).HasComment("Matched purchase order line number.");
        builder.Property(x => x.PurchaseReceiptLineNo).HasColumnName("purchase_receipt_line_no").IsRequired().HasMaxLength(100).HasComment("Matched purchase receipt line number.");
        builder.Property(x => x.SkuCode).HasColumnName("sku_code").IsRequired().HasMaxLength(100).HasComment("Matched SKU code.");
        builder.Property(x => x.UomCode).HasColumnName("uom_code").IsRequired().HasMaxLength(50).HasComment("Matched UOM code.");
        builder.Property(x => x.InvoiceQuantity).HasColumnName("invoice_quantity").IsRequired().HasPrecision(18, 6).HasComment("Supplier invoice quantity.");
        builder.Property(x => x.UnitPrice).HasColumnName("unit_price").IsRequired().HasPrecision(18, 6).HasComment("Supplier invoice unit price.");
        builder.Ignore(x => x.LineAmount);
    }
}
