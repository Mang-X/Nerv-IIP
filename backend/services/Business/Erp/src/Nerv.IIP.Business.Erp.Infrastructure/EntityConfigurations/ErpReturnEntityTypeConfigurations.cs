using Nerv.IIP.Business.Erp.Domain.AggregatesModel.CreditNoteAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.DebitNoteAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseReturnAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SalesReturnAuthorizationAggregate;

namespace Nerv.IIP.Business.Erp.Infrastructure.EntityConfigurations;

public sealed class PurchaseReturnEntityTypeConfiguration : IEntityTypeConfiguration<PurchaseReturn>
{
    public void Configure(EntityTypeBuilder<PurchaseReturn> builder)
    {
        builder.ToTable("purchase_returns", table => table.HasComment("ERP immutable supplier purchase return recorded from completed WMS outbound."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Purchase return aggregate id.");
        PurchaseRequisitionEntityTypeConfiguration.AddTenantColumns(builder);
        builder.Property(x => x.PurchaseReturnNo).HasColumnName("purchase_return_no").IsRequired().HasMaxLength(100).HasComment("ERP purchase return document number.");
        builder.Property(x => x.PurchaseReceiptNo).HasColumnName("purchase_receipt_no").IsRequired().HasMaxLength(100).HasComment("Immutable ERP receipt being compensated.");
        builder.Property(x => x.WmsOutboundOrderNo).HasColumnName("wms_outbound_order_no").IsRequired().HasMaxLength(100).HasComment("Completed WMS supplier-return outbound reference.");
        builder.Property(x => x.SupplierCode).HasColumnName("supplier_code").IsRequired().HasMaxLength(100).HasComment("Supplier copied from the source receipt.");
        builder.Property(x => x.CurrencyCode).HasColumnName("currency_code").IsRequired().HasMaxLength(10).HasComment("Return currency copied from the source receipt.");
        builder.Property(x => x.ExchangeRate).HasColumnName("exchange_rate").IsRequired().HasPrecision(18, 8).HasComment("Return exchange rate to local currency.");
        builder.Property(x => x.RecordedAtUtc).HasColumnName("recorded_at_utc").IsRequired().HasComment("UTC time ERP recorded the completed physical return.");
        builder.HasMany(x => x.Lines).WithOne().HasForeignKey("PurchaseReturnId").OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.PurchaseReturnNo }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.WmsOutboundOrderNo }).IsUnique();
    }
}

public sealed class PurchaseReturnLineEntityTypeConfiguration : IEntityTypeConfiguration<PurchaseReturnLine>
{
    public void Configure(EntityTypeBuilder<PurchaseReturnLine> builder)
    {
        builder.ToTable("purchase_return_lines", table => table.HasComment("ERP purchase return line with GR/IR and debit-note quantity split."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Purchase return line id.");
        builder.Property<PurchaseReturnId>("PurchaseReturnId").HasColumnName("purchase_return_id").IsRequired().HasComment("Owning purchase return id.");
        builder.Property(x => x.PurchaseOrderLineNo).HasColumnName("purchase_order_line_no").IsRequired().HasMaxLength(100).HasComment("Source purchase receipt purchase-order line reference.");
        builder.Property(x => x.SkuCode).HasColumnName("sku_code").IsRequired().HasMaxLength(100).HasComment("Source SKU code.");
        builder.Property(x => x.UomCode).HasColumnName("uom_code").IsRequired().HasMaxLength(50).HasComment("Source UOM code.");
        builder.Property(x => x.ReturnedQuantity).HasColumnName("returned_quantity").IsRequired().HasPrecision(18, 6).HasComment("Physically returned WMS quantity.");
        builder.Property(x => x.UnitPrice).HasColumnName("unit_price").IsRequired().HasPrecision(18, 6).HasComment("Source invoice unit price for debit-note segments or purchase-order unit price for GR/IR segments.");
        builder.Property(x => x.GrIrReversalQuantity).HasColumnName("gr_ir_reversal_quantity").IsRequired().HasPrecision(18, 6).HasComment("Uninvoiced returned quantity reversing GR/IR.");
        builder.Property(x => x.DebitNoteQuantity).HasColumnName("debit_note_quantity").IsRequired().HasPrecision(18, 6).HasComment("Invoice-matched returned quantity settled by debit note.");
        builder.Ignore(x => x.GrIrReversalAmount);
        builder.Ignore(x => x.DebitNoteAmount);
    }
}

public sealed class SalesReturnAuthorizationEntityTypeConfiguration : IEntityTypeConfiguration<SalesReturnAuthorization>
{
    public void Configure(EntityTypeBuilder<SalesReturnAuthorization> builder)
    {
        builder.ToTable("sales_return_authorizations", table => table.HasComment("ERP customer RMA authorization and Quality-gated credit lifecycle."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("RMA aggregate id.");
        PurchaseRequisitionEntityTypeConfiguration.AddTenantColumns(builder);
        builder.Property(x => x.RmaNo).HasColumnName("rma_no").IsRequired().HasMaxLength(100).HasComment("Customer return authorization number.");
        builder.Property(x => x.SalesOrderNo).HasColumnName("sales_order_no").IsRequired().HasMaxLength(100).HasComment("Source ERP sales order number.");
        builder.Property(x => x.AccountReceivableNo).HasColumnName("account_receivable_no").IsRequired().HasMaxLength(100).HasComment("Open AR to settle by credit note.");
        builder.Property(x => x.CustomerCode).HasColumnName("customer_code").IsRequired().HasMaxLength(100).HasComment("Source customer code.");
        builder.Property(x => x.SiteCode).HasColumnName("site_code").IsRequired().HasMaxLength(100).HasComment("Return receiving site.");
        builder.Property(x => x.CurrencyCode).HasColumnName("currency_code").IsRequired().HasMaxLength(10).HasComment("RMA credit currency.");
        builder.Property(x => x.ExchangeRate).HasColumnName("exchange_rate").IsRequired().HasPrecision(18, 8).HasComment("RMA exchange rate to local currency.");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasConversion<string>().HasMaxLength(50).HasComment("RMA lifecycle status.");
        builder.Property(x => x.WmsInboundOrderNo).HasColumnName("wms_inbound_order_no").HasMaxLength(100).HasComment("Actual WMS customer-return inbound order reference.");
        builder.Property(x => x.QualityDisposition).HasColumnName("quality_disposition").HasMaxLength(50).HasComment("Quality result that permits or denies the credit.");
        builder.Property(x => x.CreditNoteNo).HasColumnName("credit_note_no").HasMaxLength(100).HasComment("Issued ERP credit note reference.");
        builder.Property(x => x.AuthorizedAtUtc).HasColumnName("authorized_at_utc").IsRequired().HasComment("UTC authorization time.");
        builder.Property(x => x.WarehouseReceivedAtUtc).HasColumnName("warehouse_received_at_utc").HasComment("UTC WMS inbound completion projection time.");
        builder.Property(x => x.QualityDispositionAtUtc).HasColumnName("quality_disposition_at_utc").HasComment("UTC Quality disposition projection time.");
        builder.Property(x => x.CreditIssuedAtUtc).HasColumnName("credit_issued_at_utc").HasComment("UTC credit note issuance time.");
        builder.HasMany(x => x.Lines).WithOne().HasForeignKey("SalesReturnAuthorizationId").OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.RmaNo }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.WmsInboundOrderNo }).IsUnique().HasFilter("wms_inbound_order_no IS NOT NULL");
    }
}

public sealed class SalesReturnAuthorizationLineEntityTypeConfiguration : IEntityTypeConfiguration<SalesReturnAuthorizationLine>
{
    public void Configure(EntityTypeBuilder<SalesReturnAuthorizationLine> builder)
    {
        builder.ToTable("sales_return_authorization_lines", table => table.HasComment("ERP RMA source sales line and requested return quantity."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("RMA line id.");
        builder.Property<SalesReturnAuthorizationId>("SalesReturnAuthorizationId").HasColumnName("sales_return_authorization_id").IsRequired().HasComment("Owning RMA id.");
        builder.Property(x => x.SalesOrderLineNo).HasColumnName("sales_order_line_no").IsRequired().HasMaxLength(100).HasComment("Source sales order line number.");
        builder.Property(x => x.SkuCode).HasColumnName("sku_code").IsRequired().HasMaxLength(100).HasComment("Source SKU code.");
        builder.Property(x => x.UomCode).HasColumnName("uom_code").IsRequired().HasMaxLength(50).HasComment("Source UOM code.");
        builder.Property(x => x.Quantity).HasColumnName("quantity").IsRequired().HasPrecision(18, 6).HasComment("Authorized return quantity.");
        builder.Property(x => x.UnitPrice).HasColumnName("unit_price").IsRequired().HasPrecision(18, 6).HasComment("Source sales unit price for credit.");
        builder.Property(x => x.LocationCode).HasColumnName("location_code").IsRequired().HasMaxLength(100).HasComment("WMS return receiving location.");
        builder.Property(x => x.LotNo).HasColumnName("lot_no").HasMaxLength(100).HasComment("Optional expected return lot.");
        builder.Ignore(x => x.LineAmount);
    }
}

public sealed class DebitNoteEntityTypeConfiguration : IEntityTypeConfiguration<DebitNote>
{
    public void Configure(EntityTypeBuilder<DebitNote> builder)
    {
        builder.ToTable("debit_notes", table => table.HasComment("ERP supplier debit note applied to an open AP after purchase return."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Debit note aggregate id.");
        PurchaseRequisitionEntityTypeConfiguration.AddTenantColumns(builder);
        builder.Property(x => x.DebitNoteNo).HasColumnName("debit_note_no").IsRequired().HasMaxLength(100).HasComment("Supplier debit note number.");
        builder.Property(x => x.PurchaseReturnNo).HasColumnName("purchase_return_no").IsRequired().HasMaxLength(100).HasComment("Source purchase return number.");
        builder.Property(x => x.PayableNo).HasColumnName("payable_no").IsRequired().HasMaxLength(100).HasComment("AP document reduced by this note.");
        builder.Property(x => x.SupplierCode).HasColumnName("supplier_code").IsRequired().HasMaxLength(100).HasComment("Supplier code.");
        builder.Property(x => x.Amount).HasColumnName("amount").IsRequired().HasPrecision(18, 6).HasComment("Debit note amount.");
        builder.Property(x => x.CurrencyCode).HasColumnName("currency_code").IsRequired().HasMaxLength(10).HasComment("Debit note currency.");
        builder.Property(x => x.ExchangeRate).HasColumnName("exchange_rate").IsRequired().HasPrecision(18, 8).HasComment("Debit note exchange rate.");
        builder.Property(x => x.LocalAmount).HasColumnName("local_amount").IsRequired().HasPrecision(18, 6).HasComment("Debit note local amount.");
        builder.Property(x => x.IssuedAtUtc).HasColumnName("issued_at_utc").IsRequired().HasComment("UTC issue time.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.DebitNoteNo }).IsUnique();
    }
}

public sealed class CreditNoteEntityTypeConfiguration : IEntityTypeConfiguration<CreditNote>
{
    public void Configure(EntityTypeBuilder<CreditNote> builder)
    {
        builder.ToTable("credit_notes", table => table.HasComment("ERP customer credit note issued after RMA Quality disposition."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Credit note aggregate id.");
        PurchaseRequisitionEntityTypeConfiguration.AddTenantColumns(builder);
        builder.Property(x => x.CreditNoteNo).HasColumnName("credit_note_no").IsRequired().HasMaxLength(100).HasComment("Customer credit note number.");
        builder.Property(x => x.RmaNo).HasColumnName("rma_no").IsRequired().HasMaxLength(100).HasComment("Source RMA number.");
        builder.Property(x => x.AccountReceivableNo).HasColumnName("account_receivable_no").IsRequired().HasMaxLength(100).HasComment("AR document settled by this credit.");
        builder.Property(x => x.CustomerCode).HasColumnName("customer_code").IsRequired().HasMaxLength(100).HasComment("Customer code.");
        builder.Property(x => x.CurrencyCode).HasColumnName("currency_code").IsRequired().HasMaxLength(10).HasComment("Credit note currency.");
        builder.Property(x => x.ExchangeRate).HasColumnName("exchange_rate").IsRequired().HasPrecision(18, 8).HasComment("Credit note exchange rate.");
        builder.Property(x => x.Amount).HasColumnName("amount").IsRequired().HasPrecision(18, 6).HasComment("Credit amount.");
        builder.Property(x => x.LocalAmount).HasColumnName("local_amount").IsRequired().HasPrecision(18, 6).HasComment("Credit local amount.");
        builder.Property(x => x.IssuedAtUtc).HasColumnName("issued_at_utc").IsRequired().HasComment("UTC issue time.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.CreditNoteNo }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.RmaNo }).IsUnique();
    }
}
