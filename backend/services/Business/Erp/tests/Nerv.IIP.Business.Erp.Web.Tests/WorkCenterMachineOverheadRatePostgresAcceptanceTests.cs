using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountingPeriodAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.MachineOverheadReconciliationAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkCenterMachineOverheadRateAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkOrderCostAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Finance;
using Nerv.IIP.Business.Erp.Web.Application.Queries.Finance;
using Nerv.IIP.Testing;
using NetCorePal.Extensions.DependencyInjection;
using NetCorePal.Extensions.Primitives;
using Npgsql;

namespace Nerv.IIP.Business.Erp.Web.Tests;

[Collection(ErpPostgresLaneDatabase.CollectionName)]
public sealed class WorkCenterMachineOverheadRatePostgresAcceptanceTests
{
    [ErpCostPostgresFact(Timeout = 30_000)]
    public async Task Concurrent_commands_serialize_revision_allocation_on_postgres()
    {
        await ErpPostgresLaneDatabase.ResetSchemaAsync();
        var applicationName = $"erp-machine-rate-concurrency-{Guid.CreateVersion7():N}";
        var connectionString = new NpgsqlConnectionStringBuilder(ErpPostgresLaneDatabase.ConnectionString)
        {
            ApplicationName = applicationName,
        }.ConnectionString;
        var options = ErpPostgresLaneDatabase.CreateOptions(connectionString);
        await using (var setupDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            await setupDb.Database.MigrateAsync();
            setupDb.AccountingPeriods.Add(AccountingPeriod.Open(
                "org-concurrent", "env-concurrent", "2026-06",
                new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30)));
            await setupDb.SaveChangesAsync();
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(configuration => configuration
            .RegisterServicesFromAssembly(typeof(ConfigureWorkCenterMachineOverheadRateCommand).Assembly)
            .AddUnitOfWorkBehaviors());
        services.AddErpPostgreSqlPersistence(connectionString);
        await using var provider = services.BuildServiceProvider();
        await using var firstScope = provider.CreateAsyncScope();
        await using var secondScope = provider.CreateAsyncScope();

        await using var gateDb = new ApplicationDbContext(options, new NoopMediator());
        await using var gateTransaction = await gateDb.Database.BeginTransactionAsync();
        await new PostgreSqlErpAdvisoryLockAllocator(gateDb).AcquireAsync(
            ErpAdvisoryLockDomain.WorkCenterMachineOverheadRate,
            "org-concurrent", "env-concurrent", "WC-CONCURRENT", CancellationToken.None);

        var firstSend = firstScope.ServiceProvider.GetRequiredService<ISender>().Send(
            Command(30_000m, "user:first"));
        var secondSend = secondScope.ServiceProvider.GetRequiredService<ISender>().Send(
            Command(31_000m, "user:second"));
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
            if (!gateReleased) await gateTransaction.RollbackAsync();
            try
            {
                await Task.WhenAll(firstSend, secondSend).WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch
            {
                // Preserve the primary assertion or command failure while observing both tasks.
            }
        }

        await using var assertDb = new ApplicationDbContext(options, new NoopMediator());
        var revisions = await assertDb.WorkCenterMachineOverheadRates
            .Where(x => x.OrganizationId == "org-concurrent"
                && x.EnvironmentId == "env-concurrent"
                && x.WorkCenterId == "WC-CONCURRENT"
                && x.AccountingPeriodCode == "2026-06")
            .OrderBy(x => x.Revision)
            .Select(x => x.Revision)
            .ToArrayAsync();
        Assert.Equal(new[] { 1, 2 }, revisions);

        await using var thirdScope = provider.CreateAsyncScope();
        await using var fourthScope = provider.CreateAsyncScope();
        await using var reconciliationGateDb = new ApplicationDbContext(options, new NoopMediator());
        await using var reconciliationGateTransaction = await reconciliationGateDb.Database.BeginTransactionAsync();
        await new PostgreSqlErpAdvisoryLockAllocator(reconciliationGateDb).AcquireAsync(
            ErpAdvisoryLockDomain.WorkCenterMachineOverheadReconciliation,
            "org-concurrent", "env-concurrent", "2026-06\nWC-CONCURRENT", CancellationToken.None);

        var firstReconciliation = thirdScope.ServiceProvider.GetRequiredService<ISender>().Send(
            ReconciliationCommand(30_000m, "user:first"));
        var secondReconciliation = fourthScope.ServiceProvider.GetRequiredService<ISender>().Send(
            ReconciliationCommand(31_000m, "user:second"));
        var reconciliationGateReleased = false;
        try
        {
            await WaitForAdvisoryLockWaitersAsync(connectionString, applicationName, expectedCount: 2);
            Assert.False(firstReconciliation.IsCompleted);
            Assert.False(secondReconciliation.IsCompleted);
            await reconciliationGateTransaction.CommitAsync();
            reconciliationGateReleased = true;
            var ids = await Task.WhenAll(firstReconciliation, secondReconciliation)
                .WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(2, ids.Distinct().Count());
        }
        finally
        {
            if (!reconciliationGateReleased) await reconciliationGateTransaction.RollbackAsync();
            try
            {
                await Task.WhenAll(firstReconciliation, secondReconciliation)
                    .WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch
            {
                // Preserve the primary assertion or command failure while observing both tasks.
            }
        }

        assertDb.ChangeTracker.Clear();
        Assert.Equal(
            new[] { 1, 2 },
            await assertDb.WorkCenterMachineOverheadReconciliations
                .OrderBy(x => x.Revision)
                .Select(x => x.Revision)
                .ToArrayAsync());
    }

    [ErpCostPostgresFact]
    public async Task Migration_persists_monthly_rate_and_enforces_scope_period_revision_on_postgres()
    {
        await ErpPostgresLaneDatabase.ResetSchemaAsync();
        var options = ErpPostgresLaneDatabase.CreateOptions();
        await using var db = new ApplicationDbContext(options, new NoopMediator());
        ErpPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
        await db.Database.MigrateAsync();

        db.AccountingPeriods.AddRange(
            AccountingPeriod.Open(
                "org-pg", "env-pg", "2026-06", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30)),
            AccountingPeriod.Open(
                "org-other", "env-pg", "2026-06", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30)),
            AccountingPeriod.Open(
                "org-pg", "env-other", "2026-06", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30)),
            AccountingPeriod.Open(
                "org-pg", "env-pg", "2026-07", new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31)));
        db.WorkCenterMachineOverheadRates.Add(
            WorkCenterMachineOverheadRate.DefineApplicable(
                "org-pg", "env-pg", "WC-PG", "2026-06",
                30_000m, 10_000m, 1_000m, "CNY", 1,
                "system:test", "initial rate", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        var persisted = await db.WorkCenterMachineOverheadRates.SingleAsync();
        Assert.Equal(30m, persisted.FixedHourlyRate);
        Assert.Equal(10m, persisted.VariableHourlyRate);
        Assert.Equal(40m, persisted.TotalHourlyRate);

        var audit = await new ListWorkCenterMachineOverheadRatesQueryHandler(db).Handle(
            new ListWorkCenterMachineOverheadRatesQuery("org-pg", "env-pg", "WC-PG", "2026-06"),
            CancellationToken.None);
        Assert.Equal(1, audit.CurrentRevision);
        Assert.Equal("Applicable", Assert.Single(audit.Items).Applicability);

        db.WorkCenterMachineOverheadRates.AddRange(
            WorkCenterMachineOverheadRate.DefineApplicable(
                "org-pg", "env-pg", "WC-PG", "2026-06",
                31_000m, 10_000m, 1_000m, "CNY", 2,
                "system:test", "latest settlement rate", DateTimeOffset.UtcNow),
            WorkCenterMachineOverheadRate.DefineApplicable(
                "org-pg", "env-pg", "WC-ROUND", "2026-06",
                1m, 3m, 128m, "CNY", 1,
                "system:test", "bankers rounding vector", DateTimeOffset.UtcNow),
            WorkCenterMachineOverheadRate.DefineApplicable(
                "org-other", "env-pg", "WC-PG", "2026-06",
                99_000m, 0m, 1_000m, "CNY", 99,
                "system:test", "cross-organization distractor", DateTimeOffset.UtcNow),
            WorkCenterMachineOverheadRate.DefineApplicable(
                "org-pg", "env-other", "WC-PG", "2026-06",
                98_000m, 0m, 1_000m, "CNY", 98,
                "system:test", "cross-environment distractor", DateTimeOffset.UtcNow),
            WorkCenterMachineOverheadRate.DefineApplicable(
                "org-pg", "env-pg", "WC-OTHER", "2026-06",
                97_000m, 0m, 1_000m, "CNY", 97,
                "system:test", "cross-work-center distractor", DateTimeOffset.UtcNow),
            WorkCenterMachineOverheadRate.DefineApplicable(
                "org-pg", "env-pg", "WC-PG", "2026-07",
                96_000m, 0m, 1_000m, "CNY", 96,
                "system:test", "cross-period distractor", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var resolved = await new ResolveWorkCenterMachineOverheadRateForSettlementQueryHandler(db).Handle(
            new ResolveWorkCenterMachineOverheadRateForSettlementQuery(
                "org-pg", "env-pg", "WC-PG",
                new DateTimeOffset(2026, 6, 30, 23, 59, 59, TimeSpan.Zero)),
            CancellationToken.None);
        Assert.Equal("2026-06", resolved.AccountingPeriodCode);
        Assert.Equal(2, resolved.Revision);
        Assert.Equal(31m, resolved.FixedHourlyRate);

        var rounded = await db.WorkCenterMachineOverheadRates
            .SingleAsync(x => x.WorkCenterId == "WC-ROUND");
        Assert.Equal(0.007812m, rounded.FixedHourlyRate);
        Assert.Equal(0.023438m, rounded.VariableHourlyRate);
        Assert.Equal(0.031250m, rounded.TotalHourlyRate);

        db.WorkCenterMachineOverheadRates.Add(WorkCenterMachineOverheadRate.DefineNotApplicable(
            "org-pg", "env-pg", "WC-NOT-APPLICABLE", "2026-06", "CNY", 1,
            "system:test", "无机器费用", DateTimeOffset.UtcNow));
        db.WorkCenterCostRates.Add(WorkCenterCostRate.Define(
            "org-pg", "env-pg", "WC-LABOR-ONLY", 999m, "CNY",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), null, 1,
            "system:test", "人工费率干扰项", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var notApplicable = await new ResolveWorkCenterMachineOverheadRateForSettlementQueryHandler(db).Handle(
            new("org-pg", "env-pg", "WC-NOT-APPLICABLE", new(2026, 6, 15, 8, 0, 0, TimeSpan.Zero)),
            CancellationToken.None);
        Assert.Equal(nameof(MachineOverheadApplicability.NotApplicable), notApplicable.Applicability);
        Assert.Equal(0m, notApplicable.TotalHourlyRate);
        await Assert.ThrowsAsync<KnownException>(() =>
            new ResolveWorkCenterMachineOverheadRateForSettlementQueryHandler(db).Handle(
                new("org-pg", "env-pg", "WC-LABOR-ONLY", new(2026, 6, 15, 8, 0, 0, TimeSpan.Zero)),
                CancellationToken.None));

        var reconciliationRate = await db.WorkCenterMachineOverheadRates
            .SingleAsync(x => x.OrganizationId == "org-pg"
                && x.EnvironmentId == "env-pg"
                && x.WorkCenterId == "WC-PG"
                && x.AccountingPeriodCode == "2026-06"
                && x.Revision == 2);
        var activeState = AddMachineSettlement(db, reconciliationRate, "OP-PG", 1, 10);
        AddMachineSettlement(db, reconciliationRate, "OP-PG", 2, 20, activeState);
        AddMachineSettlement(db, await Rate("org-other", "env-pg", "WC-PG", "2026-06"), "OP-ORG", 1, 100);
        AddMachineSettlement(db, await Rate("org-pg", "env-other", "WC-PG", "2026-06"), "OP-ENV", 1, 100);
        AddMachineSettlement(db, await Rate("org-pg", "env-pg", "WC-OTHER", "2026-06"), "OP-WC", 1, 100);
        AddMachineSettlement(db, await Rate("org-pg", "env-pg", "WC-PG", "2026-07"), "OP-PERIOD", 1, 100);
        await db.SaveChangesAsync();

        await using (var currencyMismatchTransaction = await db.Database.BeginTransactionAsync())
        {
            AddMachineSettlement(db, reconciliationRate, "OP-CURRENCY", 1, 1, currencyCode: "USD");
            await db.SaveChangesAsync();
            await Assert.ThrowsAsync<KnownException>(() => new ReconcileWorkCenterMachineOverheadCommandHandler(
                db, new PostgreSqlErpAdvisoryLockAllocator(db)).Handle(
                new(
                    "org-pg", "env-pg", "WC-PG", "2026-06", 30_000m, 10_000m, "CNY",
                    0, AbnormalDowntimeDisposition.None,
                    "system:test", "ledger:currency-negative", "currency mismatch proof",
                    new DateTimeOffset(2026, 6, 30, 15, 0, 0, TimeSpan.Zero)),
                CancellationToken.None));
            await currencyMismatchTransaction.RollbackAsync();
        }
        db.ChangeTracker.Clear();
        reconciliationRate = await Rate("org-pg", "env-pg", "WC-PG", "2026-06");

        await using (var reconciliationTransaction = await db.Database.BeginTransactionAsync())
        {
            await new ReconcileWorkCenterMachineOverheadCommandHandler(
                db, new PostgreSqlErpAdvisoryLockAllocator(db)).Handle(
                new(
                    "org-pg", "env-pg", "WC-PG", "2026-06", 30_000m, 10_000m, "CNY",
                    8 * TimeSpan.TicksPerHour, AbnormalDowntimeDisposition.PeriodExpense,
                    "internal-service:finance-manager-a", "ledger:2026-06", "period reconciliation",
                    new DateTimeOffset(2026, 6, 30, 16, 0, 0, TimeSpan.Zero)),
                CancellationToken.None);
            await db.SaveChangesAsync();
            await reconciliationTransaction.CommitAsync();
        }
        db.ChangeTracker.Clear();
        var reconciliation = await db.WorkCenterMachineOverheadReconciliations.SingleAsync();
        Assert.Equal(20m, reconciliation.AppliedMachineHours);
        Assert.Equal(620m, reconciliation.AppliedFixedAmount);
        Assert.Equal(200m, reconciliation.AppliedVariableAmount);
        Assert.Equal(820m, reconciliation.AppliedTotalAmount);
        Assert.Equal(39_180m, reconciliation.UnderOverAppliedTotalAmount);
        Assert.Equal(29_380m, reconciliation.UnallocatedFixedOverheadAmount);
        Assert.Equal(8m, reconciliation.AbnormalDowntimeHours);
        Assert.Equal("internal-service:finance-manager-a", reconciliation.RecordedBy);
        Assert.Equal("ledger:2026-06", reconciliation.SourceReference);

        await using (var closeTransaction = await db.Database.BeginTransactionAsync())
        {
            await Assert.ThrowsAsync<KnownException>(() => new CloseAccountingPeriodCommandHandler(
                db, new PostgreSqlErpAdvisoryLockAllocator(db)).Handle(
                new("org-pg", "env-pg", "2026-06", "user:controller", "period close"),
                CancellationToken.None));
            await closeTransaction.RollbackAsync();
        }

        async Task<WorkCenterMachineOverheadRate> Rate(
            string organizationId, string environmentId, string workCenterId, string periodCode)
            => await db.WorkCenterMachineOverheadRates
                .Where(x => x.OrganizationId == organizationId
                    && x.EnvironmentId == environmentId
                    && x.WorkCenterId == workCenterId
                    && x.AccountingPeriodCode == periodCode)
                .OrderByDescending(x => x.Revision)
                .FirstAsync();

        db.AccountingPeriods.Add(AccountingPeriod.Open(
            "org-pg", "env-pg", "2026-OVERLAP", new(2026, 6, 15), new(2026, 7, 15)));
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<KnownException>(() =>
            new ResolveWorkCenterMachineOverheadRateForSettlementQueryHandler(db).Handle(
                new("org-pg", "env-pg", "WC-PG", new(2026, 6, 20, 8, 0, 0, TimeSpan.Zero)),
                CancellationToken.None));

        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();
        await using (var invalidCostBasis = new NpgsqlCommand("""
            INSERT INTO erp.work_center_machine_overhead_rates
                (id, organization_id, environment_id, work_center_id, accounting_period_code,
                 applicability, fixed_overhead_budget, variable_overhead_budget,
                 normal_capacity_machine_hours, fixed_hourly_rate, variable_hourly_rate,
                 total_hourly_rate, currency_code, revision, changed_by, reason, changed_at_utc)
            VALUES
                (@id, 'org-pg', 'env-pg', 'WC-PG', '2026-06',
                 'Applicable', -1, 10000, 1000, -0.001, 10, 9.999,
                 'CNY', 2, 'system:test', 'invalid negative budget', now())
            """, connection))
        {
            invalidCostBasis.Parameters.AddWithValue("id", Guid.CreateVersion7());
            var exception = await Assert.ThrowsAsync<PostgresException>(() => invalidCostBasis.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        }

        await using (var invalidReconciliation = new NpgsqlCommand("""
            INSERT INTO erp.work_center_machine_overhead_reconciliations
                (id, organization_id, environment_id, work_center_id, accounting_period_code,
                 work_center_machine_overhead_rate_id, rate_revision, currency_code,
                 actual_fixed_overhead_amount, actual_variable_overhead_amount, actual_total_overhead_amount,
                 applied_machine_ticks, applied_machine_hours, applied_fixed_amount, applied_variable_amount,
                 applied_total_amount, applied_rounding_difference_amount,
                 under_over_applied_fixed_amount, under_over_applied_variable_amount,
                 under_over_applied_total_amount, unallocated_fixed_overhead_amount,
                 over_applied_fixed_overhead_amount, abnormal_downtime_ticks, abnormal_downtime_hours,
                 abnormal_downtime_disposition, revision, recorded_by, source_reference, reason, recorded_at_utc)
            VALUES
                (@id, 'org-pg', 'env-pg', 'WC-PG', '2026-06', @rate_id, 2, 'CNY',
                 30000, 10000, 40001, 0, 0, 0, 0, 0, 0, 30000, 10000, 40000,
                 30000, 0, 0, 0, 'None', 2, 'user:test', 'bad-ledger', 'invalid total', now())
            """, connection))
        {
            invalidReconciliation.Parameters.AddWithValue("id", Guid.CreateVersion7());
            invalidReconciliation.Parameters.AddWithValue("rate_id", reconciliationRate.Id.Id);
            var exception = await Assert.ThrowsAsync<PostgresException>(() => invalidReconciliation.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        }

        await AssertCloneRejectedAsync(
            connection,
            reconciliation.Id.Id,
            """{"abnormal_downtime_ticks":36000000000,"abnormal_downtime_hours":1,"abnormal_downtime_disposition":"None","revision":2}""",
            PostgresErrorCodes.CheckViolation);
        await AssertCloneRejectedAsync(
            connection,
            reconciliation.Id.Id,
            """{"revision":0}""",
            PostgresErrorCodes.CheckViolation);
        await AssertCloneRejectedAsync(
            connection,
            reconciliation.Id.Id,
            """{"revision":1}""",
            PostgresErrorCodes.UniqueViolation);
        await AssertCloneRejectedAsync(
            connection,
            reconciliation.Id.Id,
            """{"organization_id":"org-other","revision":2}""",
            PostgresErrorCodes.ForeignKeyViolation);
        await AssertCloneRejectedAsync(
            connection,
            reconciliation.Id.Id,
            """{"environment_id":"env-other","revision":2}""",
            PostgresErrorCodes.ForeignKeyViolation);
        await AssertCloneRejectedAsync(
            connection,
            reconciliation.Id.Id,
            """{"work_center_id":"WC-OTHER","revision":2}""",
            PostgresErrorCodes.ForeignKeyViolation);
        await AssertCloneRejectedAsync(
            connection,
            reconciliation.Id.Id,
            """{"accounting_period_code":"2026-07","revision":2}""",
            PostgresErrorCodes.ForeignKeyViolation);
        await AssertCloneRejectedAsync(
            connection,
            reconciliation.Id.Id,
            """{"rate_revision":999,"revision":2}""",
            PostgresErrorCodes.ForeignKeyViolation);
        await AssertCloneRejectedAsync(
            connection,
            reconciliation.Id.Id,
            $$"""{"work_center_machine_overhead_rate_id":"{{Guid.CreateVersion7()}}","revision":2}""",
            PostgresErrorCodes.ForeignKeyViolation);

        var crossScopeRate = await Rate("org-other", "env-pg", "WC-PG", "2026-06");
        db.WorkCenterMachineOverheadReconciliations.Add(WorkCenterMachineOverheadReconciliation.Record(
            "org-other", "env-pg", "WC-PG", "2026-06",
            crossScopeRate.Id, crossScopeRate.Revision, "CNY",
            1m, 1m, 0, 0m, 0m, 0m, 0, AbnormalDowntimeDisposition.None,
            1, "system:test", "ledger:cross-scope", "same revision in another organization",
            new DateTimeOffset(2026, 6, 30, 16, 30, 0, TimeSpan.Zero)));
        await db.SaveChangesAsync();
        Assert.Equal(2, await db.WorkCenterMachineOverheadReconciliations.CountAsync(x => x.Revision == 1));

        var fractional = WorkCenterMachineOverheadReconciliation.Record(
            "org-pg", "env-pg", "WC-PG", "2026-06",
            reconciliationRate.Id, reconciliationRate.Revision, "CNY",
            1.0000005m, 2.0000015m, 1,
            0.1000005m, 0.2000015m, 0.3000035m,
            1, AbnormalDowntimeDisposition.PeriodExpense,
            2, "system:test", "ledger:fractional", "provider precision proof",
            new DateTimeOffset(2026, 6, 30, 17, 0, 0, TimeSpan.Zero));
        db.WorkCenterMachineOverheadReconciliations.Add(fractional);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var fractionalReadback = await db.WorkCenterMachineOverheadReconciliations.SingleAsync(x => x.Id == fractional.Id);
        Assert.Equal(1.000000m, fractionalReadback.ActualFixedOverheadAmount);
        Assert.Equal(2.000002m, fractionalReadback.ActualVariableOverheadAmount);
        Assert.Equal(3.000002m, fractionalReadback.ActualTotalOverheadAmount);
        Assert.Equal(0.100000m, fractionalReadback.AppliedFixedAmount);
        Assert.Equal(0.200002m, fractionalReadback.AppliedVariableAmount);
        Assert.Equal(0.300004m, fractionalReadback.AppliedTotalAmount);
        Assert.Equal(0.000002m, fractionalReadback.AppliedRoundingDifferenceAmount);
        Assert.Equal(0.900000m, fractionalReadback.UnderOverAppliedFixedAmount);
        Assert.Equal(1.800000m, fractionalReadback.UnderOverAppliedVariableAmount);
        Assert.Equal(2.699998m, fractionalReadback.UnderOverAppliedTotalAmount);
        Assert.Equal(0.900000m, fractionalReadback.UnallocatedFixedOverheadAmount);
        Assert.Equal(0m, fractionalReadback.OverAppliedFixedOverheadAmount);
        Assert.Equal(
            fractionalReadback.AppliedTotalAmount,
            fractionalReadback.AppliedFixedAmount
                + fractionalReadback.AppliedVariableAmount
                + fractionalReadback.AppliedRoundingDifferenceAmount);
        Assert.Equal(0.000000000028m, fractionalReadback.AppliedMachineHours);
        Assert.Equal(0.000000000028m, fractionalReadback.AbnormalDowntimeHours);

        db.WorkCenterMachineOverheadRates.Add(
            WorkCenterMachineOverheadRate.DefineApplicable(
                "org-pg", "env-pg", "WC-PG", "2026-06",
                32_000m, 10_000m, 1_000m, "CNY", 1,
                "system:test", "duplicate revision", DateTimeOffset.UtcNow));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        await using var indexCommand = new NpgsqlCommand("""
            SELECT indexdef
            FROM pg_indexes
            WHERE schemaname = 'erp'
              AND tablename = 'work_center_machine_overhead_rates'
              AND indexname = 'ux_wc_machine_overhead_rates_scope_period_revision'
            """, connection);
        var indexDefinition = Assert.IsType<string>(await indexCommand.ExecuteScalarAsync());
        Assert.Contains("UNIQUE", indexDefinition, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("accounting_period_code", indexDefinition, StringComparison.Ordinal);

        await using var reconciliationIndexesCommand = new NpgsqlCommand("""
            SELECT indexname, indexdef
            FROM pg_indexes
            WHERE schemaname = 'erp'
              AND tablename = 'work_center_machine_overhead_reconciliations'
              AND indexname IN (
                  'ux_wc_machine_overhead_reconciliations_scope_revision',
                  'ix_wc_machine_overhead_reconciliations_period')
            ORDER BY indexname
            """, connection);
        await using var reconciliationIndexes = await reconciliationIndexesCommand.ExecuteReaderAsync();
        var definitions = new Dictionary<string, string>(StringComparer.Ordinal);
        while (await reconciliationIndexes.ReadAsync())
            definitions.Add(reconciliationIndexes.GetString(0), reconciliationIndexes.GetString(1));
        Assert.Equal(2, definitions.Count);
        Assert.Contains("UNIQUE", definitions["ux_wc_machine_overhead_reconciliations_scope_revision"], StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "(organization_id, environment_id, work_center_id, accounting_period_code, revision DESC)",
            definitions["ux_wc_machine_overhead_reconciliations_scope_revision"], StringComparison.Ordinal);
        Assert.Contains(
            "(organization_id, environment_id, accounting_period_code, work_center_id)",
            definitions["ix_wc_machine_overhead_reconciliations_period"], StringComparison.Ordinal);
    }

    private static OperationMachineOverheadSettlementState AddMachineSettlement(
        ApplicationDbContext db,
        WorkCenterMachineOverheadRate rate,
        string operationTaskId,
        long revision,
        long machineHours,
        OperationMachineOverheadSettlementState? existingState = null,
        string? currencyCode = null)
    {
        db.OperationMachineOverheadSettlements.Add(OperationMachineOverheadSettlement.CreateApplied(
            rate.OrganizationId, rate.EnvironmentId, $"WO-{operationTaskId}", operationTaskId,
            rate.WorkCenterId, revision,
            rate.AccountingPeriodCode == "2026-07"
                ? new DateTimeOffset(2026, 7, 31, 12, 0, 0, TimeSpan.Zero)
                : new DateTimeOffset(2026, 6, 30, 12, 0, 0, TimeSpan.Zero),
            $"DEVICE-{operationTaskId}", machineHours * TimeSpan.TicksPerHour,
            "single-device-active-minus-explicit-pause-v1", rate.Id, rate.AccountingPeriodCode,
            rate.Revision, currencyCode ?? rate.CurrencyCode, rate.FixedHourlyRate, rate.VariableHourlyRate,
            $"evt-{operationTaskId}-{revision}", new string('a', 64)));
        var state = existingState ?? OperationMachineOverheadSettlementState.Open(
            rate.OrganizationId, rate.EnvironmentId, operationTaskId);
        state.ApplySettlement(revision);
        if (existingState is null) db.OperationMachineOverheadSettlementStates.Add(state);
        return state;
    }

    private static async Task AssertCloneRejectedAsync(
        NpgsqlConnection connection,
        Guid sourceId,
        string overridesJson,
        string expectedSqlState)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO erp.work_center_machine_overhead_reconciliations
            SELECT (jsonb_populate_record(
                NULL::erp.work_center_machine_overhead_reconciliations,
                to_jsonb(source_row) || jsonb_build_object('id', @id)
                    || CAST(@overrides AS jsonb)
            )).*
            FROM (
                SELECT *
                FROM erp.work_center_machine_overhead_reconciliations
                WHERE id = @source_id
            ) AS source_row
            """, connection);
        command.Parameters.AddWithValue("id", Guid.CreateVersion7());
        command.Parameters.AddWithValue("source_id", sourceId);
        command.Parameters.AddWithValue("overrides", overridesJson);
        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(expectedSqlState, exception.SqlState);
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

    private static ConfigureWorkCenterMachineOverheadRateCommand Command(decimal fixedBudget, string actor) =>
        new(
            "org-concurrent",
            "env-concurrent",
            "WC-CONCURRENT",
            "2026-06",
            MachineOverheadApplicability.Applicable,
            fixedBudget,
            10_000m,
            1_000m,
            "CNY",
            actor,
            "concurrent rate",
            new DateTimeOffset(2026, 5, 25, 8, 0, 0, TimeSpan.Zero));

    private static ReconcileWorkCenterMachineOverheadCommand ReconciliationCommand(
        decimal fixedAmount,
        string actor)
        => new(
            "org-concurrent", "env-concurrent", "WC-CONCURRENT", "2026-06",
            fixedAmount, 10_000m, "CNY", 0, AbnormalDowntimeDisposition.None,
            actor, $"ledger:{actor}", "concurrent reconciliation",
            new DateTimeOffset(2026, 6, 30, 16, 0, 0, TimeSpan.Zero));

    private static async Task WaitForAdvisoryLockWaitersAsync(
        string connectionString,
        string applicationName,
        int expectedCount,
        CancellationToken cancellationToken = default)
    {
        await Eventually.WaitAsync(
            condition: $"{expectedCount} PostgreSQL machine-rate advisory-lock waiters for {applicationName}",
            observe: async token =>
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync(token);
                await using var command = new NpgsqlCommand("""
                    SELECT count(*)
                    FROM pg_stat_activity
                    WHERE application_name = @application_name
                      AND wait_event_type = 'Lock'
                      AND query LIKE 'SELECT pg_advisory_xact_lock%'
                    """, connection);
                command.Parameters.AddWithValue("application_name", applicationName);
                return Convert.ToInt32(await command.ExecuteScalarAsync(token));
            },
            isSatisfied: waitingCount => waitingCount >= expectedCount,
            describe: waitingCount => $"waiters={waitingCount}; expected>={expectedCount}",
            options: new EventuallyOptions(
                Timeout: TimeSpan.FromSeconds(10),
                PollInterval: TimeSpan.FromMilliseconds(50),
                SensitiveValues: [connectionString]),
            cancellationToken);
    }
}
