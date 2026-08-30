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
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountingPeriodAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkCenterMachineOverheadRateAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkOrderCostAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Finance;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Erp.Web.Application.Queries.Finance;
using Nerv.IIP.Contracts.Inventory;
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
    public async Task PostgreSQL_rework_origin_arriving_after_cost_events_stays_isolated_and_queryable()
    {
        await ErpPostgresLaneDatabase.ResetSchemaAsync();
        var options = ErpPostgresLaneDatabase.CreateOptions();
        var reportedAtUtc = DateTimeOffset.Parse("2026-08-30T03:30:00Z");
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();

        await using (var setupDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            await setupDb.Database.MigrateAsync();
            setupDb.WorkCenterCostRates.Add(WorkCenterCostRate.Define(
                "org-pg", "env-pg", "WC-PG", 50m, "CNY",
                DateTimeOffset.Parse("2026-01-01T00:00:00Z"), null, 1,
                "system:test", "governed PostgreSQL rate", DateTimeOffset.Parse("2026-01-01T00:00:00Z")));
            var sourceCost = WorkOrderCost.Open("org-pg", "env-pg", "WO-SOURCE-PG", "FG-PG");
            sourceCost.RecordLabor("RPT-SOURCE-PG", "WC-PG", 1m, 25m, "CNY", false, reportedAtUtc.AddDays(-1));
            setupDb.WorkOrderCosts.Add(sourceCost);
            await setupDb.SaveChangesAsync();
        }

        var report = new ProductionReportRecordedIntegrationEvent(
            "evt-rework-report-pg", MesIntegrationEventTypes.ProductionReportRecorded,
            MesIntegrationEventVersions.V1, reportedAtUtc, MesIntegrationEventSources.BusinessMes,
            "RPT-RW-PG", "WO-RW-PG", "org-pg", "env-pg", "operator:test", "rework-report-pg",
            new ProductionReportRecordedPayload(
                "RPT-RW-PG", "WO-RW-PG", "OP-RW-PG", "WC-PG", null,
                2m, 0m, 0m, "ea", 1m, reportedAtUtc, false, MaterialMovementCount: 0));
        await using (var reportDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            await new ProductionReportRecordedIntegrationEventHandlerForAccumulateLaborCost(
                    reportDb, deadLetters, reportDb, new PostgreSqlWorkOrderCostMutationLock(reportDb))
                .HandleAsync(report, CancellationToken.None);
        }

        var created = new ReworkWorkOrderCreatedIntegrationEvent(
            "evt-rework-created-pg", MesIntegrationEventTypes.ReworkWorkOrderCreated,
            MesIntegrationEventVersions.V1, reportedAtUtc.AddMinutes(1),
            MesIntegrationEventSources.BusinessMes, "corr-rework-pg", "cause-ncr-pg",
            "org-pg", "env-pg", "system:business-mes", "rework-created-pg",
            new ReworkWorkOrderCreatedPayload(
                "ncr-pg", "NCR-PG", "WO-RW-PG", "WO-SOURCE-PG", "OP-SOURCE-PG",
                "FG-PG", 2m, "LOT-PG", null, reportedAtUtc.AddMinutes(1)));
        await using (var attributionDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            var handler = new ReworkWorkOrderCreatedIntegrationEventHandlerForAttributeCost(
                attributionDb, attributionDb, new PostgreSqlWorkOrderCostMutationLock(attributionDb), deadLetters);
            await handler.HandleAsync(created, CancellationToken.None);
            await handler.HandleAsync(created, CancellationToken.None);
        }

        var completed = new WorkOrderCompletedIntegrationEvent(
            "evt-rework-completed-pg", MesIntegrationEventTypes.WorkOrderCompleted,
            MesIntegrationEventVersions.V1, reportedAtUtc.AddMinutes(2),
            MesIntegrationEventSources.BusinessMes, "WO-RW-PG", "WO-RW-PG",
            "org-pg", "env-pg", "system:mes", "rework-completed-pg",
            new WorkOrderCompletedPayload(
                "WO-RW-PG", "FG-PG", 2m, 2m, 0m, reportedAtUtc.AddMinutes(2), 1, 0));
        await using (var completionDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            await new WorkOrderCompletedIntegrationEventHandlerForCapitalizeCost(
                    completionDb, deadLetters, completionDb)
                .HandleAsync(completed, CancellationToken.None);
        }

        await using var assertDb = new ApplicationDbContext(options, new NoopMediator());
        ErpPostgresLaneDatabase.AssertUsesGovernedDatabase(assertDb);
        var source = await assertDb.WorkOrderCosts.Include(x => x.Details)
            .SingleAsync(x => x.WorkOrderId == "WO-SOURCE-PG");
        var rework = await assertDb.WorkOrderCosts.Include(x => x.Details)
            .SingleAsync(x => x.WorkOrderId == "WO-RW-PG");
        Assert.Equal(25m, source.TotalAccumulatedCost);
        Assert.False(source.IsRework);
        Assert.Equal(100m, rework.LaborCost);
        Assert.True(rework.CapitalizationPublished);
        Assert.Equal("ncr-pg", rework.SourceNcrId);
        Assert.Equal("WO-SOURCE-PG", rework.SourceWorkOrderId);
        Assert.Equal("FG-PG", rework.SkuCode);
        Assert.Single(await assertDb.ProcessedIntegrationEvents
            .Where(x => x.ConsumerName == ReworkWorkOrderCreatedIntegrationEventHandlerForAttributeCost.ConsumerName)
            .ToArrayAsync());

        var byNcr = await new ListWorkOrderCostsQueryHandler(assertDb).Handle(
            new ListWorkOrderCostsQuery("org-pg", "env-pg", SourceNcrId: "ncr-pg"),
            CancellationToken.None);
        Assert.Equal("rework", Assert.Single(byNcr.Items).CostKind);
        Assert.Equal(100m, byNcr.ReworkCostTotal);
        Assert.Equal(0m, byNcr.OrdinaryCostTotal);
        Assert.Empty(await deadLetters.ListAsync(null, null, CancellationToken.None));
    }

    [ErpCostPostgresFact(Timeout = 30_000)]
    public async Task PostgreSQL_closed_period_stays_replayable_then_reopen_posts_machine_overhead_exactly_once()
    {
        await ErpPostgresLaneDatabase.ResetSchemaAsync();
        var options = ErpPostgresLaneDatabase.CreateOptions();
        var completedAtUtc = DateTimeOffset.Parse("2026-08-31T15:00:00Z");
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var settled = MachineSettled(
            "evt-machine-closed", "org-machine-closed", "env-machine-closed",
            "WO-MACHINE-CLOSED", "OP-MACHINE-CLOSED", "WC-MACHINE-CLOSED",
            completedAtUtc, TimeSpan.TicksPerHour);
        var voided = MachineVoided("evt-machine-closed-void", settled, completedAtUtc.AddHours(2));

        await using (var setupDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            await setupDb.Database.MigrateAsync();
            var period = AccountingPeriod.Open(
                "org-machine-closed", "env-machine-closed", "2026-08",
                new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31));
            period.Close("auditor:test", "month end close");
            setupDb.AccountingPeriods.Add(period);
            setupDb.WorkCenterMachineOverheadRates.Add(WorkCenterMachineOverheadRate.DefineApplicable(
                "org-machine-closed", "env-machine-closed", "WC-MACHINE-CLOSED", "2026-08",
                30_000m, 10_000m, 1_000m, "CNY", 1,
                "system:test", "approved machine overhead rate", completedAtUtc.AddDays(-30)));
            await setupDb.SaveChangesAsync();
        }

        await using (var closedDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            await MachineSettlementConsumer(closedDb, deadLetters).HandleAsync(settled, CancellationToken.None);
        }

        await using (var closedAssertDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            Assert.Empty(await closedAssertDb.ProcessedIntegrationEvents.ToListAsync());
            Assert.Empty(await closedAssertDb.OperationMachineOverheadSettlements.ToListAsync());
            Assert.Empty(await closedAssertDb.OperationMachineOverheadSettlementStates.ToListAsync());
            Assert.Empty(await closedAssertDb.WorkOrderCosts.ToListAsync());
            Assert.Empty(await closedAssertDb.Set<WorkOrderCostDetail>().ToListAsync());
        }
        Assert.Equal("closed-accounting-period", Assert.Single(await deadLetters.ListAsync(
            MesOperationActualTimeSettledV2IntegrationEventHandlerForAccumulateMachineOverhead.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None)).FailureCode);

        await using (var reopenDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            var period = await reopenDb.AccountingPeriods.SingleAsync();
            period.Reopen("auditor:test", "approved late machine settlement");
            await reopenDb.SaveChangesAsync();
        }

        await using (var replayDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            await MachineSettlementConsumer(replayDb, deadLetters).HandleAsync(settled, CancellationToken.None);
        }
        await using (var duplicateDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            await MachineSettlementConsumer(duplicateDb, deadLetters).HandleAsync(settled, CancellationToken.None);
        }

        await using (var assertDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            Assert.Single(await assertDb.ProcessedIntegrationEvents.Where(x => x.EventId == settled.EventId).ToListAsync());
            var snapshot = await assertDb.OperationMachineOverheadSettlements.SingleAsync();
            Assert.Equal("2026-08", snapshot.AccountingPeriodCode);
            Assert.Equal(1, snapshot.RateRevision);
            Assert.Equal(40m, snapshot.Amount);
            Assert.Equal(1, (await assertDb.OperationMachineOverheadSettlementStates.SingleAsync()).ActiveRevision);
            var cost = await assertDb.WorkOrderCosts.Include(x => x.Details).SingleAsync();
            Assert.Equal(40m, cost.MachineOverheadCost);
            Assert.Single(cost.Details, x => x.MachineOverheadBasis == MachineOverheadCostBasis.ActualOperation);
        }

        await using (var closeAfterSettlementDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            var cost = await closeAfterSettlementDb.WorkOrderCosts.Include(x => x.Details).SingleAsync();
            cost.RecordUncostedReport("RPT-MACHINE-CLOSED", false, completedAtUtc.AddMinutes(10));
            cost.Complete(10m, 1, 0, completedAtUtc.AddMinutes(20));
            cost.Capitalize("MOVE-MACHINE-CLOSED", 10m, 4m, completedAtUtc.AddMinutes(30));
            cost.RecordWipClearance(40m);
            (await closeAfterSettlementDb.AccountingPeriods.SingleAsync())
                .Close("auditor:test", "close after machine settlement");
            await closeAfterSettlementDb.SaveChangesAsync();
        }

        await using (var closedVoidDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            await MachineVoidConsumer(closedVoidDb, deadLetters).HandleAsync(voided, CancellationToken.None);
        }

        await using (var closedVoidAssertDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            Assert.Empty(await closedVoidAssertDb.ProcessedIntegrationEvents.Where(x => x.EventId == voided.EventId).ToListAsync());
            Assert.Empty(await closedVoidAssertDb.OperationMachineOverheadSettlementVoids.ToListAsync());
            Assert.Equal(1, (await closedVoidAssertDb.OperationMachineOverheadSettlementStates.SingleAsync()).ActiveRevision);
            var cost = await closedVoidAssertDb.WorkOrderCosts.Include(x => x.Details).SingleAsync();
            Assert.Equal(40m, cost.MachineOverheadCost);
            Assert.Equal(40m, cost.WipClearedCost);
            Assert.Empty(await closedVoidAssertDb.JournalVouchers.ToListAsync());
        }
        Assert.Equal("closed-accounting-period", Assert.Single(await deadLetters.ListAsync(
            MesOperationActualTimeSettlementVoidedV2IntegrationEventHandlerForReverseMachineOverhead.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None)).FailureCode);

        await using (var reopenForVoidDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            (await reopenForVoidDb.AccountingPeriods.SingleAsync())
                .Reopen("auditor:test", "approved late machine void");
            await reopenForVoidDb.SaveChangesAsync();
        }
        await using (var replayVoidDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            await MachineVoidConsumer(replayVoidDb, deadLetters).HandleAsync(voided, CancellationToken.None);
        }
        await using (var duplicateVoidDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            await MachineVoidConsumer(duplicateVoidDb, deadLetters).HandleAsync(voided, CancellationToken.None);
        }

        await using (var finalAssertDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            Assert.Single(await finalAssertDb.ProcessedIntegrationEvents.Where(x => x.EventId == voided.EventId).ToListAsync());
            Assert.Equal(-40m, (await finalAssertDb.OperationMachineOverheadSettlementVoids.SingleAsync()).Amount);
            Assert.Null((await finalAssertDb.OperationMachineOverheadSettlementStates.SingleAsync()).ActiveRevision);
            var finalCost = await finalAssertDb.WorkOrderCosts.Include(x => x.Details).SingleAsync();
            Assert.Equal(0m, finalCost.MachineOverheadCost);
            Assert.Equal(0m, finalCost.WipClearedCost);
            Assert.Single(finalCost.Details, x => x.MachineOverheadBasis == MachineOverheadCostBasis.ActualOperationVoid);
            var voucher = await finalAssertDb.JournalVouchers.Include(x => x.Lines).SingleAsync();
            Assert.Equal(40m, voucher.Lines.Sum(x => x.DebitAmount));
            Assert.Equal(voucher.Lines.Sum(x => x.DebitAmount), voucher.Lines.Sum(x => x.CreditAmount));
        }

        await using (var closeAfterVoidDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            (await closeAfterVoidDb.AccountingPeriods.SingleAsync())
                .Close("auditor:test", "close after machine void");
            await closeAfterVoidDb.SaveChangesAsync();
        }
        await using (var duplicateAfterCloseDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            await MachineVoidConsumer(duplicateAfterCloseDb, deadLetters).HandleAsync(voided, CancellationToken.None);
        }

        var conflictingVoid = voided with
        {
            EventId = "evt-machine-closed-void-conflict",
            Payload = voided.Payload with { VoidedAtUtc = voided.Payload.VoidedAtUtc.AddMinutes(1) },
        };
        await using (var conflictAfterCloseDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            await MachineVoidConsumer(conflictAfterCloseDb, deadLetters).HandleAsync(conflictingVoid, CancellationToken.None);
        }

        await using var idempotencyAssertDb = new ApplicationDbContext(options, new NoopMediator());
        Assert.Single(await idempotencyAssertDb.ProcessedIntegrationEvents.Where(x => x.EventId == voided.EventId).ToListAsync());
        Assert.Empty(await idempotencyAssertDb.ProcessedIntegrationEvents.Where(x => x.EventId == conflictingVoid.EventId).ToListAsync());
        Assert.Single(await idempotencyAssertDb.OperationMachineOverheadSettlementVoids.ToListAsync());
        Assert.Null((await idempotencyAssertDb.OperationMachineOverheadSettlementStates.SingleAsync()).ActiveRevision);
        var idempotentCost = await idempotencyAssertDb.WorkOrderCosts.Include(x => x.Details).SingleAsync();
        Assert.Equal(0m, idempotentCost.MachineOverheadCost);
        Assert.Equal(0m, idempotentCost.WipClearedCost);
        Assert.Single(idempotentCost.Details, x => x.MachineOverheadBasis == MachineOverheadCostBasis.ActualOperationVoid);
        Assert.Single(await idempotencyAssertDb.JournalVouchers.ToListAsync());
        Assert.Single(await deadLetters.ListAsync(
            MesOperationActualTimeSettlementVoidedV2IntegrationEventHandlerForReverseMachineOverhead.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None), x => x.FailureCode == "closed-accounting-period");
        Assert.Single(await deadLetters.ListAsync(
            MesOperationActualTimeSettlementVoidedV2IntegrationEventHandlerForReverseMachineOverhead.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None), x => x.FailureCode == "conflicting-operation-machine-overhead-settlement");
    }

    [ErpCostPostgresFact(Timeout = 30_000)]
    public async Task PostgreSQL_priced_labor_then_zero_not_applicable_machine_settle_and_void_do_not_freeze_machine_currency()
    {
        await ErpPostgresLaneDatabase.ResetSchemaAsync();
        var options = ErpPostgresLaneDatabase.CreateOptions();
        var completedAtUtc = DateTimeOffset.Parse("2026-08-31T15:00:00Z");
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var settled = MachineSettled(
            "evt-machine-na-zero", "org-machine-currency", "env-machine-currency",
            "WO-LABOR-FIRST", "OP-LABOR-FIRST", "WC-NOT-APPLICABLE",
            completedAtUtc, null, MesMachineTimeFactStatus.NotApplicable);

        await using (var setupDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            await setupDb.Database.MigrateAsync();
            setupDb.AccountingPeriods.Add(AccountingPeriod.Open(
                "org-machine-currency", "env-machine-currency", "2026-08",
                new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)));
            setupDb.WorkCenterMachineOverheadRates.Add(WorkCenterMachineOverheadRate.DefineNotApplicable(
                "org-machine-currency", "env-machine-currency", "WC-NOT-APPLICABLE", "2026-08",
                "CNY", 1, "system:test", "no machine overhead", completedAtUtc.AddDays(-30)));
            var cost = WorkOrderCost.Open(
                "org-machine-currency", "env-machine-currency", "WO-LABOR-FIRST", "SKU-001");
            cost.RecordLabor("RPT-USD-FIRST", "WC-LABOR", 1m, 80m, "USD", false, completedAtUtc.AddMinutes(-10));
            setupDb.WorkOrderCosts.Add(cost);
            await setupDb.SaveChangesAsync();
        }

        await using (var settlementDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            await MachineSettlementConsumer(settlementDb, deadLetters).HandleAsync(settled, CancellationToken.None);
        }
        await using (var voidDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            await MachineVoidConsumer(voidDb, deadLetters).HandleAsync(
                MachineVoided("evt-machine-na-zero-void", settled, completedAtUtc.AddHours(1)),
                CancellationToken.None);
        }

        await using var assertDb = new ApplicationDbContext(options, new NoopMediator());
        var snapshot = await assertDb.OperationMachineOverheadSettlements.SingleAsync();
        var reversal = await assertDb.OperationMachineOverheadSettlementVoids.SingleAsync();
        var persisted = await assertDb.WorkOrderCosts.Include(x => x.Details).SingleAsync();
        Assert.Equal(0m, snapshot.Amount);
        Assert.Equal(0m, reversal.Amount);
        Assert.Equal("USD", persisted.LaborCurrencyCode);
        Assert.Null(persisted.MachineOverheadCurrencyCode);
        Assert.Equal(80m, persisted.TotalAccumulatedCost);
        Assert.Empty(await deadLetters.ListAsync(
            MesOperationActualTimeSettledV2IntegrationEventHandlerForAccumulateMachineOverhead.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
        Assert.Empty(await deadLetters.ListAsync(
            MesOperationActualTimeSettlementVoidedV2IntegrationEventHandlerForReverseMachineOverhead.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
    }

    [ErpCostPostgresFact(Timeout = 30_000)]
    public async Task PostgreSQL_zero_available_machine_settle_and_void_do_not_poison_later_priced_labor_currency()
    {
        await ErpPostgresLaneDatabase.ResetSchemaAsync();
        var options = ErpPostgresLaneDatabase.CreateOptions();
        var completedAtUtc = DateTimeOffset.Parse("2026-08-31T15:00:00Z");
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var settled = MachineSettled(
            "evt-machine-zero-first", "org-machine-zero-first", "env-machine-zero-first",
            "WO-MACHINE-FIRST", "OP-MACHINE-FIRST", "WC-MACHINE-FIRST",
            completedAtUtc, 0);

        await using (var setupDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            await setupDb.Database.MigrateAsync();
            setupDb.AccountingPeriods.Add(AccountingPeriod.Open(
                "org-machine-zero-first", "env-machine-zero-first", "2026-08",
                new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)));
            setupDb.WorkCenterMachineOverheadRates.Add(WorkCenterMachineOverheadRate.DefineApplicable(
                "org-machine-zero-first", "env-machine-zero-first", "WC-MACHINE-FIRST", "2026-08",
                30_000m, 10_000m, 1_000m, "CNY", 1,
                "system:test", "approved machine overhead rate", completedAtUtc.AddDays(-30)));
            await setupDb.SaveChangesAsync();
        }

        await using (var settlementDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            await MachineSettlementConsumer(settlementDb, deadLetters).HandleAsync(settled, CancellationToken.None);
        }
        await using (var voidDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            await MachineVoidConsumer(voidDb, deadLetters).HandleAsync(
                MachineVoided("evt-machine-zero-first-void", settled, completedAtUtc.AddHours(1)),
                CancellationToken.None);
        }
        await using (var laborDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            var cost = await laborDb.WorkOrderCosts.Include(x => x.Details).SingleAsync();
            cost.RecordLabor("RPT-USD-LATER", "WC-LABOR", 1m, 80m, "USD", false, completedAtUtc.AddHours(2));
            await laborDb.SaveChangesAsync();
        }

        await using var assertDb = new ApplicationDbContext(options, new NoopMediator());
        var snapshot = await assertDb.OperationMachineOverheadSettlements.SingleAsync();
        var reversal = await assertDb.OperationMachineOverheadSettlementVoids.SingleAsync();
        var persisted = await assertDb.WorkOrderCosts.Include(x => x.Details).SingleAsync();
        Assert.Equal(0m, snapshot.Amount);
        Assert.Equal(0m, reversal.Amount);
        Assert.Equal("USD", persisted.LaborCurrencyCode);
        Assert.Null(persisted.MachineOverheadCurrencyCode);
        Assert.Equal(80m, persisted.TotalAccumulatedCost);
    }

    [ErpCostPostgresFact(Timeout = 30_000)]
    public async Task PostgreSQL_nonzero_machine_overhead_still_fails_closed_for_priced_labor_in_another_currency()
    {
        await ErpPostgresLaneDatabase.ResetSchemaAsync();
        var options = ErpPostgresLaneDatabase.CreateOptions();
        var completedAtUtc = DateTimeOffset.Parse("2026-08-31T15:00:00Z");
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();

        await using (var setupDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            await setupDb.Database.MigrateAsync();
            setupDb.AccountingPeriods.Add(AccountingPeriod.Open(
                "org-machine-priced", "env-machine-priced", "2026-08",
                new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)));
            setupDb.WorkCenterMachineOverheadRates.Add(WorkCenterMachineOverheadRate.DefineApplicable(
                "org-machine-priced", "env-machine-priced", "WC-MACHINE-PRICED", "2026-08",
                30_000m, 10_000m, 1_000m, "CNY", 1,
                "system:test", "approved machine overhead rate", completedAtUtc.AddDays(-30)));
            var cost = WorkOrderCost.Open(
                "org-machine-priced", "env-machine-priced", "WO-MACHINE-PRICED", "SKU-001");
            cost.RecordLabor("RPT-USD-PRICED", "WC-LABOR", 1m, 80m, "USD", false, completedAtUtc.AddMinutes(-10));
            setupDb.WorkOrderCosts.Add(cost);
            await setupDb.SaveChangesAsync();
        }

        var settled = MachineSettled(
            "evt-machine-priced", "org-machine-priced", "env-machine-priced",
            "WO-MACHINE-PRICED", "OP-MACHINE-PRICED", "WC-MACHINE-PRICED",
            completedAtUtc, TimeSpan.TicksPerHour);
        await using (var settlementDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            await MachineSettlementConsumer(settlementDb, deadLetters).HandleAsync(settled, CancellationToken.None);
        }

        await using var assertDb = new ApplicationDbContext(options, new NoopMediator());
        Assert.Empty(await assertDb.OperationMachineOverheadSettlements.ToListAsync());
        Assert.Empty(await assertDb.ProcessedIntegrationEvents.Where(x => x.EventId == settled.EventId).ToListAsync());
        Assert.Equal(80m, (await assertDb.WorkOrderCosts.Include(x => x.Details).SingleAsync()).TotalAccumulatedCost);
        Assert.Equal("incompatible-work-order-machine-overhead-currency", Assert.Single(await deadLetters.ListAsync(
            MesOperationActualTimeSettledV2IntegrationEventHandlerForAccumulateMachineOverhead.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None)).FailureCode);
    }

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
            setupDb.AccountingPeriods.Add(AccountingPeriod.Open(
                "org-concurrent", "env-concurrent", "2026-08",
                new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)));
            setupDb.WorkCenterMachineOverheadRates.Add(WorkCenterMachineOverheadRate.DefineApplicable(
                "org-concurrent", "env-concurrent", "WC-CONCURRENT", "2026-08",
                30_000m, 10_000m, 1_000m, "CNY", 1,
                "system:test", "approved machine overhead rate", DateTimeOffset.UtcNow));
            await setupDb.SaveChangesAsync();
        }

        await using var gateDb = new ApplicationDbContext(options, new NoopMediator());
        await using var gateTransaction = await gateDb.Database.BeginTransactionAsync();
        await new PostgreSqlWorkOrderCostMutationLock(gateDb)
            .AcquireAsync("org-concurrent", "env-concurrent", "WO-CONCURRENT", CancellationToken.None);

        await using var reportDb = new ApplicationDbContext(options, new NoopMediator());
        await using var settlementDb = new ApplicationDbContext(options, new NoopMediator());
        await using var machineDb = new ApplicationDbContext(options, new NoopMediator());
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var reportedAtUtc = new DateTimeOffset(2026, 8, 31, 15, 40, 0, TimeSpan.Zero);
        var completedAtUtc = reportedAtUtc.AddMinutes(10);
        var report = new ProductionReportRecordedIntegrationEvent(
            "evt-report-concurrent", MesIntegrationEventTypes.ProductionReportRecorded, 1, reportedAtUtc,
            MesIntegrationEventSources.BusinessMes, "RPT-CONCURRENT", "WO-CONCURRENT",
            "org-concurrent", "env-concurrent", "operator:test", "report:RPT-CONCURRENT",
            new ProductionReportRecordedPayload(
                "RPT-CONCURRENT", "WO-CONCURRENT", "OP-CONCURRENT", "WC-CONCURRENT", null,
                10m, 0m, 0m, "ea", 5.0000004m, reportedAtUtc, false, MaterialMovementCount: 0));
        var settled = new MesOperationActualTimeSettledIntegrationEvent(
            "evt-settled-concurrent", MesIntegrationEventTypes.OperationActualTimeSettled, 1,
            completedAtUtc.AddMinutes(1), MesIntegrationEventSources.BusinessMes,
            "correlation-concurrent", "causation-concurrent", "org-concurrent", "env-concurrent",
            "operator:test", "actual-time:OP-CONCURRENT:1:settled",
            new OperationActualTimeSettledPayload(
                "WO-CONCURRENT", "OP-CONCURRENT", "WC-CONCURRENT", 1, completedAtUtc,
                2 * TimeSpan.TicksPerHour, 2 * TimeSpan.TicksPerHour, ["RPT-CONCURRENT"]));
        var machineSettled = new MesOperationActualTimeSettledV2IntegrationEvent(
            "evt-machine-concurrent", MesIntegrationEventTypes.OperationActualTimeSettled,
            MesIntegrationEventVersions.V2, completedAtUtc.AddMinutes(1), MesIntegrationEventSources.BusinessMes,
            "correlation-concurrent", "causation-concurrent", "org-concurrent", "env-concurrent",
            "operator:test", "actual-time:OP-CONCURRENT:1:settled:v2",
            new OperationActualTimeSettledV2Payload(
                "WO-CONCURRENT", "OP-CONCURRENT", "WC-CONCURRENT", 1, completedAtUtc,
                2 * TimeSpan.TicksPerHour, 2 * TimeSpan.TicksPerHour, ["RPT-CONCURRENT"],
                "DEVICE-CONCURRENT", MesMachineTimeFactStatus.Available,
                2 * TimeSpan.TicksPerHour,
                MesMachineTimeBasisCodes.SingleDeviceActiveMinusExplicitPauseV1));

        var reportTask = new ProductionReportRecordedIntegrationEventHandlerForAccumulateLaborCost(
                reportDb, deadLetters, reportDb, new PostgreSqlWorkOrderCostMutationLock(reportDb))
            .HandleAsync(report, CancellationToken.None);
        var settlementTask = new MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost(
                settlementDb, settlementDb, new PostgreSqlWorkOrderCostMutationLock(settlementDb),
                new OperationLaborSettlementOrchestrator(settlementDb, deadLetters))
            .HandleAsync(settled, CancellationToken.None);
        var machineTask = new MesOperationActualTimeSettledV2IntegrationEventHandlerForAccumulateMachineOverhead(
                machineDb, machineDb, new PostgreSqlWorkOrderCostMutationLock(machineDb),
                new OperationMachineOverheadSettlementOrchestrator(machineDb, deadLetters, new PostgreSqlErpAdvisoryLockAllocator(machineDb)))
            .HandleAsync(machineSettled, CancellationToken.None);
        await WaitForAdvisoryLockWaitersAsync(connectionString, applicationName, expectedCount: 3);
        Assert.False(reportTask.IsCompleted);
        Assert.False(settlementTask.IsCompleted);
        Assert.False(machineTask.IsCompleted);

        await gateTransaction.CommitAsync();
        await Task.WhenAll(reportTask, settlementTask, machineTask).WaitAsync(TimeSpan.FromSeconds(10));

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
        var reportSnapshot = Assert.Single(await assertDb.OperationLaborReportSnapshots.AsNoTracking().ToListAsync());
        Assert.Equal(10m, reportSnapshot.GoodQuantity);
        Assert.Equal(5m, reportSnapshot.TheoreticalRatePerHour);
        Assert.False(reportSnapshot.HasValidNumericScale);
        Assert.Equal(80m, cost.MachineOverheadCost);
        Assert.Single(cost.Details, x => x.MachineOverheadBasis == MachineOverheadCostBasis.ActualOperation);
        Assert.Single(await assertDb.OperationMachineOverheadSettlements.ToListAsync());
        Assert.Equal(1, (await assertDb.OperationMachineOverheadSettlementStates.SingleAsync()).ActiveRevision);

        var stageRead = await new GetWorkOrderCostVarianceQueryHandler(assertDb).Handle(
            new GetWorkOrderCostVarianceQuery("org-concurrent", "env-concurrent", "WO-CONCURRENT"),
            CancellationToken.None);
        Assert.Equal("unavailable", stageRead.LaborVarianceStatus);
        Assert.Equal("work_order_not_completed", stageRead.UnavailableReason);
        Assert.Null(stageRead.StandardLaborHours);
        Assert.Null(stageRead.LaborEfficiencyVarianceAmount);
        Assert.Null(stageRead.CapitalizationVarianceAmount);
        Assert.Equal("unavailable", Assert.Single(stageRead.Operations).Status);

        cost.Complete(10m, 1, 0, completedAtUtc.AddMinutes(2));
        await assertDb.SaveChangesAsync();
        assertDb.ChangeTracker.Clear();

        var read = await new GetWorkOrderCostVarianceQueryHandler(assertDb).Handle(
            new GetWorkOrderCostVarianceQuery("org-concurrent", "env-concurrent", "WO-CONCURRENT"),
            CancellationToken.None);
        Assert.Equal("unavailable", read.LaborVarianceStatus);
        Assert.Equal("numeric_scale_out_of_range", read.UnavailableReason);
        Assert.Null(read.StandardLaborHours);
        Assert.Equal(2.000000m, read.ActualLaborHours);
        Assert.Null(read.LaborEfficiencyVarianceAmount);
        Assert.Equal(2.000000m, read.ActualMachineHours);
        Assert.Equal("unavailable", read.MachineCostStatus);

        var governedRateId = await assertDb.WorkCenterCostRates.Select(x => x.Id).SingleAsync();
        var oldRevision = OperationLaborSettlement.Create(
            "org-concurrent", "env-concurrent", "WO-CONCURRENT", "OP-REVISION", "WC-REVISION", 1,
            completedAtUtc, TimeSpan.TicksPerHour, governedRateId,
            1, "CNY", 1m, "evt-revision-old", "hash-revision-old");
        var activeRevision = OperationLaborSettlement.Create(
            "org-concurrent", "env-concurrent", "WO-CONCURRENT", "OP-REVISION", "WC-REVISION", 2,
            completedAtUtc, 2 * TimeSpan.TicksPerHour, governedRateId,
            2, "CNY", 1m, "evt-revision-active", "hash-revision-active");
        var revisionState = OperationLaborSettlementState.Open(
            "org-concurrent", "env-concurrent", "OP-REVISION");
        revisionState.ApplySettlement(1);
        revisionState.ApplySettlement(2);
        var roundingSettlement = OperationLaborSettlement.Create(
            "org-concurrent", "env-concurrent", "WO-CONCURRENT", "OP-ROUND", "WC-ROUND", 1,
            completedAtUtc, TimeSpan.TicksPerHour, governedRateId,
            1, "CNY", 1m, "evt-round", "hash-round");
        var roundingState = OperationLaborSettlementState.Open(
            "org-concurrent", "env-concurrent", "OP-ROUND");
        roundingState.ApplySettlement(1);
        assertDb.AddRange(
            oldRevision,
            activeRevision,
            revisionState,
            OperationLaborCoveredReport.Create(
                "org-concurrent", "env-concurrent", "WO-CONCURRENT", "OP-REVISION", 1, "RPT-REVISION-OLD"),
            OperationLaborCoveredReport.Create(
                "org-concurrent", "env-concurrent", "WO-CONCURRENT", "OP-REVISION", 2, "RPT-REVISION-ACTIVE"),
            OperationLaborReportSnapshot.Create(
                "org-concurrent", "env-concurrent", "WO-CONCURRENT", "OP-REVISION", "WC-REVISION", "RPT-REVISION-OLD",
                100m, 0m, 0m, "ea", 2m, reportedAtUtc.AddMinutes(1), false, null, "evt-report-revision-old"),
            OperationLaborReportSnapshot.Create(
                "org-concurrent", "env-concurrent", "WO-CONCURRENT", "OP-REVISION", "WC-REVISION", "RPT-REVISION-ACTIVE",
                4m, 0m, 0m, "ea", 2m, reportedAtUtc.AddMinutes(2), false, null, "evt-report-revision-active"),
            roundingSettlement,
            roundingState,
            OperationLaborCoveredReport.Create(
                "org-concurrent", "env-concurrent", "WO-CONCURRENT", "OP-ROUND", 1, "RPT-ROUND"),
            OperationLaborReportSnapshot.Create(
                "org-concurrent", "env-concurrent", "WO-CONCURRENT", "OP-ROUND", "WC-ROUND", "RPT-ROUND",
                2.000001m, 0m, 0m, "ea", 2m, reportedAtUtc.AddMinutes(3), false, null, "evt-report-round"));
        await assertDb.SaveChangesAsync();
        assertDb.ChangeTracker.Clear();

        var vectorRead = await new GetWorkOrderCostVarianceQueryHandler(assertDb).Handle(
            new GetWorkOrderCostVarianceQuery("org-concurrent", "env-concurrent", "WO-CONCURRENT"),
            CancellationToken.None);
        Assert.Equal(3, vectorRead.TotalOperations);
        var operations = vectorRead.Operations.ToDictionary(x => x.OperationTaskId, StringComparer.Ordinal);
        Assert.Equal(2, operations["OP-REVISION"].SettlementRevision);
        Assert.Equal(new[] { "RPT-REVISION-ACTIVE" },
            operations["OP-REVISION"].CoveredReports.Select(x => x.ReportNo));
        Assert.Equal(1.000001m, operations["OP-ROUND"].StandardLaborHours);
        Assert.Equal(-0.000001m, operations["OP-ROUND"].LaborEfficiencyVarianceHours);
        Assert.Equal(1.000001m, operations["OP-ROUND"].StandardLaborCost);
        Assert.Equal(-0.000001m, operations["OP-ROUND"].LaborEfficiencyVarianceAmount);

        var secondPage = await new GetWorkOrderCostVarianceQueryHandler(assertDb).Handle(
            new GetWorkOrderCostVarianceQuery("org-concurrent", "env-concurrent", "WO-CONCURRENT", 2, 2),
            CancellationToken.None);
        Assert.Equal(3, secondPage.TotalOperations);
        Assert.Equal(2, secondPage.PageNumber);
        Assert.Equal(2, secondPage.PageSize);
        Assert.Equal("OP-ROUND", Assert.Single(secondPage.Operations).OperationTaskId);

        var snapshotIndexes = await assertDb.Database.SqlQueryRaw<string>("""
            SELECT indexname AS "Value"
            FROM pg_indexes
            WHERE schemaname = 'erp'
              AND tablename = 'operation_labor_report_snapshots'
              AND indexname IN (
                'ux_operation_labor_report_snapshots_scope_report',
                'ix_operation_labor_report_snapshots_work_order_operation')
            ORDER BY indexname
            """).ToListAsync();
        Assert.Equal([
            "ix_operation_labor_report_snapshots_work_order_operation",
            "ux_operation_labor_report_snapshots_scope_report",
        ], snapshotIndexes);

        assertDb.OperationLaborReportSnapshots.Remove(await assertDb.OperationLaborReportSnapshots
            .SingleAsync(x => x.ReportNo == "RPT-CONCURRENT"));
        await assertDb.SaveChangesAsync();
        assertDb.ChangeTracker.Clear();
        var historicalRead = await new GetWorkOrderCostVarianceQueryHandler(assertDb).Handle(
            new GetWorkOrderCostVarianceQuery("org-concurrent", "env-concurrent", "WO-CONCURRENT"),
            CancellationToken.None);
        Assert.Equal("unavailable", historicalRead.LaborVarianceStatus);
        Assert.Equal("missing_report_snapshot", historicalRead.UnavailableReason);
        Assert.Equal(5.000000m, historicalRead.ActualLaborHours);
        Assert.Null(historicalRead.StandardLaborHours);
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

        await using (var metadataCommand = new NpgsqlCommand("""
            SELECT
                obj_description('erp.work_center_cost_rates'::regclass),
                col_description('erp.work_center_cost_rates'::regclass, (
                    SELECT attnum FROM pg_attribute
                    WHERE attrelid = 'erp.work_center_cost_rates'::regclass AND attname = 'hourly_rate')),
                col_description('erp.work_order_costs'::regclass, (
                    SELECT attnum FROM pg_attribute
                    WHERE attrelid = 'erp.work_order_costs'::regclass AND attname = 'labor_currency_code'))
            """, (NpgsqlConnection)db.Database.GetDbConnection()))
        await using (var metadata = await metadataCommand.ExecuteReaderAsync())
        {
            Assert.True(await metadata.ReadAsync());
            Assert.Equal("ERP append-only, effective-dated standard labor hourly-rate revision history by work center.", metadata.GetString(0));
            Assert.Equal("Positive standard labor hourly rate.", metadata.GetString(1));
            Assert.Equal("Frozen three-letter currency code shared by all priced labor on this work order; no implicit conversion is allowed.", metadata.GetString(2));
        }

        await using (var costDetailMetadataCommand = new NpgsqlCommand("""
            SELECT
                obj_description('erp.work_order_cost_details'::regclass),
                col_description('erp.work_order_cost_details'::regclass, (
                    SELECT attnum FROM pg_attribute
                    WHERE attrelid = 'erp.work_order_cost_details'::regclass AND attname = 'cost_type')),
                col_description('erp.work_order_cost_details'::regclass, (
                    SELECT attnum FROM pg_attribute
                    WHERE attrelid = 'erp.work_order_cost_details'::regclass AND attname = 'quantity')),
                col_description('erp.work_order_cost_details'::regclass, (
                    SELECT attnum FROM pg_attribute
                    WHERE attrelid = 'erp.work_order_cost_details'::regclass AND attname = 'rate'))
            """, (NpgsqlConnection)db.Database.GetDbConnection()))
        await using (var metadata = await costDetailMetadataCommand.ExecuteReaderAsync())
        {
            Assert.True(await metadata.ReadAsync());
            Assert.Equal("ERP auditable labor, material, or machine-overhead cost detail.", metadata.GetString(0));
            Assert.Equal("Labor, material, or machine-overhead cost type.", metadata.GetString(1));
            Assert.Equal("Labor or machine hours, or material quantity.", metadata.GetString(2));
            Assert.Equal("Labor or machine-overhead hourly rate, or moving-average material unit cost.", metadata.GetString(3));
        }

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
        cost.RecordLabor("RPT-PG-001", "WC-PG", 2m, 50m, "CNY", false, DateTimeOffset.UtcNow);
        cost.RecordMaterial("MOVE-PG-RM", "RPT-PG-001", "RM-PG", 3m, 20m, DateTimeOffset.UtcNow);
        cost.Complete(8m, 1, 1, DateTimeOffset.UtcNow);
        cost.Capitalize("MOVE-PG-FG", 8m, 20m, DateTimeOffset.UtcNow);
        cost.RecordWipClearance(160m);
        db.WorkOrderCosts.Add(cost);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        var persisted = await db.WorkOrderCosts.Include(x => x.Details).SingleAsync();
        Assert.Equal(160m, persisted.TotalAccumulatedCost);
        Assert.Equal("CNY", persisted.LaborCurrencyCode);
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
        cost.RecordLabor("RPT-CAP", "WC-CAP", 2m, 80m, "CNY", false, DateTimeOffset.Parse("2026-08-31T15:40:00Z"));
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

        var mixedCost = WorkOrderCost.Open("org-cap", "env-cap", "WO-MIXED", "FG-MIXED");
        mixedCost.RecordLabor("RPT-MIXED", "WC-CAP", 1m, 80m, "USD", false,
            DateTimeOffset.Parse("2026-08-31T15:40:00Z"));
        db.WorkOrderCosts.Add(mixedCost);
        await db.SaveChangesAsync();
        var mixedSettlement = settled with
        {
            EventId = "evt-cap-mixed",
            IdempotencyKey = "actual-time:OP-MIXED:1:settled",
            Payload = settled.Payload with
            {
                WorkOrderId = "WO-MIXED",
                OperationTaskId = "OP-MIXED",
                CoveredProductionReportNos = ["RPT-MIXED"],
            },
        };
        await new MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost(
                db, db, new PostgreSqlWorkOrderCostMutationLock(db),
                new OperationLaborSettlementOrchestrator(db, deadLetters))
            .HandleAsync(mixedSettlement, CancellationToken.None);

        var readyButUnposted = WorkOrderCost.Open("org-cap", "env-cap", "WO-READY", "FG-READY");
        readyButUnposted.RecordLabor("RPT-READY", "WC-CAP", 2m, 80m, "CNY", false,
            DateTimeOffset.Parse("2026-08-31T15:40:00Z"));
        readyButUnposted.Complete(10m, 1, 0, DateTimeOffset.Parse("2026-08-31T15:50:00Z"));
        db.WorkOrderCosts.Add(readyButUnposted);
        await db.SaveChangesAsync();
        var prePostingSettlement = settled with
        {
            EventId = "evt-ready-settled",
            IdempotencyKey = "actual-time:OP-READY:1:settled",
            Payload = settled.Payload with
            {
                WorkOrderId = "WO-READY",
                OperationTaskId = "OP-READY",
                CoveredProductionReportNos = ["RPT-READY"],
            },
        };
        await new MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost(
                db, db, new PostgreSqlWorkOrderCostMutationLock(db),
                new OperationLaborSettlementOrchestrator(db, deadLetters))
            .HandleAsync(prePostingSettlement, CancellationToken.None);
        await new MesOperationActualTimeSettlementVoidedIntegrationEventHandlerForReverseLaborCost(
                db, db, new PostgreSqlWorkOrderCostMutationLock(db),
                new OperationLaborSettlementOrchestrator(db, deadLetters))
            .HandleAsync(new MesOperationActualTimeSettlementVoidedIntegrationEvent(
                "evt-ready-void", MesIntegrationEventTypes.OperationActualTimeSettlementVoided, 1,
                DateTimeOffset.Parse("2026-08-31T16:05:00Z"), MesIntegrationEventSources.BusinessMes,
                "correlation-ready", "causation-ready", "org-cap", "env-cap", "operator:test",
                "actual-time:OP-READY:1:voided",
                new OperationActualTimeSettlementVoidedPayload(
                    "WO-READY", "OP-READY", "WC-CAP", 1,
                    DateTimeOffset.Parse("2026-08-31T15:50:00Z"),
                    DateTimeOffset.Parse("2026-08-31T16:05:00Z"),
                    90 * TimeSpan.TicksPerMinute, 90 * TimeSpan.TicksPerMinute,
                    ["RPT-READY"])),
                CancellationToken.None);

        await using var assertDb = new ApplicationDbContext(options, new NoopMediator());
        var persistedCost = await assertDb.WorkOrderCosts.Include(x => x.Details)
            .SingleAsync(x => x.WorkOrderId == "WO-CAP");
        var voucher = await assertDb.JournalVouchers.Include(x => x.Lines).SingleAsync();
        Assert.Equal(120m, persistedCost.LaborCost);
        Assert.Equal("CNY", persistedCost.LaborCurrencyCode);
        Assert.Equal(120m, persistedCost.WipClearedCost);
        Assert.Equal(voucher.Lines.Sum(x => x.DebitAmount), voucher.Lines.Sum(x => x.CreditAmount));
        Assert.Equal(40m, voucher.Lines.Sum(x => x.DebitAmount));
        var persistedMixedCost = await assertDb.WorkOrderCosts.Include(x => x.Details)
            .SingleAsync(x => x.WorkOrderId == "WO-MIXED");
        Assert.Equal("USD", persistedMixedCost.LaborCurrencyCode);
        Assert.Equal(80m, persistedMixedCost.LaborCost);
        Assert.DoesNotContain(await assertDb.OperationLaborSettlements.ToListAsync(),
            x => x.OperationTaskId == "OP-MIXED");
        Assert.DoesNotContain(await assertDb.OperationLaborSettlementStates.ToListAsync(),
            x => x.OperationTaskId == "OP-MIXED");
        Assert.DoesNotContain(await assertDb.ProcessedIntegrationEvents.ToListAsync(),
            x => x.EventId == "evt-cap-mixed");
        Assert.Equal("incompatible-work-order-labor-currency",
            Assert.Single(await deadLetters.ListAsync(
                MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost.ConsumerName,
                IntegrationEventDeadLetterStatus.Pending,
                CancellationToken.None)).FailureCode);
        var persistedReady = await assertDb.WorkOrderCosts.Include(x => x.Details)
            .SingleAsync(x => x.WorkOrderId == "WO-READY");
        Assert.True(persistedReady.CapitalizationPublished);
        Assert.Equal(0m, persistedReady.CapitalizedQuantity);
        Assert.Equal(0m, persistedReady.LaborCost);
        Assert.Single(await assertDb.JournalVouchers.ToListAsync());

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

    [ErpCostPostgresFact]
    public async Task PostgreSQL_partial_capitalization_settle_void_and_final_receipt_persist_balanced_vouchers()
    {
        await ErpPostgresLaneDatabase.ResetSchemaAsync();
        var options = ErpPostgresLaneDatabase.CreateOptions();
        await using var db = new ApplicationDbContext(options, new NoopMediator());
        await db.Database.MigrateAsync();
        db.WorkCenterCostRates.Add(WorkCenterCostRate.Define(
            "org-partial", "env-partial", "WC-PARTIAL", 80m, "CNY",
            DateTimeOffset.Parse("2026-08-01T00:00:00Z"), null, 1,
            "system:test", "partial capitalization rate", DateTimeOffset.Parse("2026-08-01T00:00:00Z")));
        var cost = WorkOrderCost.Open("org-partial", "env-partial", "WO-PARTIAL", "FG-PARTIAL");
        cost.RecordLabor("RPT-PARTIAL", "WC-PARTIAL", 2m, 80m, "CNY", false,
            DateTimeOffset.Parse("2026-08-31T15:40:00Z"));
        cost.Complete(10m, 1, 0, DateTimeOffset.Parse("2026-08-31T15:50:00Z"));
        db.WorkOrderCosts.Add(cost);
        await db.SaveChangesAsync();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var receiptConsumer = new StockMovementPostedIntegrationEventHandlerForAccumulateMaterialCost(db, deadLetters, db);
        await receiptConsumer.HandleAsync(PartialReceipt("evt-pg-partial", "MOVE-PG-PARTIAL", "FGR-PG-PARTIAL"), CancellationToken.None);

        var settled = new MesOperationActualTimeSettledIntegrationEvent(
            "evt-pg-partial-settle", MesIntegrationEventTypes.OperationActualTimeSettled, 1,
            DateTimeOffset.Parse("2026-08-31T16:00:00Z"), MesIntegrationEventSources.BusinessMes,
            "correlation-pg-partial", "causation-pg-partial", "org-partial", "env-partial",
            "operator:test", "actual-time:OP-PARTIAL:1:settled",
            new OperationActualTimeSettledPayload(
                "WO-PARTIAL", "OP-PARTIAL", "WC-PARTIAL", 1,
                DateTimeOffset.Parse("2026-08-31T15:50:00Z"),
                90 * TimeSpan.TicksPerMinute, 90 * TimeSpan.TicksPerMinute, ["RPT-PARTIAL"]));
        await new MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost(
                db, db, new PostgreSqlWorkOrderCostMutationLock(db),
                new OperationLaborSettlementOrchestrator(db, deadLetters))
            .HandleAsync(settled, CancellationToken.None);
        await new MesOperationActualTimeSettlementVoidedIntegrationEventHandlerForReverseLaborCost(
                db, db, new PostgreSqlWorkOrderCostMutationLock(db),
                new OperationLaborSettlementOrchestrator(db, deadLetters))
            .HandleAsync(new MesOperationActualTimeSettlementVoidedIntegrationEvent(
                "evt-pg-partial-void", MesIntegrationEventTypes.OperationActualTimeSettlementVoided, 1,
                DateTimeOffset.Parse("2026-08-31T16:05:00Z"), MesIntegrationEventSources.BusinessMes,
                "correlation-pg-partial", settled.EventId, "org-partial", "env-partial",
                "operator:test", "actual-time:OP-PARTIAL:1:voided",
                new OperationActualTimeSettlementVoidedPayload(
                    "WO-PARTIAL", "OP-PARTIAL", "WC-PARTIAL", 1,
                    DateTimeOffset.Parse("2026-08-31T15:50:00Z"),
                    DateTimeOffset.Parse("2026-08-31T16:05:00Z"),
                    90 * TimeSpan.TicksPerMinute, 90 * TimeSpan.TicksPerMinute, ["RPT-PARTIAL"])),
                CancellationToken.None);
        await receiptConsumer.HandleAsync(PartialReceipt("evt-pg-final", "MOVE-PG-FINAL", "FGR-PG-FINAL"), CancellationToken.None);

        await using var assertDb = new ApplicationDbContext(options, new NoopMediator());
        var persisted = await assertDb.WorkOrderCosts.Include(x => x.Details)
            .SingleAsync(x => x.WorkOrderId == "WO-PARTIAL");
        Assert.Equal(0m, persisted.LaborCost);
        Assert.Equal(10m, persisted.CapitalizedQuantity);
        Assert.Equal(0m, persisted.WipClearedCost);
        var vouchers = await assertDb.JournalVouchers.Include(x => x.Lines).ToListAsync();
        Assert.Equal(2, vouchers.Count);
        Assert.All(vouchers, voucher =>
            Assert.Equal(voucher.Lines.Sum(x => x.DebitAmount), voucher.Lines.Sum(x => x.CreditAmount)));
        var lines = vouchers.SelectMany(x => x.Lines).ToList();
        Assert.Equal(160m, lines.Where(x => x.AccountCode == "1406-FINISHED-GOODS").Sum(x => x.DebitAmount));
        Assert.Equal(80m, lines.Where(x => x.AccountCode == "1405-WIP").Sum(x => x.DebitAmount));
        Assert.Equal(80m, lines.Where(x => x.AccountCode == "1405-WIP").Sum(x => x.CreditAmount));
        var varianceLine = Assert.Single(lines, x => x.AccountCode == "5101-PRODUCTION-VARIANCE");
        Assert.Equal(0m, varianceLine.DebitAmount);
        Assert.Equal(160m, varianceLine.CreditAmount);
        Assert.Equal(
            persisted.WipClearedCost,
            lines.Where(x => x.AccountCode == "1405-WIP").Sum(x => x.CreditAmount - x.DebitAmount));
        Assert.Empty(await deadLetters.ListAsync(
            MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));

        static StockMovementPostedIntegrationEvent PartialReceipt(string eventId, string movementId, string receiptId)
            => new(
                eventId, InventoryIntegrationEventTypes.StockMovementPosted, 1,
                DateTimeOffset.Parse("2026-08-31T15:55:00Z"), InventoryIntegrationEventSources.BusinessInventory,
                receiptId, receiptId, "org-partial", "env-partial", "inventory", movementId,
                new StockMovementPostedPayload(
                    movementId, "inbound", InventoryIntegrationEventSources.BusinessMes,
                    receiptId, "WO-PARTIAL", $"mes:finished-goods-receipt:{receiptId}",
                    "FG-PARTIAL", "ea", "finished-goods", "receiving", null, null,
                    "unrestricted", "organization", "org-partial", 5m,
                    DateTimeOffset.Parse("2026-08-31T15:55:00Z"), 16m, 80m));
    }

    [ErpCostPostgresFact]
    public async Task PostgreSQL_rejects_duplicate_settlement_void_and_covered_report_without_half_commit()
    {
        await ErpPostgresLaneDatabase.ResetSchemaAsync();
        var options = ErpPostgresLaneDatabase.CreateOptions();
        await using (var seed = new ApplicationDbContext(options, new NoopMediator()))
        {
            await seed.Database.MigrateAsync();
            var rate = WorkCenterCostRate.Define("org-unique", "env-unique", "WC-UNIQUE", 80m, "CNY", DateTimeOffset.Parse("2026-08-01T00:00:00Z"), null, 1, "system:test", "unique proof", DateTimeOffset.Parse("2026-08-01T00:00:00Z"));
            seed.WorkCenterCostRates.Add(rate);
            var settlement = OperationLaborSettlement.Create("org-unique", "env-unique", "WO-UNIQUE", "OP-UNIQUE", "WC-UNIQUE", 1, DateTimeOffset.Parse("2026-08-31T15:00:00Z"), TimeSpan.TicksPerHour, rate.Id, 1, "CNY", 80m, "evt-unique", new string('a', 64));
            seed.OperationLaborSettlements.Add(settlement);
            seed.OperationLaborSettlementVoids.Add(OperationLaborSettlementVoid.Create(settlement, DateTimeOffset.Parse("2026-08-31T16:00:00Z"), "evt-void", new string('b', 64)));
            seed.OperationLaborCoveredReports.Add(OperationLaborCoveredReport.Create("org-unique", "env-unique", "WO-UNIQUE", "OP-UNIQUE", 1, "RPT-UNIQUE"));
            var machineRate = WorkCenterMachineOverheadRate.DefineApplicable(
                "org-unique", "env-unique", "WC-UNIQUE", "2026-08",
                30_000m, 10_000m, 1_000m, "CNY", 1,
                "system:test", "machine unique proof", DateTimeOffset.Parse("2026-08-01T00:00:00Z"));
            seed.WorkCenterMachineOverheadRates.Add(machineRate);
            var machineSettlement = OperationMachineOverheadSettlement.CreateApplied(
                "org-unique", "env-unique", "WO-UNIQUE", "OP-UNIQUE", "WC-UNIQUE", 1,
                DateTimeOffset.Parse("2026-08-31T15:00:00Z"), "DEVICE-UNIQUE",
                TimeSpan.TicksPerHour, MesMachineTimeBasisCodes.SingleDeviceActiveMinusExplicitPauseV1,
                machineRate.Id, "2026-08", 1, "CNY", 30m, 10m,
                "evt-machine-unique", new string('e', 64));
            seed.OperationMachineOverheadSettlements.Add(machineSettlement);
            seed.OperationMachineOverheadSettlementVoids.Add(OperationMachineOverheadSettlementVoid.Create(
                machineSettlement, DateTimeOffset.Parse("2026-08-31T16:00:00Z"),
                "evt-machine-void", new string('f', 64)));
            await seed.SaveChangesAsync();
        }

        await AssertConstraintAsync(options, "ux_operation_labor_settlements_business_identity", db =>
        {
            var rateId = db.WorkCenterCostRates.Select(x => x.Id).Single();
            db.OperationLaborSettlements.Add(OperationLaborSettlement.Create("org-unique", "env-unique", "WO-DUP", "OP-UNIQUE", "WC-UNIQUE", 1, DateTimeOffset.Parse("2026-08-31T15:00:00Z"), TimeSpan.TicksPerHour, rateId, 1, "CNY", 80m, "evt-dup", new string('c', 64)));
        });
        await AssertConstraintAsync(options, "ux_operation_labor_settlement_voids_business_identity", db =>
        {
            var settlement = db.OperationLaborSettlements.Single();
            db.OperationLaborSettlementVoids.Add(OperationLaborSettlementVoid.Create(settlement, DateTimeOffset.Parse("2026-08-31T17:00:00Z"), "evt-void-dup", new string('d', 64)));
        });
        await AssertConstraintAsync(options, "ux_operation_labor_covered_reports_report", db =>
            db.OperationLaborCoveredReports.Add(OperationLaborCoveredReport.Create("org-unique", "env-unique", "WO-DUP", "OP-DUP", 2, "RPT-UNIQUE")));
        await AssertConstraintAsync(options, "ux_op_machine_overhead_settlements_identity", db =>
        {
            var rateId = db.WorkCenterMachineOverheadRates.Select(x => x.Id).Single();
            db.OperationMachineOverheadSettlements.Add(OperationMachineOverheadSettlement.CreateApplied(
                "org-unique", "env-unique", "WO-DUP", "OP-UNIQUE", "WC-UNIQUE", 1,
                DateTimeOffset.Parse("2026-08-31T15:00:00Z"), "DEVICE-DUP", TimeSpan.TicksPerHour,
                MesMachineTimeBasisCodes.SingleDeviceActiveMinusExplicitPauseV1,
                rateId, "2026-08", 1, "CNY", 30m, 10m, "evt-machine-dup", new string('1', 64)));
        });
        await AssertConstraintAsync(options, "ux_op_machine_overhead_settlement_voids_identity", db =>
        {
            var settlement = db.OperationMachineOverheadSettlements.Single();
            db.OperationMachineOverheadSettlementVoids.Add(OperationMachineOverheadSettlementVoid.Create(
                settlement, DateTimeOffset.Parse("2026-08-31T17:00:00Z"),
                "evt-machine-void-dup", new string('2', 64)));
        });

        await using var verify = new ApplicationDbContext(options, new NoopMediator());
        Assert.Equal(1, await verify.OperationLaborSettlements.CountAsync());
        Assert.Equal(1, await verify.OperationLaborSettlementVoids.CountAsync());
        Assert.Equal(1, await verify.OperationLaborCoveredReports.CountAsync());
        Assert.Equal(1, await verify.OperationMachineOverheadSettlements.CountAsync());
        Assert.Equal(1, await verify.OperationMachineOverheadSettlementVoids.CountAsync());
    }

    private static async Task AssertConstraintAsync(DbContextOptions<ApplicationDbContext> options, string constraintName, Action<ApplicationDbContext> arrange)
    {
        await using var db = new ApplicationDbContext(options, new NoopMediator());
        arrange(db);
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var postgres = Assert.IsType<PostgresException>(error.InnerException);
        Assert.Equal(constraintName, postgres.ConstraintName);
    }

    private static MesOperationActualTimeSettledV2IntegrationEventHandlerForAccumulateMachineOverhead MachineSettlementConsumer(
        ApplicationDbContext db,
        InMemoryIntegrationEventDeadLetterStore deadLetters)
        => new(
            db,
            db,
            new PostgreSqlWorkOrderCostMutationLock(db),
            new OperationMachineOverheadSettlementOrchestrator(db, deadLetters, new PostgreSqlErpAdvisoryLockAllocator(db)));

    private static MesOperationActualTimeSettlementVoidedV2IntegrationEventHandlerForReverseMachineOverhead MachineVoidConsumer(
        ApplicationDbContext db,
        InMemoryIntegrationEventDeadLetterStore deadLetters)
        => new(
            db,
            db,
            new PostgreSqlWorkOrderCostMutationLock(db),
            new OperationMachineOverheadSettlementOrchestrator(db, deadLetters, new PostgreSqlErpAdvisoryLockAllocator(db)));

    private static MesOperationActualTimeSettledV2IntegrationEvent MachineSettled(
        string eventId,
        string organizationId,
        string environmentId,
        string workOrderId,
        string operationTaskId,
        string workCenterId,
        DateTimeOffset completedAtUtc,
        long? billableMachineTicks,
        MesMachineTimeFactStatus status = MesMachineTimeFactStatus.Available)
        => new(
            eventId,
            MesIntegrationEventTypes.OperationActualTimeSettled,
            MesIntegrationEventVersions.V2,
            completedAtUtc.AddMinutes(1),
            MesIntegrationEventSources.BusinessMes,
            $"correlation-{eventId}",
            $"causation-{eventId}",
            organizationId,
            environmentId,
            "operator:test",
            $"actual-time:{operationTaskId}:1:settled:v2",
            new OperationActualTimeSettledV2Payload(
                workOrderId,
                operationTaskId,
                workCenterId,
                1,
                completedAtUtc,
                TimeSpan.TicksPerHour,
                billableMachineTicks ?? 0,
                [],
                status == MesMachineTimeFactStatus.Available ? $"DEVICE-{operationTaskId}" : null,
                status,
                status == MesMachineTimeFactStatus.Available ? billableMachineTicks : null,
                status == MesMachineTimeFactStatus.Available
                    ? MesMachineTimeBasisCodes.SingleDeviceActiveMinusExplicitPauseV1
                    : null));

    private static MesOperationActualTimeSettlementVoidedV2IntegrationEvent MachineVoided(
        string eventId,
        MesOperationActualTimeSettledV2IntegrationEvent settled,
        DateTimeOffset voidedAtUtc)
        => new(
            eventId,
            MesIntegrationEventTypes.OperationActualTimeSettlementVoided,
            MesIntegrationEventVersions.V2,
            voidedAtUtc,
            MesIntegrationEventSources.BusinessMes,
            settled.CorrelationId,
            settled.EventId,
            settled.OrganizationId,
            settled.EnvironmentId,
            "operator:test",
            $"actual-time:{settled.Payload.OperationTaskId}:{settled.Payload.SettlementRevision}:voided:v2",
            new OperationActualTimeSettlementVoidedV2Payload(
                settled.Payload.WorkOrderId,
                settled.Payload.OperationTaskId,
                settled.Payload.WorkCenterId,
                settled.Payload.SettlementRevision,
                settled.Payload.CompletedAtUtc,
                voidedAtUtc,
                settled.Payload.ActualLaborTicks,
                settled.Payload.ActualMachineTicks,
                settled.Payload.CoveredProductionReportNos,
                settled.Payload.DeviceAssetId,
                settled.Payload.MachineTimeStatus,
                settled.Payload.BillableMachineTicks,
                settled.Payload.MachineTimeBasisCode));

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
