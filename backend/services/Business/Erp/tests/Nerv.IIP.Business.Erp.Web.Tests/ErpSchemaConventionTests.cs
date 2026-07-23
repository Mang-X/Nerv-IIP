using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nerv.IIP.Business.Erp.Domain;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountPayableAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountReceivableAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountingPeriodAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.CashReceiptAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.CostCandidateAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.DeliveryOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.JournalVoucherAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.OpportunityAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PaymentExecutionAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseReceiptAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseRequisitionAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.QuotationAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.RequestForQuotationAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SalesOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SupplierInvoiceAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SupplierQuotationAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkOrderCostAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Infrastructure.IntegrationEvents;
using Nerv.IIP.Business.Erp.Infrastructure.MasterData;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Finance;
using Nerv.IIP.Coding;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Testing.EntityFramework;

namespace Nerv.IIP.Business.Erp.Web.Tests;

public sealed class ErpSchemaConventionTests
{
    [Fact]
    public void Runtime_PostgreSQL_profile_configures_migrations_history_schema()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddErpPostgreSqlPersistence("Host=localhost;Database=nerv_iip_erp_schema_conventions;Username=nerv;Password=nerv");

        using var fixture = new SchemaFixture(services.BuildServiceProvider());
        var failures = SchemaConventionAssertions.MigrationsHistoryTableIsInSchema(
            fixture.DbContext,
            ErpFacts.ServiceName,
            ErpFacts.Schema);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Erp_schema_metadata_follows_database_conventions()
    {
        using var fixture = CreateFixture();
        var businessEntities = new[]
        {
            typeof(PurchaseRequisition),
            typeof(RequestForQuotation),
            typeof(SupplierQuotation),
            typeof(PurchaseOrder),
            typeof(PurchaseReceipt),
            typeof(SupplierInvoice),
            typeof(Opportunity),
            typeof(Quotation),
            typeof(SalesOrder),
            typeof(DeliveryOrder),
            typeof(AccountPayable),
            typeof(AccountReceivable),
            typeof(AccountingPeriod),
            typeof(PaymentExecution),
            typeof(PaymentExecutionAllocation),
            typeof(CashReceipt),
            typeof(CashReceiptAllocation),
            typeof(JournalVoucher),
            typeof(CostCandidate),
            typeof(CodeCounter),
            typeof(CodeIdempotencyKey),
            typeof(ProcessedIntegrationEvent),
            typeof(BusinessPartnerAvailability),
            typeof(IntegrationEventDeadLetter),
            typeof(WorkCenterCostRate),
        };

        var failures = new List<string>();
        Assert.Equal(ErpFacts.Schema, fixture.DbContext.Model.GetDefaultSchema());
        failures.AddRange(SchemaConventionAssertions.BusinessTablesHaveComments(fixture.DbContext, ErpFacts.ServiceName, businessEntities));
        failures.AddRange(SchemaConventionAssertions.BusinessColumnsHaveComments(fixture.DbContext, ErpFacts.ServiceName, businessEntities));
        failures.AddRange(SchemaConventionAssertions.MigrationsHistoryTableIsInSchema(fixture.DbContext, ErpFacts.ServiceName, ErpFacts.Schema));
        failures.AddRange(ProcessedIntegrationEventHasUniqueInboxIndex(fixture.DbContext.Model));

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Finance_close_read_model_queries_translate_on_PostgreSQL_provider()
    {
        using var fixture = CreateFixture();
        var periodStart = new DateOnly(2026, 6, 1);
        var periodEnd = new DateOnly(2026, 6, 30);

        var trialBalanceSql = fixture.DbContext.JournalVouchers.AsNoTracking()
            .Where(x =>
                x.OrganizationId == "org-001"
                && x.EnvironmentId == "env-dev"
                && x.PostingDate >= periodStart
                && x.PostingDate <= periodEnd)
            .SelectMany(x => x.Lines)
            .GroupBy(x => x.AccountCode)
            .Select(x => new
            {
                AccountCode = x.Key,
                LocalDebitAmount = x.Sum(line => line.LocalDebitAmount),
                LocalCreditAmount = x.Sum(line => line.LocalCreditAmount),
            })
            .OrderBy(x => x.AccountCode)
            .ToQueryString();

        var grIrBalanceSql = fixture.DbContext.JournalVouchers.AsNoTracking()
            .Where(x =>
                x.OrganizationId == "org-001"
                && x.EnvironmentId == "env-dev"
                && x.PostingDate >= periodStart
                && x.PostingDate <= periodEnd)
            .SelectMany(x => x.Lines)
            .Where(x => x.AccountCode == FinanceVoucherFactory.GoodsReceiptInvoiceReceiptAccountCode)
            .Select(x => x.LocalDebitAmount - x.LocalCreditAmount)
            .ToQueryString();

        var unexecutedPaymentsSql = fixture.DbContext.PaymentExecutions.AsNoTracking()
            .Where(x =>
                x.OrganizationId == "org-001"
                && x.EnvironmentId == "env-dev"
                && x.PaymentDate >= periodStart
                && x.PaymentDate <= periodEnd
                && x.Status != PaymentExecutionStatus.Executed)
            .Select(x => x.PaymentExecutionNo)
            .ToQueryString();

        var unmatchedCashReceiptsSql = fixture.DbContext.CashReceipts.AsNoTracking()
            .Where(x =>
                x.OrganizationId == "org-001"
                && x.EnvironmentId == "env-dev"
                && x.ReceiptDate >= periodStart
                && x.ReceiptDate <= periodEnd
                && x.Status != CashReceiptStatus.Matched)
            .Select(x => x.CashReceiptNo)
            .ToQueryString();

        Assert.Contains("GROUP BY", trialBalanceSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("journal_voucher_lines", grIrBalanceSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("payment_executions", unexecutedPaymentsSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(nameof(PaymentExecutionStatus.Executed), unexecutedPaymentsSql, StringComparison.Ordinal);
        Assert.Contains("cash_receipts", unmatchedCashReceiptsSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(nameof(CashReceiptStatus.Matched), unmatchedCashReceiptsSql, StringComparison.Ordinal);
    }

    [Fact]
    public void Payment_execution_exchange_rate_migration_default_matches_domain_invariant()
    {
        using var fixture = CreateFixture();
        var migrations = fixture.DbContext.GetService<IMigrationsAssembly>();
        var migrationType = migrations.Migrations["20260707034335_AddErpPaymentExecutionExchangeRate"];
        var migration = migrations.CreateMigration(migrationType, fixture.DbContext.Database.ProviderName!);
        var addColumn = Assert.IsType<AddColumnOperation>(Assert.Single(migration.UpOperations, operation =>
            operation is AddColumnOperation add
            && add.Schema == ErpFacts.Schema
            && add.Table == "payment_executions"
            && add.Name == "payment_exchange_rate"));

        Assert.Equal(1m, addColumn.DefaultValue);
    }

    [Fact]
    public void Delivery_shipment_projection_migration_preserves_existing_delivery_rows()
    {
        using var fixture = CreateFixture();
        var migrations = fixture.DbContext.GetService<IMigrationsAssembly>();
        var migrationType = migrations.Migrations["20260719165003_AddErpDeliveryShipmentProjection"];
        var migration = migrations.CreateMigration(migrationType, fixture.DbContext.Database.ProviderName!);

        var shippedQuantity = Assert.IsType<AddColumnOperation>(Assert.Single(migration.UpOperations, operation =>
            operation is AddColumnOperation add
            && add.Schema == ErpFacts.Schema
            && add.Table == "delivery_order_lines"
            && add.Name == "shipped_quantity"));
        Assert.Equal(0m, shippedQuantity.DefaultValue);
        Assert.False(shippedQuantity.IsNullable);

        Assert.Contains(migration.UpOperations, operation =>
            operation is AddColumnOperation add
            && add.Schema == ErpFacts.Schema
            && add.Table == "delivery_orders"
            && add.Name == "shipped_at_utc"
            && add.IsNullable);
        Assert.Contains(migration.UpOperations, operation =>
            operation is AddColumnOperation add
            && add.Schema == ErpFacts.Schema
            && add.Table == "delivery_orders"
            && add.Name == "completed_at_utc"
            && add.IsNullable);
    }

    [Fact]
    public void Order_revision_versions_are_optimistic_concurrency_tokens()
    {
        using var fixture = CreateFixture();

        AssertVersionIsConcurrencyToken<PurchaseOrder>(fixture.DbContext.Model);
        AssertVersionIsConcurrencyToken<SalesOrder>(fixture.DbContext.Model);
    }

    [Fact]
    public void Order_revision_versions_are_optimistic_concurrency_tokens_in_migration_snapshot()
    {
        var snapshotType = typeof(ApplicationDbContext).Assembly.GetType(
            "Nerv.IIP.Business.Erp.Infrastructure.Migrations.ApplicationDbContextModelSnapshot",
            throwOnError: true);
        var snapshot = Assert.IsAssignableFrom<ModelSnapshot>(Activator.CreateInstance(snapshotType!)!);

        AssertVersionIsConcurrencyToken<PurchaseOrder>(snapshot.Model);
        AssertVersionIsConcurrencyToken<SalesOrder>(snapshot.Model);
    }

    [Fact]
    public void Erp_procurement_schema_does_not_own_inventory_balance_or_warehouse_execution()
    {
        using var fixture = CreateFixture();
        var forbiddenFragments = new[] { "stock_balance", "on_hand", "warehouse_task_state", "wcs_task" };
        var names = fixture.DbContext.Model.GetEntityTypes()
            .SelectMany(entity => new[] { entity.GetTableName() ?? string.Empty }.Concat(entity.GetProperties().Select(property => property.GetColumnName())))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();

        foreach (var fragment in forbiddenFragments)
        {
            Assert.DoesNotContain(names, name => name.Contains(fragment, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static IReadOnlyCollection<string> ProcessedIntegrationEventHasUniqueInboxIndex(IModel model)
    {
        var entity = model.FindEntityType(typeof(ProcessedIntegrationEvent));
        if (entity is null)
        {
            return [$"{ErpFacts.ServiceName}: missing processed integration event entity metadata."];
        }

        var hasUniqueIndex = entity.GetIndexes().Any(index =>
            index.IsUnique &&
            index.GetDatabaseName() == "ux_processed_integration_events_consumer_idempotency_key" &&
            index.Properties.Select(property => property.Name).SequenceEqual([
                nameof(ProcessedIntegrationEvent.ConsumerName),
                nameof(ProcessedIntegrationEvent.IdempotencyKey),
            ]));

        return hasUniqueIndex
            ? []
            : [$"{ErpFacts.ServiceName}: processed integration event inbox requires a unique consumer/idempotency key index."];
    }

    private static void AssertVersionIsConcurrencyToken<TEntity>(IModel model)
    {
        var entity = model.FindEntityType(typeof(TEntity));
        Assert.NotNull(entity);

        var version = entity.FindProperty("Version");
        Assert.NotNull(version);
        Assert.True(
            version.IsConcurrencyToken,
            $"{typeof(TEntity).Name}.Version must be configured as an optimistic concurrency token.");
    }

    private static SchemaFixture CreateFixture()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddErpPostgreSqlPersistence("Host=localhost;Database=nerv_iip_erp_schema_conventions;Username=nerv;Password=nerv");

        return new SchemaFixture(services.BuildServiceProvider());
    }

    private sealed class SchemaFixture : IDisposable
    {
        private readonly ServiceProvider serviceProvider;
        private readonly IServiceScope scope;

        public SchemaFixture(ServiceProvider serviceProvider)
        {
            this.serviceProvider = serviceProvider;
            scope = serviceProvider.CreateScope();
            DbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        }

        public ApplicationDbContext DbContext { get; }

        public void Dispose()
        {
            DbContext.Dispose();
            scope.Dispose();
            serviceProvider.Dispose();
        }
    }
}
