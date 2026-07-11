using MediatR;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountPayableAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountReceivableAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountingPeriodAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.CashReceiptAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.CreditNoteAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.CostCandidateAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.DebitNoteAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.DeliveryOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.JournalVoucherAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.OpportunityAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PaymentExecutionAggregate;
using Nerv.IIP.Business.Erp.Domain;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseReceiptAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseReturnAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseRequisitionAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.QuotationAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.RequestForQuotationAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SalesOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SalesReturnAuthorizationAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SupplierInvoiceAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SupplierQuotationAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.GLAccountAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkOrderCostAggregate;
using NetCorePal.Extensions.DistributedTransactions.CAP.Persistence;
using Nerv.IIP.Coding;
using Nerv.IIP.Business.Erp.Infrastructure.IntegrationEvents;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Erp.Infrastructure;

public partial class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, IMediator mediator)
    : AppDbContextBase(options, mediator), IPostgreSqlCapDataStorage
{
    public DbSet<PurchaseRequisition> PurchaseRequisitions => Set<PurchaseRequisition>();
    public DbSet<RequestForQuotation> RequestForQuotations => Set<RequestForQuotation>();
    public DbSet<SupplierQuotation> SupplierQuotations => Set<SupplierQuotation>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseReceipt> PurchaseReceipts => Set<PurchaseReceipt>();
    public DbSet<PurchaseReturn> PurchaseReturns => Set<PurchaseReturn>();
    public DbSet<SupplierInvoice> SupplierInvoices => Set<SupplierInvoice>();
    public DbSet<Opportunity> Opportunities => Set<Opportunity>();
    public DbSet<Quotation> Quotations => Set<Quotation>();
    public DbSet<SalesOrder> SalesOrders => Set<SalesOrder>();
    public DbSet<SalesReturnAuthorization> SalesReturnAuthorizations => Set<SalesReturnAuthorization>();
    public DbSet<DeliveryOrder> DeliveryOrders => Set<DeliveryOrder>();
    public DbSet<AccountPayable> AccountPayables => Set<AccountPayable>();
    public DbSet<AccountReceivable> AccountReceivables => Set<AccountReceivable>();
    public DbSet<DebitNote> DebitNotes => Set<DebitNote>();
    public DbSet<CreditNote> CreditNotes => Set<CreditNote>();
    public DbSet<AccountingPeriod> AccountingPeriods => Set<AccountingPeriod>();
    public DbSet<PaymentExecution> PaymentExecutions => Set<PaymentExecution>();
    public DbSet<CashReceipt> CashReceipts => Set<CashReceipt>();
    public DbSet<JournalVoucher> JournalVouchers => Set<JournalVoucher>();
    public DbSet<CostCandidate> CostCandidates => Set<CostCandidate>();
    public DbSet<GLAccount> GLAccounts => Set<GLAccount>();
    public DbSet<WorkOrderCost> WorkOrderCosts => Set<WorkOrderCost>();
    public DbSet<WorkCenterCostRate> WorkCenterCostRates => Set<WorkCenterCostRate>();
    public DbSet<PendingMaterialCost> PendingMaterialCosts => Set<PendingMaterialCost>();
    public DbSet<CodeCounter> CodeCounters => Set<CodeCounter>();
    public DbSet<CodeIdempotencyKey> CodeIdempotencyKeys => Set<CodeIdempotencyKey>();
    public DbSet<ProcessedIntegrationEvent> ProcessedIntegrationEvents => Set<ProcessedIntegrationEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(ErpFacts.Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        modelBuilder.ConfigureIntegrationEventDeadLetters();
        ConfigureCapStorage(modelBuilder);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ConfigureStronglyTypedIdValueConverter(configurationBuilder);
        base.ConfigureConventions(configurationBuilder);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        return SaveChangesWithAccountLinkageAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private async Task<int> SaveChangesWithAccountLinkageAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken)
    {
        await EnsureAddedVoucherAccountsAsync(cancellationToken);
        return await ProcessedIntegrationEventInbox.SaveChangesOrIgnoreDuplicateAsync<ProcessedIntegrationEvent>(
            this,
            token => base.SaveChangesAsync(acceptAllChangesOnSuccess, token),
            cancellationToken);
    }

    private async Task EnsureAddedVoucherAccountsAsync(CancellationToken cancellationToken)
    {
        var required = ChangeTracker.Entries<JournalVoucherLine>()
            .Where(x => x.State == EntityState.Added)
            .Select(x => new { x.Entity.OrganizationId, x.Entity.EnvironmentId, x.Entity.AccountCode })
            .Distinct()
            .ToArray();
        foreach (var account in required)
        {
            var exists = await GLAccounts.AnyAsync(x => x.OrganizationId == account.OrganizationId && x.EnvironmentId == account.EnvironmentId && x.Code == account.AccountCode, cancellationToken);
            if (!exists && !GLAccounts.Local.Any(x => x.OrganizationId == account.OrganizationId && x.EnvironmentId == account.EnvironmentId && x.Code == account.AccountCode))
            {
                GLAccounts.Add(GLAccount.Create(account.OrganizationId, account.EnvironmentId, account.AccountCode, account.AccountCode, InferAccountType(account.AccountCode)));
            }
        }
    }

    private static GLAccountType InferAccountType(string accountCode) => accountCode.Length > 0 ? accountCode[0] switch
    {
        '1' => GLAccountType.Asset,
        '2' => GLAccountType.Liability,
        '3' => GLAccountType.Equity,
        '4' => GLAccountType.Revenue,
        _ => GLAccountType.Expense,
    } : GLAccountType.Expense;

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        return ProcessedIntegrationEventInbox.SaveChangesOrIgnoreDuplicate<ProcessedIntegrationEvent>(
            this,
            () => base.SaveChanges(acceptAllChangesOnSuccess));
    }

    private static void ConfigureCapStorage(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PublishedMessage>().ToTable("cap_published_messages").HasKey(x => x.Id);
        modelBuilder.Entity<ReceivedMessage>().ToTable("cap_received_messages").HasKey(x => x.Id);
        modelBuilder.Entity<CapLock>().ToTable("cap_locks").HasKey(x => x.Key);
    }

}
