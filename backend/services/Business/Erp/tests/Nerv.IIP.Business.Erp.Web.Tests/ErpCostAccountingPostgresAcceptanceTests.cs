using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Nerv.IIP.Business.Erp.Domain;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.GLAccountAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.JournalVoucherAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkOrderCostAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;

namespace Nerv.IIP.Business.Erp.Web.Tests;

[Collection("ERP PostgreSQL acceptance")]
public sealed class ErpCostAccountingPostgresAcceptanceTests
{
    [ErpCostPostgresFact]
    public async Task PostgreSQL_migration_backfills_legacy_rate_and_enforces_revision_indexes()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES"), x => x.MigrationsHistoryTable("__EFMigrationsHistory", ErpFacts.Schema))
            .Options;
        await using var db = new ApplicationDbContext(options, new NoopMediator());
        await db.Database.OpenConnectionAsync();
        var quotedSchema = new NpgsqlCommandBuilder().QuoteIdentifier(ErpFacts.Schema);
        await using (var drop = db.Database.GetDbConnection().CreateCommand())
        {
            drop.CommandText = $"DROP SCHEMA IF EXISTS {quotedSchema} CASCADE";
            await drop.ExecuteNonQueryAsync();
        }

        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260720014936_AddDeliveryOrderConcurrencyToken");
        await using (var seed = new NpgsqlCommand($"""
            INSERT INTO {quotedSchema}.work_center_cost_rates
                (id, organization_id, environment_id, work_center_id, hourly_rate)
            VALUES
                (@id, 'org-legacy', 'env-legacy', 'WC-LEGACY', 37.5)
            """, (NpgsqlConnection)db.Database.GetDbConnection()))
        {
            seed.Parameters.AddWithValue("id", Guid.CreateVersion7());
            await seed.ExecuteNonQueryAsync();
        }

        await migrator.MigrateAsync();
        db.ChangeTracker.Clear();

        var legacy = await db.WorkCenterCostRates.SingleAsync();
        Assert.Equal(1, legacy.Revision);
        Assert.Equal("CNY", legacy.CurrencyCode);
        Assert.Equal(DateTimeOffset.UnixEpoch, legacy.EffectiveFromUtc);
        Assert.Null(legacy.EffectiveToUtc);
        Assert.Equal("system:migration", legacy.ChangedBy);
        Assert.Equal("legacy cost-rate migration", legacy.Reason);
        Assert.Equal(new DateTimeOffset(2026, 7, 23, 2, 54, 18, TimeSpan.Zero), legacy.ChangedAtUtc);

        var indexes = new Dictionary<string, string>(StringComparer.Ordinal);
        await using (var indexCommand = new NpgsqlCommand("""
            SELECT indexname, indexdef
            FROM pg_indexes
            WHERE schemaname = 'erp' AND tablename = 'work_center_cost_rates'
            """, (NpgsqlConnection)db.Database.GetDbConnection()))
        await using (var reader = await indexCommand.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) indexes.Add(reader.GetString(0), reader.GetString(1));
        }

        Assert.Contains("ux_work_center_cost_rates_scope_revision", indexes.Keys);
        Assert.Contains("UNIQUE", indexes["ux_work_center_cost_rates_scope_revision"], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ix_work_center_cost_rates_effective_lookup", indexes.Keys);
        Assert.DoesNotContain("IX_work_center_cost_rates_organization_id_environment_id_work_~", indexes.Keys);

        db.WorkCenterCostRates.AddRange(
            WorkCenterCostRate.Define("org-legacy", "env-legacy", "WC-LEGACY", 40m, "CNY", DateTimeOffset.UnixEpoch, null, 2, "system:test", "first concurrent candidate", DateTimeOffset.UtcNow),
            WorkCenterCostRate.Define("org-legacy", "env-legacy", "WC-LEGACY", 41m, "CNY", DateTimeOffset.UnixEpoch, null, 2, "system:test", "second concurrent candidate", DateTimeOffset.UtcNow));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [ErpCostPostgresFact]
    public async Task PostgreSQL_migration_enforces_gl_link_and_persists_reconciled_cost()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES"), x => x.MigrationsHistoryTable("__EFMigrationsHistory", ErpFacts.Schema))
            .Options;
        await using var db = new ApplicationDbContext(options, new NoopMediator());
        await db.Database.OpenConnectionAsync();
        var quotedSchema = new NpgsqlCommandBuilder().QuoteIdentifier(ErpFacts.Schema);
        await using (var command = db.Database.GetDbConnection().CreateCommand())
        {
            command.CommandText = $"DROP SCHEMA IF EXISTS {quotedSchema} CASCADE";
            await command.ExecuteNonQueryAsync();
        }
        await db.Database.MigrateAsync();

        db.GLAccounts.AddRange(
            GLAccount.Create("org-pg", "env-pg", "1405-WIP", "Work in process", GLAccountType.Asset),
            GLAccount.Create("org-pg", "env-pg", "1406-FINISHED-GOODS", "Finished goods", GLAccountType.Asset));
        db.JournalVouchers.Add(JournalVoucher.Post("org-pg", "env-pg", "JV-PG-001", new DateOnly(2026, 7, 11),
            [new JournalVoucherLineDraft("1406-FINISHED-GOODS", 160m, 0m, "capitalization"), new JournalVoucherLineDraft("1405-WIP", 0m, 160m, "clear WIP")]));
        var cost = WorkOrderCost.Open("org-pg", "env-pg", "WO-PG-001", "FG-PG-001");
        cost.RecordLabor("RPT-PG-001", "WC-PG", 2m, 50m, false, DateTimeOffset.UtcNow);
        cost.RecordMaterial("MOVE-PG-RM", "RPT-PG-001", "RM-PG", 3m, 20m, DateTimeOffset.UtcNow);
        cost.Complete(8m, 1, 1, DateTimeOffset.UtcNow);
        cost.Capitalize("MOVE-PG-FG", 8m, 20m, DateTimeOffset.UtcNow);
        cost.RecordWipClearance(160m);
        db.WorkOrderCosts.Add(cost);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        var persisted = await db.WorkOrderCosts.Include(x => x.Details).SingleAsync();
        Assert.Equal(160m, persisted.TotalAccumulatedCost);
        Assert.Equal(persisted.TotalAccumulatedCost, persisted.WipClearedCost);
        Assert.Equal(2, await db.JournalVouchers.SelectMany(x => x.Lines).CountAsync());
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification => Task.CompletedTask;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class ErpCostPostgresFactAttribute : FactAttribute
{
    public ErpCostPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")))
            Skip = "Set NERV_IIP_TEST_POSTGRES to run the real PostgreSQL ERP cost-accounting acceptance test.";
    }
}
