using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Nerv.IIP.Business.Inventory.Domain;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLedgerAggregate;
using Nerv.IIP.Business.Inventory.Infrastructure;

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
            ledger.ApplyMovement(DomainMovementFactory.Inbound(10m));
            db.StockLedgers.Add(ledger);
            await db.SaveChangesAsync();
        }

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var ledger = await db.StockLedgers.SingleAsync();
            Assert.Equal(10m, ledger.OnHandQuantity);
            Assert.Equal(1, await db.StockMovements.CountAsync());
        }
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
