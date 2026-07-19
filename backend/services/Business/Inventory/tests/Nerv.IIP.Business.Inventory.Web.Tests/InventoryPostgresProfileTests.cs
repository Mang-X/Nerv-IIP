using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Nerv.IIP.Business.Inventory.Domain;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLedgerAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockMovementAggregate;
using Nerv.IIP.Business.Inventory.Infrastructure;
using Nerv.IIP.Business.Inventory.Web.Application.Queries;

namespace Nerv.IIP.Business.Inventory.Web.Tests;

public sealed class InventoryPostgresProfileTests
{
    [InventoryPostgresFact]
    public async Task Postgres_store_persists_inventory_ledger_and_enforces_migrations_history_schema()
    {
        var connectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!;
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddInventoryPostgreSqlPersistence(connectionString);

        await using var provider = services.BuildServiceProvider();
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await DropInventorySchemaAsync(db);
            await db.Database.MigrateAsync();
            await AssertMigrationsHistoryTableInSchemaAsync(db, InventoryFacts.Schema);

            var ledger = StockLedger.Create(
                "org-001",
                "env-dev",
                "SKU-FG-1000",
                "kg",
                "SITE-01",
                "LOC-A-01",
                "LOT-001",
                null,
                "qualified",
                "company",
                "owner-001");
            var movement = ledger.ApplyMovement(DomainMovementFactory.Inbound(10m));
            db.StockLedgers.Add(ledger);
            db.StockMovements.Add(movement);

            AddExpiryLedger(db, new DateOnly(2026, 6, 25), new DateOnly(2026, 7, 25));
            AddExpiryLedger(db, new DateOnly(2026, 6, 26), new DateOnly(2026, 7, 26));
            await db.SaveChangesAsync();
        }

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var ledger = await db.StockLedgers.SingleAsync(x => x.SkuCode == "SKU-FG-1000");
            Assert.Equal(10m, ledger.OnHandQuantity);
            Assert.Equal(3, await db.StockMovements.CountAsync());

            var alerts = await new ListStockExpiryAlertsQueryHandler(db).Handle(
                new ListStockExpiryAlertsQuery(
                    "org-001",
                    "env-dev",
                    "SITE-01",
                    SkuCode: "SKU-EXPIRY",
                    AsOfDate: new DateOnly(2026, 7, 19)),
                CancellationToken.None);

            Assert.Equal(2, alerts.TotalCount);
            Assert.All(alerts.Items, item =>
            {
                Assert.False(item.CountAllowed);
                Assert.Equal("count-scope-ambiguous", item.CountBlockReasonCode);
            });
        }
    }

    private static void AddExpiryLedger(
        ApplicationDbContext db,
        DateOnly productionDate,
        DateOnly expiryDate)
    {
        var ledger = StockLedger.Create(
            "org-001",
            "env-dev",
            "SKU-EXPIRY",
            "kg",
            "SITE-01",
            "LOC-EXPIRY",
            "LOT-EXPIRY",
            null,
            "qualified",
            "company",
            "owner-001",
            ProductionDate: productionDate,
            ExpiryDate: expiryDate,
            ShelfLifeDays: 30,
            ExpiryDateSource: StockExpiryDateSource.Derived);
        var movement = ledger.ApplyMovement(StockMovement.Post(
            "org-001",
            "env-dev",
            "inbound",
            "postgres-profile",
            $"IN-{expiryDate:yyyyMMdd}",
            "LINE-001",
            $"idem-{expiryDate:yyyyMMdd}",
            "SKU-EXPIRY",
            "kg",
            "SITE-01",
            "LOC-EXPIRY",
            "LOT-EXPIRY",
            null,
            "qualified",
            "company",
            "owner-001",
            1m,
            ProductionDate: productionDate,
            ExpiryDate: expiryDate));
        db.StockLedgers.Add(ledger);
        db.StockMovements.Add(movement);
    }

    private static async Task DropInventorySchemaAsync(ApplicationDbContext db)
    {
        var quotedSchema = new NpgsqlCommandBuilder().QuoteIdentifier(InventoryFacts.Schema);
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"DROP SCHEMA IF EXISTS {quotedSchema} CASCADE";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AssertMigrationsHistoryTableInSchemaAsync(ApplicationDbContext db, string schema)
    {
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.tables
                WHERE table_schema = @schema
                  AND table_name = '__EFMigrationsHistory'
            )
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "schema";
        parameter.Value = schema;
        command.Parameters.Add(parameter);

        var exists = (bool?)await command.ExecuteScalarAsync() ?? false;
        Assert.True(exists, $"Expected EF migrations history table in schema '{schema}'.");
    }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class InventoryPostgresFactAttribute : FactAttribute
{
    public InventoryPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")))
        {
            Skip = "Set NERV_IIP_TEST_POSTGRES to run Inventory PostgreSQL profile tests.";
        }
    }
}
