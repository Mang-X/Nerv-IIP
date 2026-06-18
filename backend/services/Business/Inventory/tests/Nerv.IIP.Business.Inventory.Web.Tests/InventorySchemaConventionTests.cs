using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nerv.IIP.Business.Inventory.Domain;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockCountTaskAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockCountAdjustmentAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLedgerAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLocationAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockMovementAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockReservationAggregate;
using Nerv.IIP.Business.Inventory.Infrastructure;
using Nerv.IIP.Testing.EntityFramework;

namespace Nerv.IIP.Business.Inventory.Web.Tests;

public sealed class InventorySchemaConventionTests
{
    [Fact]
    public void Runtime_PostgreSQL_profile_configures_migrations_history_schema()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddInventoryPostgreSqlPersistence("Host=localhost;Database=nerv_iip_schema_conventions;Username=nerv;Password=nerv");

        using var fixture = new SchemaFixture(services.BuildServiceProvider());
        var failures = SchemaConventionAssertions.MigrationsHistoryTableIsInSchema(
            fixture.DbContext,
            InventoryFacts.ServiceName,
            InventoryFacts.Schema);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Inventory_schema_metadata_follows_database_conventions()
    {
        using var fixture = CreateFixture();
        var businessEntities = new[]
        {
            typeof(StockLocation),
            typeof(StockLedger),
            typeof(StockMovement),
            typeof(StockReservation),
            typeof(StockCountTask),
            typeof(StockCountAdjustment),
        };

        var failures = new List<string>();
        Assert.Equal(InventoryFacts.Schema, fixture.DbContext.Model.GetDefaultSchema());
        failures.AddRange(SchemaConventionAssertions.BusinessTablesHaveComments(fixture.DbContext, InventoryFacts.ServiceName, businessEntities));
        failures.AddRange(SchemaConventionAssertions.BusinessColumnsHaveComments(fixture.DbContext, InventoryFacts.ServiceName, businessEntities));
        failures.AddRange(SchemaConventionAssertions.MigrationsHistoryTableIsInSchema(fixture.DbContext, InventoryFacts.ServiceName, InventoryFacts.Schema));

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Inventory_key_codes_have_database_format_check_constraints()
    {
        using var fixture = CreateFixture();

        var expected = new Dictionary<Type, string[]>
        {
            [typeof(StockLocation)] =
            [
                "ck_stock_locations_location_code_format",
                "ck_stock_locations_site_code_format",
            ],
            [typeof(StockLedger)] =
            [
                "ck_stock_ledgers_location_code_format",
                "ck_stock_ledgers_sku_code_format",
                "ck_stock_ledgers_site_code_format",
                "ck_stock_ledgers_quality_status",
            ],
            [typeof(StockMovement)] =
            [
                "ck_stock_movements_location_code_format",
                "ck_stock_movements_sku_code_format",
                "ck_stock_movements_site_code_format",
                "ck_stock_movements_quality_status",
            ],
            [typeof(StockReservation)] =
            [
                "ck_stock_reservations_location_code_format",
                "ck_stock_reservations_sku_code_format",
                "ck_stock_reservations_site_code_format",
                "ck_stock_reservations_quality_status",
            ],
            [typeof(StockCountTask)] =
            [
                "ck_stock_count_tasks_location_code_format",
                "ck_stock_count_tasks_sku_code_format",
                "ck_stock_count_tasks_site_code_format",
                "ck_stock_count_tasks_quality_status",
            ],
            [typeof(StockCountAdjustment)] =
            [
                "ck_stock_count_adjustments_location_code_format",
                "ck_stock_count_adjustments_sku_code_format",
                "ck_stock_count_adjustments_site_code_format",
                "ck_stock_count_adjustments_quality_status",
            ],
        };

        foreach (var (entityType, constraintNames) in expected)
        {
            var model = fixture.DbContext.GetService<IDesignTimeModel>().Model;
            var constraints = model.FindEntityType(entityType)!.GetCheckConstraints().ToArray();
            foreach (var constraintName in constraintNames)
            {
                var constraint = Assert.Single(constraints, x => x.Name == constraintName);
                if (constraintName.EndsWith("_quality_status", StringComparison.Ordinal))
                {
                    Assert.Contains("quality_status in ('unrestricted','quality','blocked')", constraint.Sql, StringComparison.Ordinal);
                }
                else
                {
                    Assert.Contains("~ '^[A-Za-z0-9_.:-]+$'", constraint.Sql, StringComparison.Ordinal);
                }
            }
        }
    }

    private static SchemaFixture CreateFixture()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddInventoryPostgreSqlPersistence("Host=localhost;Database=nerv_iip_schema_conventions;Username=nerv;Password=nerv");

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
