using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountPayableAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountReceivableAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountingPeriodAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.CashReceiptAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.CostCandidateAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.DeliveryOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.JournalVoucherAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.OpportunityAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PaymentExecutionAggregate;
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
        builder.Property(x => x.Version).HasColumnName("version").IsRequired().IsConcurrencyToken().HasComment("Monotonic sales order revision number.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC creation time.");
        builder.HasMany(x => x.Lines).WithOne().HasForeignKey("SalesOrderId").OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.ChangeHistory).WithOne().HasForeignKey("SalesOrderId").OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(x => x.ChangeHistory).UsePropertyAccessMode(PropertyAccessMode.Field);
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
        builder.Property(x => x.Cancelled).HasColumnName("cancelled").IsRequired().HasComment("Whether the unfulfilled sales order line was cancelled.");
        builder.Ignore(x => x.OpenQuantity);
        builder.Ignore(x => x.LineAmount);
    }
}

public sealed class SalesOrderChangeEntityTypeConfiguration : IEntityTypeConfiguration<SalesOrderChange>
{
    public void Configure(EntityTypeBuilder<SalesOrderChange> builder)
    {
        builder.ToTable("sales_order_changes", table => table.HasComment("Auditable sales order amendment and cancellation records."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseIdentityByDefaultColumn().HasComment("Sales order change audit row id.");
        builder.Property<SalesOrderId>("SalesOrderId").HasColumnName("sales_order_id").IsRequired().HasComment("Owning sales order id.");
        builder.Property(x => x.ChangeType).HasColumnName("change_type").IsRequired().HasMaxLength(50).HasComment("Change category: amend, cancel-line, or cancel.");
        builder.Property(x => x.LineNo).HasColumnName("line_no").HasMaxLength(100).HasComment("Optional sales order line number affected by the change.");
        builder.Property(x => x.Reason).HasColumnName("reason").IsRequired().HasMaxLength(1000).HasComment("Business reason for the sales order change.");
        builder.Property(x => x.ChangedAtUtc).HasColumnName("changed_at_utc").IsRequired().HasComment("UTC time when the sales order change was applied.");
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
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasMaxLength(50).HasComment("ERP delivery order lifecycle status projected from WMS execution facts.");
        builder.Property(x => x.ReleasedAtUtc).HasColumnName("released_at_utc").IsRequired().HasComment("UTC release time.");
        builder.Property(x => x.CancelledAtUtc).HasColumnName("cancelled_at_utc").HasComment("UTC time when WMS cancellation was projected to ERP.");
        builder.Property(x => x.CancellationReason).HasColumnName("cancellation_reason").HasMaxLength(1000).HasComment("WMS cancellation reason projected to ERP delivery order.");
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
        builder.Property(x => x.DebitNoteAmount).HasColumnName("debit_note_amount").IsRequired().HasPrecision(18, 6).HasComment("Applied supplier debit-note amount.");
        builder.Property(x => x.LocalAmount).HasColumnName("local_amount").IsRequired().HasPrecision(18, 6).HasComment("Local currency amount at document exchange rate.");
        builder.Property(x => x.LocalPaidAmount).HasColumnName("local_paid_amount").IsRequired().HasPrecision(18, 6).HasComment("Local currency paid amount at document exchange rate.");
        builder.Property(x => x.LocalDebitNoteAmount).HasColumnName("local_debit_note_amount").IsRequired().HasPrecision(18, 6).HasComment("Applied supplier debit-note local amount.");
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
        builder.Property(x => x.CreditNoteAmount).HasColumnName("credit_note_amount").IsRequired().HasPrecision(18, 6).HasComment("Applied customer credit-note amount.");
        builder.Property(x => x.LocalAmount).HasColumnName("local_amount").IsRequired().HasPrecision(18, 6).HasComment("Local currency amount at document exchange rate.");
        builder.Property(x => x.LocalCollectedAmount).HasColumnName("local_collected_amount").IsRequired().HasPrecision(18, 6).HasComment("Local currency collected amount at document exchange rate.");
        builder.Property(x => x.LocalCreditNoteAmount).HasColumnName("local_credit_note_amount").IsRequired().HasPrecision(18, 6).HasComment("Applied customer credit-note local amount.");
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
        builder.Property(x => x.ExchangeRate).HasColumnName("exchange_rate").IsRequired().HasPrecision(18, 8).HasComment("Candidate exchange rate to local currency.");
        builder.Property(x => x.LocalAmount).HasColumnName("local_amount").IsRequired().HasPrecision(18, 6).HasComment("Candidate amount in local currency.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC creation time.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.CandidateNo }).IsUnique();
    }
}

public sealed class AccountingPeriodEntityTypeConfiguration : IEntityTypeConfiguration<AccountingPeriod>
{
    public void Configure(EntityTypeBuilder<AccountingPeriod> builder)
    {
        builder.ToTable("accounting_periods", table => table.HasComment("ERP accounting period open and close control."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Accounting period aggregate id.");
        PurchaseRequisitionEntityTypeConfiguration.AddTenantColumns(builder);
        builder.Property(x => x.PeriodCode).HasColumnName("period_code").IsRequired().HasMaxLength(50).HasComment("Accounting period code such as fiscal month.");
        builder.Property(x => x.StartDate).HasColumnName("start_date").IsRequired().HasComment("Inclusive period start date.");
        builder.Property(x => x.EndDate).HasColumnName("end_date").IsRequired().HasComment("Inclusive period end date.");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasConversion<string>().HasMaxLength(50).HasComment("Accounting period status.");
        builder.Property(x => x.OpenedAtUtc).HasColumnName("opened_at_utc").IsRequired().HasComment("UTC time when period was opened.");
        builder.Property(x => x.ClosedAtUtc).HasColumnName("closed_at_utc").HasComment("UTC time when period was closed.");
        builder.Property(x => x.ClosedBy).HasColumnName("closed_by").HasMaxLength(100).HasComment("User or service that closed the period.");
        builder.Property(x => x.CloseReason).HasColumnName("close_reason").HasMaxLength(500).HasComment("Auditable close reason.");
        builder.Property(x => x.ReopenedAtUtc).HasColumnName("reopened_at_utc").HasComment("UTC time when period was reopened for exception handling.");
        builder.Property(x => x.ReopenedBy).HasColumnName("reopened_by").HasMaxLength(100).HasComment("User or service that reopened the period.");
        builder.Property(x => x.ReopenReason).HasColumnName("reopen_reason").HasMaxLength(500).HasComment("Auditable reopen or exception reason.");
        builder.Ignore(x => x.CanPost);
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.PeriodCode }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.StartDate, x.EndDate });
    }
}

public sealed class PaymentExecutionEntityTypeConfiguration : IEntityTypeConfiguration<PaymentExecution>
{
    public void Configure(EntityTypeBuilder<PaymentExecution> builder)
    {
        builder.ToTable("payment_executions", table => table.HasComment("ERP payment execution document for AP settlement."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Payment execution aggregate id.");
        PurchaseRequisitionEntityTypeConfiguration.AddTenantColumns(builder);
        builder.Property(x => x.PaymentExecutionNo).HasColumnName("payment_execution_no").IsRequired().HasMaxLength(100).HasComment("Payment execution document number.");
        builder.Property(x => x.SupplierCode).HasColumnName("supplier_code").IsRequired().HasMaxLength(100).HasComment("MasterData supplier code.");
        builder.Property(x => x.Amount).HasColumnName("amount").IsRequired().HasPrecision(18, 6).HasComment("Payment amount.");
        builder.Property(x => x.CurrencyCode).HasColumnName("currency_code").IsRequired().HasMaxLength(10).HasComment("Payment currency code.");
        builder.Property(x => x.PaymentExchangeRate).HasColumnName("payment_exchange_rate").IsRequired().HasPrecision(18, 8).HasComment("Payment currency exchange rate to local currency.");
        builder.Property(x => x.PaymentDate).HasColumnName("payment_date").IsRequired().HasComment("Payment execution date.");
        builder.Property(x => x.CashAccountCode).HasColumnName("cash_account_code").IsRequired().HasMaxLength(100).HasComment("Cash or bank account code used by payment.");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasConversion<string>().HasMaxLength(50).HasComment("Payment execution status.");
        builder.Property(x => x.ApprovedBy).HasColumnName("approved_by").IsRequired().HasMaxLength(100).HasComment("Approver user or service.");
        builder.Property(x => x.ApprovedAtUtc).HasColumnName("approved_at_utc").IsRequired().HasComment("UTC approval time.");
        builder.Property(x => x.ExecutedBy).HasColumnName("executed_by").HasMaxLength(100).HasComment("Executor user or service.");
        builder.Property(x => x.ExecutedAtUtc).HasColumnName("executed_at_utc").HasComment("UTC execution time.");
        builder.HasMany(x => x.Allocations).WithOne().HasForeignKey("PaymentExecutionId").OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Allocations).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.PaymentExecutionNo }).IsUnique();
    }
}

public sealed class PaymentExecutionAllocationEntityTypeConfiguration : IEntityTypeConfiguration<PaymentExecutionAllocation>
{
    public void Configure(EntityTypeBuilder<PaymentExecutionAllocation> builder)
    {
        builder.ToTable("payment_execution_allocations", table => table.HasComment("ERP payment execution allocation to AP documents."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Payment execution allocation id.");
        builder.Property<PaymentExecutionId>("PaymentExecutionId").HasColumnName("payment_execution_id").IsRequired().HasComment("Owning payment execution id.");
        builder.Property(x => x.PayableNo).HasColumnName("payable_no").IsRequired().HasMaxLength(100).HasComment("Allocated AP document number.");
        builder.Property(x => x.Amount).HasColumnName("amount").IsRequired().HasPrecision(18, 6).HasComment("Allocated payment amount.");
    }
}

public sealed class CashReceiptEntityTypeConfiguration : IEntityTypeConfiguration<CashReceipt>
{
    public void Configure(EntityTypeBuilder<CashReceipt> builder)
    {
        builder.ToTable("cash_receipts", table => table.HasComment("ERP cash receipt document for AR matching."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Cash receipt aggregate id.");
        PurchaseRequisitionEntityTypeConfiguration.AddTenantColumns(builder);
        builder.Property(x => x.CashReceiptNo).HasColumnName("cash_receipt_no").IsRequired().HasMaxLength(100).HasComment("Cash receipt document number.");
        builder.Property(x => x.CustomerCode).HasColumnName("customer_code").IsRequired().HasMaxLength(100).HasComment("MasterData customer code.");
        builder.Property(x => x.Amount).HasColumnName("amount").IsRequired().HasPrecision(18, 6).HasComment("Receipt amount.");
        builder.Property(x => x.CurrencyCode).HasColumnName("currency_code").IsRequired().HasMaxLength(10).HasComment("Receipt currency code.");
        builder.Property(x => x.ReceiptDate).HasColumnName("receipt_date").IsRequired().HasComment("Cash receipt date.");
        builder.Property(x => x.CashAccountCode).HasColumnName("cash_account_code").IsRequired().HasMaxLength(100).HasComment("Cash or bank account code used by receipt.");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasConversion<string>().HasMaxLength(50).HasComment("Cash receipt status.");
        builder.Property(x => x.RegisteredAtUtc).HasColumnName("registered_at_utc").IsRequired().HasComment("UTC registration time.");
        builder.Property(x => x.MatchedAtUtc).HasColumnName("matched_at_utc").HasComment("UTC matching time.");
        builder.HasMany(x => x.Allocations).WithOne().HasForeignKey("CashReceiptId").OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Allocations).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.CashReceiptNo }).IsUnique();
    }
}

public sealed class CashReceiptAllocationEntityTypeConfiguration : IEntityTypeConfiguration<CashReceiptAllocation>
{
    public void Configure(EntityTypeBuilder<CashReceiptAllocation> builder)
    {
        builder.ToTable("cash_receipt_allocations", table => table.HasComment("ERP cash receipt allocation to AR documents."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Cash receipt allocation id.");
        builder.Property<CashReceiptId>("CashReceiptId").HasColumnName("cash_receipt_id").IsRequired().HasComment("Owning cash receipt id.");
        builder.Property(x => x.ReceivableNo).HasColumnName("receivable_no").IsRequired().HasMaxLength(100).HasComment("Allocated AR document number.");
        builder.Property(x => x.Amount).HasColumnName("amount").IsRequired().HasPrecision(18, 6).HasComment("Allocated receipt amount.");
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
