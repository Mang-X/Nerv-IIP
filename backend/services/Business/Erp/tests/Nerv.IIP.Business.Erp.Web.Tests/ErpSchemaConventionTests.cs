using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nerv.IIP.Business.Erp.Domain;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountPayableAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountReceivableAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.CostCandidateAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.DeliveryOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.JournalVoucherAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.OpportunityAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseReceiptAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseRequisitionAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.QuotationAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.RequestForQuotationAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SalesOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SupplierQuotationAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Infrastructure.Numbering;
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
            typeof(Opportunity),
            typeof(Quotation),
            typeof(SalesOrder),
            typeof(DeliveryOrder),
            typeof(AccountPayable),
            typeof(AccountReceivable),
            typeof(JournalVoucher),
            typeof(CostCandidate),
            typeof(NumberingCounter),
            typeof(NumberingIdempotencyKey),
        };

        var failures = new List<string>();
        Assert.Equal(ErpFacts.Schema, fixture.DbContext.Model.GetDefaultSchema());
        failures.AddRange(SchemaConventionAssertions.BusinessTablesHaveComments(fixture.DbContext, ErpFacts.ServiceName, businessEntities));
        failures.AddRange(SchemaConventionAssertions.BusinessColumnsHaveComments(fixture.DbContext, ErpFacts.ServiceName, businessEntities));
        failures.AddRange(SchemaConventionAssertions.MigrationsHistoryTableIsInSchema(fixture.DbContext, ErpFacts.ServiceName, ErpFacts.Schema));

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
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
