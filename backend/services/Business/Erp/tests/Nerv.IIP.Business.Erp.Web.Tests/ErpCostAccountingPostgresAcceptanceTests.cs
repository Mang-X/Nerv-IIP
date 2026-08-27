using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Nerv.IIP.Business.Erp.Domain;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.GLAccountAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.JournalVoucherAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkOrderCostAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Finance;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Testing;
using NetCorePal.Extensions.DependencyInjection;
using NetCorePal.Extensions.DistributedTransactions;
using NetCorePal.Extensions.Repository;
using NetCorePal.Extensions.Repository.EntityFrameworkCore;

namespace Nerv.IIP.Business.Erp.Web.Tests;

[Collection("ERP PostgreSQL acceptance")]
public sealed class ErpCostAccountingPostgresAcceptanceTests
{
    [ErpCostPostgresFact(Timeout = 30_000)]
    public async Task PostgreSQL_concurrent_report_and_actual_settlement_leave_only_actual_labor_active()
    {
        await ErpPostgresLaneDatabase.ResetSchemaAsync();
        var applicationName = $"erp-actual-labor-{Guid.CreateVersion7():N}";
        var connectionString = new NpgsqlConnectionStringBuilder(ErpPostgresLaneDatabase.ConnectionString)
        {
            ApplicationName = applicationName,
        }.ConnectionString;
        var options = ErpPostgresLaneDatabase.CreateOptions(connectionString);

        await using (var setupDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            ErpPostgresLaneDatabase.AssertUsesGovernedDatabase(setupDb);
            await setupDb.Database.MigrateAsync();
            setupDb.WorkCenterCostRates.Add(WorkCenterCostRate.Define(
                "org-concurrent", "env-concurrent", "WC-CONCURRENT", 80m, "CNY",
                new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero), null, 1,
                "system:test", "approved standard labor rate", DateTimeOffset.UtcNow));
            await setupDb.SaveChangesAsync();
        }

        await using var gateDb = new ApplicationDbContext(options, new NoopMediator());
        await using var gateTransaction = await gateDb.Database.BeginTransactionAsync();
        await new PostgreSqlWorkOrderCostMutationLock(gateDb)
            .AcquireAsync("org-concurrent", "env-concurrent", "WO-CONCURRENT", CancellationToken.None);

        await using var reportDb = new ApplicationDbContext(options, new NoopMediator());
        await using var settlementDb = new ApplicationDbContext(options, new NoopMediator());
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var reportedAtUtc = new DateTimeOffset(2026, 8, 31, 15, 40, 0, TimeSpan.Zero);
        var completedAtUtc = reportedAtUtc.AddMinutes(10);
        var report = new ProductionReportRecordedIntegrationEvent(
            "evt-report-concurrent", MesIntegrationEventTypes.ProductionReportRecorded, 1, reportedAtUtc,
            MesIntegrationEventSources.BusinessMes, "RPT-CONCURRENT", "WO-CONCURRENT",
            "org-concurrent", "env-concurrent", "operator:test", "report:RPT-CONCURRENT",
            new ProductionReportRecordedPayload(
                "RPT-CONCURRENT", "WO-CONCURRENT", "OP-CONCURRENT", "WC-CONCURRENT", null,
                10m, 0m, 0m, "ea", 5m, reportedAtUtc, false, MaterialMovementCount: 0));
        var settled = new MesOperationActualTimeSettledIntegrationEvent(
            "evt-settled-concurrent", MesIntegrationEventTypes.OperationActualTimeSettled, 1,
            completedAtUtc.AddMinutes(1), MesIntegrationEventSources.BusinessMes,
            "correlation-concurrent", "causation-concurrent", "org-concurrent", "env-concurrent",
            "operator:test", "actual-time:OP-CONCURRENT:1:settled",
            new OperationActualTimeSettledPayload(
                "WO-CONCURRENT", "OP-CONCURRENT", "WC-CONCURRENT", 1, completedAtUtc,
                2 * TimeSpan.TicksPerHour, 2 * TimeSpan.TicksPerHour, ["RPT-CONCURRENT"]));

        var reportTask = new ProductionReportRecordedIntegrationEventHandlerForAccumulateLaborCost(
                reportDb, deadLetters, reportDb, new PostgreSqlWorkOrderCostMutationLock(reportDb))
            .HandleAsync(report, CancellationToken.None);
        var settlementTask = new MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost(
                settlementDb, settlementDb, new PostgreSqlWorkOrderCostMutationLock(settlementDb),
                new OperationLaborSettlementOrchestrator(settlementDb, deadLetters))
            .HandleAsync(settled, CancellationToken.None);
        await WaitForAdvisoryLockWaitersAsync(connectionString, applicationName, expectedCount: 2);
        Assert.False(reportTask.IsCompleted);
        Assert.False(settlementTask.IsCompleted);

        await gateTransaction.CommitAsync();
        await Task.WhenAll(reportTask, settlementTask).WaitAsync(TimeSpan.FromSeconds(10));

        await using var assertDb = new ApplicationDbContext(options, new NoopMediator());
        var cost = await assertDb.WorkOrderCosts.Include(x => x.Details).SingleAsync();
        Assert.Equal(160m, cost.LaborCost);
        Assert.Equal(0m, cost.Details
            .Where(x => x.LaborBasis is LaborCostBasis.TheoreticalReport or LaborCostBasis.TheoreticalReportReplacement)
            .Sum(x => x.Amount));
        Assert.InRange(
            cost.Details.Count(x => x.LaborBasis == LaborCostBasis.TheoreticalReportReplacement),
            0,
            1);
        Assert.Single(cost.Details, x => x.LaborBasis == LaborCostBasis.ActualOperation);
        Assert.Single(await assertDb.OperationLaborSettlements.ToListAsync());
        Assert.Single(await assertDb.OperationLaborCoveredReports.ToListAsync());
    }

    [ErpCostPostgresFact(Timeout = 30_000)]
    public async Task PostgreSQL_second_concurrent_rate_command_blocks_then_observes_committed_revision()
    {
        await ErpPostgresLaneDatabase.ResetSchemaAsync();
        var applicationName = $"erp-rate-concurrency-{Guid.CreateVersion7():N}";
        var connectionStringBuilder = new NpgsqlConnectionStringBuilder(ErpPostgresLaneDatabase.ConnectionString)
        {
            ApplicationName = applicationName,
        };
        var connectionString = connectionStringBuilder.ConnectionString;
        var options = ErpPostgresLaneDatabase.CreateOptions(connectionString);

        await using (var setupDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            ErpPostgresLaneDatabase.AssertUsesGovernedDatabase(setupDb);
            await setupDb.Database.MigrateAsync();
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(configuration => configuration
            .RegisterServicesFromAssembly(typeof(ConfigureWorkCenterCostRateCommand).Assembly)
            .AddUnitOfWorkBehaviors());
        services.AddErpPostgreSqlPersistence(connectionString);
        await using var provider = services.BuildServiceProvider();
        await using var firstScope = provider.CreateAsyncScope();
        await using var secondScope = provider.CreateAsyncScope();
        var firstDb = firstScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var secondDb = secondScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.NotSame(firstDb, secondDb);

        await using var gateDb = new ApplicationDbContext(options, new NoopMediator());
        await using var gateTransaction = await gateDb.Database.BeginTransactionAsync();
        await new PostgreSqlErpAdvisoryLockAllocator(gateDb)
            .AcquireAsync(
                ErpAdvisoryLockDomain.WorkCenterLaborCostRate,
                "org-concurrent", "env-concurrent", "WC-CONCURRENT", CancellationToken.None);

        var effectiveFromUtc = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
        var changedAtUtc = new DateTimeOffset(2026, 7, 23, 8, 0, 0, TimeSpan.Zero);
        var firstCommand = new ConfigureWorkCenterCostRateCommand(
            " org-concurrent ",
            "env-concurrent",
            " WC-CONCURRENT ",
            40m,
            "CNY",
            effectiveFromUtc,
            null,
            "user:first",
            "first concurrent rate",
            changedAtUtc);
        var secondCommand = new ConfigureWorkCenterCostRateCommand(
            "org-concurrent",
            " env-concurrent ",
            "WC-CONCURRENT",
            45m,
            "CNY",
            effectiveFromUtc,
            null,
            "user:second",
            "second concurrent rate",
            changedAtUtc.AddSeconds(1));

        var firstSend = firstScope.ServiceProvider.GetRequiredService<ISender>().Send(firstCommand);
        var secondSend = secondScope.ServiceProvider.GetRequiredService<ISender>().Send(secondCommand);
        var gateReleased = false;
        try
        {
            await WaitForAdvisoryLockWaitersAsync(connectionString, applicationName, expectedCount: 2);
            Assert.False(firstSend.IsCompleted);
            Assert.False(secondSend.IsCompleted);
            await gateTransaction.CommitAsync();
            gateReleased = true;

            var ids = await Task.WhenAll(firstSend, secondSend).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(2, ids.Distinct().Count());
        }
        finally
        {
            if (!gateReleased)
            {
                await gateTransaction.RollbackAsync();
            }

            try
            {
                await Task.WhenAll(firstSend, secondSend).WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch
            {
                // Preserve the primary assertion/command failure while still observing both tasks.
            }
        }

        await using var assertDb = new ApplicationDbContext(options, new NoopMediator());
        var persisted = await assertDb.WorkCenterCostRates
            .Where(x => x.OrganizationId == "org-concurrent"
                && x.EnvironmentId == "env-concurrent"
                && x.WorkCenterId == "WC-CONCURRENT")
            .OrderBy(x => x.Revision)
            .ToListAsync();

        Assert.Collection(
            persisted,
            first => Assert.Equal(1, first.Revision),
            second => Assert.Equal(2, second.Revision));
        Assert.Equal([40m, 45m], persisted.Select(x => x.HourlyRate).OrderBy(x => x).ToArray());
    }

    [ErpCostPostgresFact]
    public async Task PostgreSQL_migration_backfills_legacy_rate_and_enforces_revision_indexes()
    {
        await ErpPostgresLaneDatabase.ResetSchemaAsync();
        var options = ErpPostgresLaneDatabase.CreateOptions();
        await using var db = new ApplicationDbContext(options, new NoopMediator());
        ErpPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
        await db.Database.OpenConnectionAsync();
        var quotedSchema = new NpgsqlCommandBuilder().QuoteIdentifier(ErpFacts.Schema);

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
        await ErpPostgresLaneDatabase.ResetSchemaAsync();
        var options = ErpPostgresLaneDatabase.CreateOptions();
        await using var db = new ApplicationDbContext(options, new NoopMediator());
        ErpPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
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

    [ErpCostPostgresFact]
    public async Task PostgreSQL_fault_after_save_rolls_back_settlement_inbox_lineage_and_cost()
    {
        await ErpPostgresLaneDatabase.ResetSchemaAsync();
        var options = ErpPostgresLaneDatabase.CreateOptions();
        await using var db = new ApplicationDbContext(options, new NoopMediator());
        await db.Database.MigrateAsync();
        db.WorkCenterCostRates.Add(WorkCenterCostRate.Define(
            "org-rollback", "env-rollback", "WC-ROLLBACK", 80m, "CNY",
            DateTimeOffset.Parse("2026-08-01T00:00:00Z"), null, 1,
            "system:test", "rollback rate", DateTimeOffset.Parse("2026-08-01T00:00:00Z")));
        await db.SaveChangesAsync();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var failingUnitOfWork = new SaveThenFailUnitOfWork(db);
        var settled = new MesOperationActualTimeSettledIntegrationEvent(
            "evt-rollback", MesIntegrationEventTypes.OperationActualTimeSettled, 1,
            DateTimeOffset.Parse("2026-08-31T16:00:00Z"), MesIntegrationEventSources.BusinessMes,
            "correlation-rollback", "causation-rollback", "org-rollback", "env-rollback",
            "operator:test", "actual-time:OP-ROLLBACK:1:settled",
            new OperationActualTimeSettledPayload(
                "WO-ROLLBACK", "OP-ROLLBACK", "WC-ROLLBACK", 1,
                DateTimeOffset.Parse("2026-08-31T15:50:00Z"),
                2 * TimeSpan.TicksPerHour, 2 * TimeSpan.TicksPerHour, ["RPT-ROLLBACK"]));
        var handler = new MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost(
            db, failingUnitOfWork, new PostgreSqlWorkOrderCostMutationLock(db),
            new OperationLaborSettlementOrchestrator(db, deadLetters));

        await Assert.ThrowsAsync<InjectedSaveFailureException>(
            () => handler.HandleAsync(settled, CancellationToken.None));

        await using var assertDb = new ApplicationDbContext(options, new NoopMediator());
        Assert.Empty(await assertDb.OperationLaborSettlements.ToListAsync());
        Assert.Empty(await assertDb.OperationLaborSettlementStates.ToListAsync());
        Assert.Empty(await assertDb.OperationLaborCoveredReports.ToListAsync());
        Assert.Empty(await assertDb.WorkOrderCosts.ToListAsync());
        Assert.Empty(await assertDb.ProcessedIntegrationEvents.ToListAsync());
    }

    [ErpCostPostgresFact]
    public async Task PostgreSQL_capitalized_settlement_reads_back_balanced_voucher_and_unique_lineage_indexes()
    {
        await ErpPostgresLaneDatabase.ResetSchemaAsync();
        var options = ErpPostgresLaneDatabase.CreateOptions();
        await using var db = new ApplicationDbContext(options, new NoopMediator());
        await db.Database.MigrateAsync();
        db.WorkCenterCostRates.Add(WorkCenterCostRate.Define(
            "org-cap", "env-cap", "WC-CAP", 80m, "CNY",
            DateTimeOffset.Parse("2026-08-01T00:00:00Z"), null, 1,
            "system:test", "capitalization rate", DateTimeOffset.Parse("2026-08-01T00:00:00Z")));
        var cost = WorkOrderCost.Open("org-cap", "env-cap", "WO-CAP", "FG-CAP");
        cost.RecordLabor("RPT-CAP", "WC-CAP", 2m, 80m, false, DateTimeOffset.Parse("2026-08-31T15:40:00Z"));
        cost.Complete(10m, 1, 0, DateTimeOffset.Parse("2026-08-31T15:50:00Z"));
        cost.Capitalize("MOVE-CAP", 10m, 16m, DateTimeOffset.Parse("2026-08-31T15:51:00Z"));
        cost.RecordWipClearance(160m);
        db.WorkOrderCosts.Add(cost);
        await db.SaveChangesAsync();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var settled = new MesOperationActualTimeSettledIntegrationEvent(
            "evt-cap", MesIntegrationEventTypes.OperationActualTimeSettled, 1,
            DateTimeOffset.Parse("2026-08-31T16:00:00Z"), MesIntegrationEventSources.BusinessMes,
            "correlation-cap", "causation-cap", "org-cap", "env-cap", "operator:test",
            "actual-time:OP-CAP:1:settled",
            new OperationActualTimeSettledPayload(
                "WO-CAP", "OP-CAP", "WC-CAP", 1, DateTimeOffset.Parse("2026-08-31T15:50:00Z"),
                90 * TimeSpan.TicksPerMinute, 90 * TimeSpan.TicksPerMinute, ["RPT-CAP"]));
        await new MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost(
                db, db, new PostgreSqlWorkOrderCostMutationLock(db),
                new OperationLaborSettlementOrchestrator(db, deadLetters))
            .HandleAsync(settled, CancellationToken.None);

        await using var assertDb = new ApplicationDbContext(options, new NoopMediator());
        var persistedCost = await assertDb.WorkOrderCosts.Include(x => x.Details).SingleAsync();
        var voucher = await assertDb.JournalVouchers.Include(x => x.Lines).SingleAsync();
        Assert.Equal(120m, persistedCost.LaborCost);
        Assert.Equal(120m, persistedCost.WipClearedCost);
        Assert.Equal(voucher.Lines.Sum(x => x.DebitAmount), voucher.Lines.Sum(x => x.CreditAmount));
        Assert.Equal(40m, voucher.Lines.Sum(x => x.DebitAmount));

        var indexes = await assertDb.Database.SqlQueryRaw<string>("""
            SELECT indexname AS "Value"
            FROM pg_indexes
            WHERE schemaname = 'erp'
              AND indexname IN (
                'ux_operation_labor_settlements_business_identity',
                'ux_operation_labor_settlement_voids_business_identity',
                'ux_operation_labor_covered_reports_report')
              AND indexdef ILIKE '%UNIQUE%'
            ORDER BY indexname
            """).ToListAsync();
        Assert.Equal(3, indexes.Count);
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

    private sealed class SaveThenFailUnitOfWork(ITransactionUnitOfWork inner) : ITransactionUnitOfWork
    {
        public IDbContextTransaction? CurrentTransaction
        {
            get => inner.CurrentTransaction;
            set => inner.CurrentTransaction = value;
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => inner.SaveChangesAsync(cancellationToken);

        public async Task<bool> SaveEntitiesAsync(CancellationToken cancellationToken = default)
        {
            _ = await ((IUnitOfWork)inner).SaveEntitiesAsync(cancellationToken);
            throw new InjectedSaveFailureException();
        }

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
            => inner.BeginTransactionAsync(cancellationToken);
        public Task CommitAsync(CancellationToken cancellationToken = default)
            => inner.CommitAsync(cancellationToken);
        public Task RollbackAsync(CancellationToken cancellationToken = default)
            => inner.RollbackAsync(cancellationToken);
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class InjectedSaveFailureException : Exception;

    private static async Task WaitForAdvisoryLockWaitersAsync(
        string connectionString,
        string applicationName,
        int expectedCount,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        // Opening the probe connection is a single operation that can hang, so it keeps its own explicit
        // budget instead of falling back to Npgsql's 15 s default. Caller cancellation propagates as-is;
        // only this helper's own budget turns into a TestTimeoutException.
        await TestTimeout.RunAsync(
            operation: $"open the advisory-lock probe connection for {applicationName}",
            action: async token => await connection.OpenAsync(token),
            timeout: TimeSpan.FromSeconds(10),
            cancellationToken,
            sensitiveValues: [connectionString]);
        await using var command = new NpgsqlCommand("""
            SELECT count(*)
            FROM pg_stat_activity
            WHERE application_name = @application_name
              AND wait_event_type = 'Lock'
              AND query LIKE 'SELECT pg_advisory_xact_lock%'
            """, connection);
        command.Parameters.AddWithValue("application_name", applicationName);

        // Real PostgreSQL: the only observable fact is pg_stat_activity, so poll it on a bounded budget.
        await Eventually.WaitAsync(
            condition: $"{expectedCount} PostgreSQL advisory-lock waiters for {applicationName}",
            observe: async token => Convert.ToInt32(await command.ExecuteScalarAsync(token)),
            isSatisfied: waitingCount => waitingCount >= expectedCount,
            describe: waitingCount => $"waiters={waitingCount}; expected>={expectedCount}",
            options: new EventuallyOptions(
                Timeout: TimeSpan.FromSeconds(10),
                PollInterval: TimeSpan.FromMilliseconds(50),
                SensitiveValues: [connectionString]),
            cancellationToken);
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
