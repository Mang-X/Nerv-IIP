using MediatR;
using Microsoft.EntityFrameworkCore;
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
