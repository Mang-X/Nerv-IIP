using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountPayableAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountReceivableAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.CostCandidateAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.DeliveryOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.JournalVoucherAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.OpportunityAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.QuotationAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SalesOrderAggregate;

namespace Nerv.IIP.Business.Erp.Infrastructure.EntityConfigurations;

public sealed class OpportunityEntityTypeConfiguration : IEntityTypeConfiguration<Opportunity>
{
    public void Configure(EntityTypeBuilder<Opportunity> builder)
    {
        builder.ToTable("opportunities", table => table.HasComment("ERP sales opportunity header."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Opportunity aggregate id.");
        PurchaseRequisitionEntityTypeConfiguration.AddTenantColumns(builder);
        builder.Property(x => x.OpportunityNo).HasColumnName("opportunity_no").IsRequired().HasMaxLength(100).HasComment("Opportunity number.");
        builder.Property(x => x.CustomerCode).HasColumnName("customer_code").IsRequired().HasMaxLength(100).HasComment("MasterData customer code.");
        builder.Property(x => x.Topic).HasColumnName("topic").IsRequired().HasMaxLength(200).HasComment("Opportunity topic.");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasMaxLength(50).HasComment("Opportunity lifecycle status.");
        builder.Property(x => x.OpenedAtUtc).HasColumnName("opened_at_utc").IsRequired().HasComment("UTC opportunity open time.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.OpportunityNo }).IsUnique();
    }
}

public sealed class QuotationEntityTypeConfiguration : IEntityTypeConfiguration<Quotation>
{
    public void Configure(EntityTypeBuilder<Quotation> builder)
    {
        builder.ToTable("quotations", table => table.HasComment("ERP customer quotation header."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Quotation aggregate id.");
        PurchaseRequisitionEntityTypeConfiguration.AddTenantColumns(builder);
        builder.Property(x => x.QuotationNo).HasColumnName("quotation_no").IsRequired().HasMaxLength(100).HasComment("Quotation number.");
        builder.Property(x => x.CustomerCode).HasColumnName("customer_code").IsRequired().HasMaxLength(100).HasComment("MasterData customer code.");
        builder.Property(x => x.ExpiresOn).HasColumnName("expires_on").IsRequired().HasComment("Quotation expiration date.");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasConversion<string>().HasMaxLength(50).HasComment("Quotation approval status.");
        builder.Property(x => x.TotalAmount).HasColumnName("total_amount").IsRequired().HasPrecision(18, 6).HasComment("Quotation total amount.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC quotation creation time.");
        builder.HasMany(x => x.Lines).WithOne().HasForeignKey("QuotationId").OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.QuotationNo }).IsUnique();
    }
}

public sealed class QuotationLineEntityTypeConfiguration : IEntityTypeConfiguration<QuotationLine>
{
    public void Configure(EntityTypeBuilder<QuotationLine> builder)
    {
        builder.ToTable("quotation_lines", table => table.HasComment("ERP customer quotation lines."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Quotation line id.");
        builder.Property<QuotationId>("QuotationId").HasColumnName("quotation_id").IsRequired().HasComment("Owning quotation id.");
        builder.Property(x => x.LineNo).HasColumnName("line_no").IsRequired().HasMaxLength(100).HasComment("Quotation line number.");
        builder.Property(x => x.SkuCode).HasColumnName("sku_code").IsRequired().HasMaxLength(100).HasComment("MasterData SKU code.");
        builder.Property(x => x.UomCode).HasColumnName("uom_code").IsRequired().HasMaxLength(50).HasComment("MasterData UOM code.");
        builder.Property(x => x.Quantity).HasColumnName("quantity").IsRequired().HasPrecision(18, 6).HasComment("Quoted quantity.");
        builder.Property(x => x.UnitPrice).HasColumnName("unit_price").IsRequired().HasPrecision(18, 6).HasComment("Quoted unit price.");
        builder.Property(x => x.RequiredDate).HasColumnName("required_date").IsRequired().HasComment("Customer required date.");
        builder.Ignore(x => x.LineAmount);
    }
}

public sealed class SalesOrderEntityTypeConfiguration : IEntityTypeConfiguration<SalesOrder>
{
    public void Configure(EntityTypeBuilder<SalesOrder> builder)
    {
        builder.ToTable("sales_orders", table => table.HasComment("ERP sales order header."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Sales order aggregate id.");
        PurchaseRequisitionEntityTypeConfiguration.AddTenantColumns(builder);
        builder.Property(x => x.SalesOrderNo).HasColumnName("sales_order_no").IsRequired().HasMaxLength(100).HasComment("Sales order number.");
        builder.Property(x => x.QuotationNo).HasColumnName("quotation_no").IsRequired().HasMaxLength(100).HasComment("Source quotation number.");
        builder.Property(x => x.CustomerCode).HasColumnName("customer_code").IsRequired().HasMaxLength(100).HasComment("MasterData customer code.");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasMaxLength(50).HasComment("Sales order lifecycle status.");
        builder.Property(x => x.TotalAmount).HasColumnName("total_amount").IsRequired().HasPrecision(18, 6).HasComment("Sales order total amount.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC creation time.");
        builder.HasMany(x => x.Lines).WithOne().HasForeignKey("SalesOrderId").OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.SalesOrderNo }).IsUnique();
    }
}

public sealed class SalesOrderLineEntityTypeConfiguration : IEntityTypeConfiguration<SalesOrderLine>
{
    public void Configure(EntityTypeBuilder<SalesOrderLine> builder)
    {
        builder.ToTable("sales_order_lines", table => table.HasComment("ERP sales order lines and delivered quantity fact."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Sales order line id.");
        builder.Property<SalesOrderId>("SalesOrderId").HasColumnName("sales_order_id").IsRequired().HasComment("Owning sales order id.");
        builder.Property(x => x.LineNo).HasColumnName("line_no").IsRequired().HasMaxLength(100).HasComment("Sales order line number.");
        builder.Property(x => x.SkuCode).HasColumnName("sku_code").IsRequired().HasMaxLength(100).HasComment("MasterData SKU code.");
        builder.Property(x => x.UomCode).HasColumnName("uom_code").IsRequired().HasMaxLength(50).HasComment("MasterData UOM code.");
        builder.Property(x => x.OrderedQuantity).HasColumnName("ordered_quantity").IsRequired().HasPrecision(18, 6).HasComment("Ordered quantity.");
        builder.Property(x => x.DeliveredQuantity).HasColumnName("delivered_quantity").IsRequired().HasPrecision(18, 6).HasComment("Released delivery quantity.");
        builder.Property(x => x.UnitPrice).HasColumnName("unit_price").IsRequired().HasPrecision(18, 6).HasComment("Sales unit price.");
        builder.Property(x => x.RequiredDate).HasColumnName("required_date").IsRequired().HasComment("Customer required date.");
        builder.Ignore(x => x.OpenQuantity);
        builder.Ignore(x => x.LineAmount);
    }
}

public sealed class DeliveryOrderEntityTypeConfiguration : IEntityTypeConfiguration<DeliveryOrder>
{
    public void Configure(EntityTypeBuilder<DeliveryOrder> builder)
    {
        builder.ToTable("delivery_orders", table => table.HasComment("ERP delivery order request header for WMS outbound execution."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Delivery order aggregate id.");
        PurchaseRequisitionEntityTypeConfiguration.AddTenantColumns(builder);
        builder.Property(x => x.DeliveryOrderNo).HasColumnName("delivery_order_no").IsRequired().HasMaxLength(100).HasComment("Delivery order request number.");
        builder.Property(x => x.SalesOrderNo).HasColumnName("sales_order_no").IsRequired().HasMaxLength(100).HasComment("Source sales order number.");
        builder.Property(x => x.CustomerCode).HasColumnName("customer_code").IsRequired().HasMaxLength(100).HasComment("MasterData customer code.");
        builder.Property(x => x.ReleasedAtUtc).HasColumnName("released_at_utc").IsRequired().HasComment("UTC release time.");
        builder.HasMany(x => x.Lines).WithOne().HasForeignKey("DeliveryOrderId").OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.DeliveryOrderNo }).IsUnique();
    }
}

public sealed class DeliveryOrderLineEntityTypeConfiguration : IEntityTypeConfiguration<DeliveryOrderLine>
{
    public void Configure(EntityTypeBuilder<DeliveryOrderLine> builder)
    {
        builder.ToTable("delivery_order_lines", table => table.HasComment("ERP delivery order request lines."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Delivery order line id.");
        builder.Property<DeliveryOrderId>("DeliveryOrderId").HasColumnName("delivery_order_id").IsRequired().HasComment("Owning delivery order id.");
        builder.Property(x => x.SalesOrderLineNo).HasColumnName("sales_order_line_no").IsRequired().HasMaxLength(100).HasComment("Referenced sales order line number.");
        builder.Property(x => x.SkuCode).HasColumnName("sku_code").IsRequired().HasMaxLength(100).HasComment("MasterData SKU code copied from sales order line for WMS outbound execution.");
        builder.Property(x => x.UomCode).HasColumnName("uom_code").IsRequired().HasMaxLength(50).HasComment("MasterData UOM code copied from sales order line for WMS outbound execution.");
        builder.Property(x => x.LocationCode).HasColumnName("location_code").IsRequired().HasMaxLength(100).HasComment("Outbound source location code.");
        builder.Property(x => x.LotNo).HasColumnName("lot_no").HasMaxLength(100).HasComment("Optional outbound lot number.");
        builder.Property(x => x.Quantity).HasColumnName("quantity").IsRequired().HasPrecision(18, 6).HasComment("Requested delivery quantity.");
    }
}

public sealed class AccountPayableEntityTypeConfiguration : IEntityTypeConfiguration<AccountPayable>
{
    public void Configure(EntityTypeBuilder<AccountPayable> builder)
    {
        builder.ToTable("account_payables", table => table.HasComment("ERP account payable candidate fact."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Account payable aggregate id.");
        PurchaseRequisitionEntityTypeConfiguration.AddTenantColumns(builder);
        builder.Property(x => x.PayableNo).HasColumnName("payable_no").IsRequired().HasMaxLength(100).HasComment("AP document number.");
        builder.Property(x => x.SourceDocumentNo).HasColumnName("source_document_no").IsRequired().HasMaxLength(100).HasComment("Source document number.");
        builder.Property(x => x.SupplierCode).HasColumnName("supplier_code").IsRequired().HasMaxLength(100).HasComment("MasterData supplier code.");
        builder.Property(x => x.Amount).HasColumnName("amount").IsRequired().HasPrecision(18, 6).HasComment("Document amount.");
        builder.Property(x => x.PaidAmount).HasColumnName("paid_amount").IsRequired().HasPrecision(18, 6).HasComment("Paid amount.");
        builder.Property(x => x.LocalAmount).HasColumnName("local_amount").IsRequired().HasPrecision(18, 6).HasComment("Local currency amount at document exchange rate.");
        builder.Property(x => x.LocalPaidAmount).HasColumnName("local_paid_amount").IsRequired().HasPrecision(18, 6).HasComment("Local currency paid amount at document exchange rate.");
        builder.Property(x => x.CurrencyCode).HasColumnName("currency_code").IsRequired().HasMaxLength(10).HasComment("Currency code.");
        builder.Property(x => x.ExchangeRate).HasColumnName("exchange_rate").IsRequired().HasPrecision(18, 8).HasComment("Document exchange rate to local currency.");
        builder.Property(x => x.InvoiceDate).HasColumnName("invoice_date").IsRequired().HasComment("Supplier invoice date.");
        builder.Property(x => x.DueDate).HasColumnName("due_date").IsRequired().HasComment("Payment due date.");
        builder.Property(x => x.PaymentTermCode).HasColumnName("payment_term_code").IsRequired().HasMaxLength(50).HasComment("Payment term code snapshot.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC creation time.");
        builder.Ignore(x => x.OpenAmount);
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.PayableNo }).IsUnique();
    }
}

public sealed class AccountReceivableEntityTypeConfiguration : IEntityTypeConfiguration<AccountReceivable>
{
    public void Configure(EntityTypeBuilder<AccountReceivable> builder)
    {
        builder.ToTable("account_receivables", table => table.HasComment("ERP account receivable candidate fact."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Account receivable aggregate id.");
        PurchaseRequisitionEntityTypeConfiguration.AddTenantColumns(builder);
        builder.Property(x => x.ReceivableNo).HasColumnName("receivable_no").IsRequired().HasMaxLength(100).HasComment("AR document number.");
        builder.Property(x => x.SourceDocumentNo).HasColumnName("source_document_no").IsRequired().HasMaxLength(100).HasComment("Source document number.");
        builder.Property(x => x.CustomerCode).HasColumnName("customer_code").IsRequired().HasMaxLength(100).HasComment("MasterData customer code.");
        builder.Property(x => x.Amount).HasColumnName("amount").IsRequired().HasPrecision(18, 6).HasComment("Document amount.");
        builder.Property(x => x.CollectedAmount).HasColumnName("collected_amount").IsRequired().HasPrecision(18, 6).HasComment("Collected amount.");
        builder.Property(x => x.LocalAmount).HasColumnName("local_amount").IsRequired().HasPrecision(18, 6).HasComment("Local currency amount at document exchange rate.");
        builder.Property(x => x.LocalCollectedAmount).HasColumnName("local_collected_amount").IsRequired().HasPrecision(18, 6).HasComment("Local currency collected amount at document exchange rate.");
        builder.Property(x => x.CurrencyCode).HasColumnName("currency_code").IsRequired().HasMaxLength(10).HasComment("Currency code.");
        builder.Property(x => x.ExchangeRate).HasColumnName("exchange_rate").IsRequired().HasPrecision(18, 8).HasComment("Document exchange rate to local currency.");
        builder.Property(x => x.InvoiceDate).HasColumnName("invoice_date").IsRequired().HasComment("Customer invoice date.");
        builder.Property(x => x.DueDate).HasColumnName("due_date").IsRequired().HasComment("Collection due date.");
        builder.Property(x => x.PaymentTermCode).HasColumnName("payment_term_code").IsRequired().HasMaxLength(50).HasComment("Payment term code snapshot.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC creation time.");
        builder.Ignore(x => x.OpenAmount);
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.ReceivableNo }).IsUnique();
    }
}

public sealed class CostCandidateEntityTypeConfiguration : IEntityTypeConfiguration<CostCandidate>
{
    public void Configure(EntityTypeBuilder<CostCandidate> builder)
    {
        builder.ToTable("cost_candidates", table => table.HasComment("ERP cost candidate fact from public production, inventory or WMS facts."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Cost candidate aggregate id.");
        PurchaseRequisitionEntityTypeConfiguration.AddTenantColumns(builder);
        builder.Property(x => x.CandidateNo).HasColumnName("candidate_no").IsRequired().HasMaxLength(100).HasComment("Cost candidate number.");
        builder.Property(x => x.SourceType).HasColumnName("source_type").IsRequired().HasMaxLength(50).HasComment("Public source fact type.");
        builder.Property(x => x.SourceDocumentNo).HasColumnName("source_document_no").IsRequired().HasMaxLength(100).HasComment("Public source document number.");
        builder.Property(x => x.Amount).HasColumnName("amount").IsRequired().HasPrecision(18, 6).HasComment("Candidate amount.");
        builder.Property(x => x.CurrencyCode).HasColumnName("currency_code").IsRequired().HasMaxLength(10).HasComment("Currency code.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC creation time.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.CandidateNo }).IsUnique();
    }
}

public sealed class JournalVoucherEntityTypeConfiguration : IEntityTypeConfiguration<JournalVoucher>
{
    public void Configure(EntityTypeBuilder<JournalVoucher> builder)
    {
        builder.ToTable("journal_vouchers", table => table.HasComment("ERP balanced journal voucher posting fact."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Journal voucher aggregate id.");
        PurchaseRequisitionEntityTypeConfiguration.AddTenantColumns(builder);
        builder.Property(x => x.VoucherNo).HasColumnName("voucher_no").IsRequired().HasMaxLength(100).HasComment("Voucher number.");
        builder.Property(x => x.PostingDate).HasColumnName("posting_date").IsRequired().HasComment("Voucher posting date.");
        builder.Property(x => x.PostedAtUtc).HasColumnName("posted_at_utc").IsRequired().HasComment("UTC posting time.");
        builder.HasMany(x => x.Lines).WithOne().HasForeignKey("JournalVoucherId").OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.VoucherNo }).IsUnique();
    }
}

public sealed class JournalVoucherLineEntityTypeConfiguration : IEntityTypeConfiguration<JournalVoucherLine>
{
    public void Configure(EntityTypeBuilder<JournalVoucherLine> builder)
    {
        builder.ToTable("journal_voucher_lines", table => table.HasComment("ERP journal voucher debit and credit lines."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Journal voucher line id.");
        builder.Property<JournalVoucherId>("JournalVoucherId").HasColumnName("journal_voucher_id").IsRequired().HasComment("Owning journal voucher id.");
        builder.Property(x => x.AccountCode).HasColumnName("account_code").IsRequired().HasMaxLength(100).HasComment("Accounting subject code.");
        builder.Property(x => x.DebitAmount).HasColumnName("debit_amount").IsRequired().HasPrecision(18, 6).HasComment("Debit amount.");
        builder.Property(x => x.CreditAmount).HasColumnName("credit_amount").IsRequired().HasPrecision(18, 6).HasComment("Credit amount.");
        builder.Property(x => x.CurrencyCode).HasColumnName("currency_code").IsRequired().HasMaxLength(10).HasComment("Voucher line currency code.");
        builder.Property(x => x.ExchangeRate).HasColumnName("exchange_rate").IsRequired().HasPrecision(18, 8).HasComment("Voucher line exchange rate to local currency.");
        builder.Property(x => x.LocalDebitAmount).HasColumnName("local_debit_amount").IsRequired().HasPrecision(18, 6).HasComment("Debit amount in local currency.");
        builder.Property(x => x.LocalCreditAmount).HasColumnName("local_credit_amount").IsRequired().HasPrecision(18, 6).HasComment("Credit amount in local currency.");
        builder.Property(x => x.Memo).HasColumnName("memo").IsRequired().HasMaxLength(300).HasComment("Voucher line memo.");
    }
}
