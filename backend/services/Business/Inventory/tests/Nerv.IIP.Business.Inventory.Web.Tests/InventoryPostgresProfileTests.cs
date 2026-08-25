using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Nerv.IIP.Business.Inventory.Domain;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLedgerAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockMovementAggregate;
using Nerv.IIP.Business.Inventory.Infrastructure;
using Nerv.IIP.Business.Inventory.Web.Application.Queries;

namespace Nerv.IIP.Business.Inventory.Web.Tests;

[Collection(InventoryPostgresLaneCollection.Name)]
public sealed class InventoryPostgresProfileTests
{
    private const string PreviousMigrationId = "20260731141027_AddStockMovementRequestedUnitCost";
    private const string AuthorityPendingMigrationId = "20260825202829_AddInventoryAuthorityResolutionPendingAudit";

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
            Assert.True(await AuthorityPendingTableExistsAsync(db));
            Assert.Equal(
                ["id", "event_id", "idempotency_key", "reason_code", "status", "observed_at_utc"],
                await AuthorityPendingColumnsAsync(db));
            Assert.True(await AuthorityPendingConstraintExistsAsync(db, "ck_authority_resolution_pending_audits_status"));
            Assert.True(await AuthorityPendingConstraintExistsAsync(db, "ck_authority_resolution_pending_audits_event_id"));
            Assert.True(await AuthorityPendingIndexExistsAsync(db, "ux_authority_resolution_pending_audits_event_id"));

            var migrator = db.Database.GetService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigrationId);
            Assert.False(await AuthorityPendingTableExistsAsync(db));
            await migrator.MigrateAsync(AuthorityPendingMigrationId);
            Assert.True(await AuthorityPendingTableExistsAsync(db));

            db.AuthorityResolutionPendingAudits.Add(
                new InventoryAuthorityResolutionPendingAudit(
                    "evt-postgres-authority-pending-001",
                    "idem-postgres-authority-pending-001",
                    "authority-not-ready",
                    DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();

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

        using (var firstScope = provider.CreateScope())
        using (var secondScope = provider.CreateScope())
        {
            using var barrier = new Barrier(2);
            var outcomes = await Task.WhenAll(
                Task.Run(() => TryInsertConcurrentPendingAsync(
                    firstScope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
                    barrier,
                    "evt-postgres-authority-pending-concurrent-001")),
                Task.Run(() => TryInsertConcurrentPendingAsync(
                    secondScope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
                    barrier,
                    "evt-postgres-authority-pending-concurrent-001")));

            Assert.Contains(true, outcomes);
            Assert.Contains(false, outcomes);
        }

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(
                2,
                await db.AuthorityResolutionPendingAudits
                    .CountAsync(x => x.EventId.StartsWith("evt-postgres-authority-pending")));
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

    private static async Task<bool> AuthorityPendingTableExistsAsync(ApplicationDbContext db)
    {
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.tables
                WHERE table_schema = 'inventory'
                  AND table_name = 'authority_resolution_pending_audits'
            )
            """;
        return (bool?)await command.ExecuteScalarAsync() ?? false;
    }

    private static async Task<string[]> AuthorityPendingColumnsAsync(ApplicationDbContext db)
    {
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT column_name
            FROM information_schema.columns
            WHERE table_schema = 'inventory'
              AND table_name = 'authority_resolution_pending_audits'
            ORDER BY ordinal_position
            """;
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new List<string>();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0));
        }

        return [.. columns];
    }

    private static async Task<bool> AuthorityPendingConstraintExistsAsync(
        ApplicationDbContext db,
        string constraintName)
    {
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.table_constraints
                WHERE table_schema = 'inventory'
                  AND table_name = 'authority_resolution_pending_audits'
                  AND constraint_name = @constraint_name
            )
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "constraint_name";
        parameter.Value = constraintName;
        command.Parameters.Add(parameter);
        return (bool?)await command.ExecuteScalarAsync() ?? false;
    }

    private static async Task<bool> AuthorityPendingIndexExistsAsync(
        ApplicationDbContext db,
        string indexName)
    {
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM pg_indexes
                WHERE schemaname = 'inventory'
                  AND tablename = 'authority_resolution_pending_audits'
                  AND indexname = @index_name
            )
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "index_name";
        parameter.Value = indexName;
        command.Parameters.Add(parameter);
        return (bool?)await command.ExecuteScalarAsync() ?? false;
    }

    private static async Task<bool> TryInsertConcurrentPendingAsync(
        ApplicationDbContext db,
        Barrier barrier,
        string eventId)
    {
        if (!barrier.SignalAndWait(TimeSpan.FromSeconds(10)))
        {
            throw new TimeoutException("Concurrent pending-audit inserts did not reach the synchronization barrier.");
        }
        db.AuthorityResolutionPendingAudits.Add(
            new InventoryAuthorityResolutionPendingAudit(
                eventId,
                "idem-postgres-authority-pending-concurrent-001",
                "authority-not-ready",
                DateTimeOffset.UtcNow));
        try
        {
            await db.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            return false;
        }
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
