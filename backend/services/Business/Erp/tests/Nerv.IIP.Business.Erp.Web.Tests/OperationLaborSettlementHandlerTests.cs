using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkOrderCostAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Erp.Web.Tests;

public sealed class OperationLaborSettlementHandlerTests
{
    private static readonly DateTimeOffset AugustCompletedAtUtc =
        DateTimeOffset.Parse("2026-08-31T15:50:00Z");
    private static readonly DateTimeOffset SeptemberStartsAtUtc =
        DateTimeOffset.Parse("2026-09-01T00:00:00Z");

    [Fact]
    public async Task Settlement_uses_completed_at_rate_and_replaces_covered_theoretical_labor()
    {
        await using var db = CreateDb();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        db.WorkCenterCostRates.AddRange(
            Rate(7, 80m, DateTimeOffset.Parse("2026-08-01T00:00:00Z"), SeptemberStartsAtUtc),
            Rate(8, 88m, SeptemberStartsAtUtc, null));
        await db.SaveChangesAsync();

        await new ProductionReportRecordedIntegrationEventHandlerForAccumulateLaborCost(db, deadLetters, db, TestWorkOrderCostMutationLock.Instance)
            .HandleAsync(Report("evt-report-001", "RPT-001", AugustCompletedAtUtc.AddMinutes(-10)), CancellationToken.None);
        await new MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost(db, db, TestWorkOrderCostMutationLock.Instance, new OperationLaborSettlementOrchestrator(db, deadLetters))
            .HandleAsync(Settled("evt-settled-r1", 1, AugustCompletedAtUtc, 2 * TimeSpan.TicksPerHour, ["RPT-001"]), CancellationToken.None);

        var cost = await db.WorkOrderCosts.Include(x => x.Details).SingleAsync();
        var settlement = await db.OperationLaborSettlements.SingleAsync();
        Assert.Equal(160m, cost.LaborCost);
        Assert.Equal(3, cost.Details.Count);
        Assert.Equal(160m, settlement.Amount);
        Assert.Equal(7, settlement.RateRevision);
        Assert.Equal("CNY", settlement.CurrencyCode);
        Assert.Equal("standard", settlement.RateBasis);
        Assert.Equal(AugustCompletedAtUtc, settlement.RateBasisAtUtc);
        Assert.Contains(cost.Details, x => x.LaborBasis == LaborCostBasis.ActualOperation && x.Amount == 160m);
        Assert.Contains(cost.Details, x => x.LaborBasis == LaborCostBasis.TheoreticalReportReplacement && x.Amount == -160m);
    }

    [Fact]
    public async Task Settlement_on_rate_boundary_uses_new_revision_regardless_of_delivery_or_retry_time()
    {
        await using var db = CreateDb();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        db.WorkCenterCostRates.AddRange(
            Rate(7, 80m, DateTimeOffset.Parse("2026-08-01T00:00:00Z"), SeptemberStartsAtUtc),
            Rate(8, 88m, SeptemberStartsAtUtc, null));
        await db.SaveChangesAsync();
        var original = Settled(
            "evt-settled-boundary", 1, SeptemberStartsAtUtc,
            2 * TimeSpan.TicksPerHour, ["RPT-BOUNDARY"])
            with
        { OccurredAtUtc = SeptemberStartsAtUtc.AddMonths(3) };
        var consumer = new MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost(db, db, TestWorkOrderCostMutationLock.Instance, new OperationLaborSettlementOrchestrator(db, deadLetters));

        await consumer.HandleAsync(original, CancellationToken.None);
        await consumer.HandleAsync(
            original with
            {
                EventId = "evt-settled-boundary-retry",
                IdempotencyKey = "actual-time:OP-001:1:settled:retry",
                OccurredAtUtc = SeptemberStartsAtUtc.AddMonths(6),
            },
            CancellationToken.None);

        var settlement = await db.OperationLaborSettlements.SingleAsync();
        Assert.Equal(8, settlement.RateRevision);
        Assert.Equal(176m, settlement.Amount);
        Assert.Single((await db.WorkOrderCosts.Include(x => x.Details).SingleAsync()).Details,
            x => x.LaborBasis == LaborCostBasis.ActualOperation);
    }

    [Fact]
    public async Task Settlement_delivered_after_rate_boundary_still_uses_completion_period_rate()
    {
        await using var db = CreateDb();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        db.WorkCenterCostRates.AddRange(
            Rate(7, 80m, DateTimeOffset.Parse("2026-08-01T00:00:00Z"), SeptemberStartsAtUtc),
            Rate(8, 88m, SeptemberStartsAtUtc, null));
        await db.SaveChangesAsync();
        var delayed = Settled(
            "evt-settled-delayed", 1, AugustCompletedAtUtc,
            2 * TimeSpan.TicksPerHour, ["RPT-DELAYED"])
            with
        { OccurredAtUtc = SeptemberStartsAtUtc.AddMonths(3) };

        await new MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost(
                db, db, TestWorkOrderCostMutationLock.Instance,
                new OperationLaborSettlementOrchestrator(db, deadLetters))
            .HandleAsync(delayed, CancellationToken.None);

        var settlement = await db.OperationLaborSettlements.SingleAsync();
        Assert.Equal(7, settlement.RateRevision);
        Assert.Equal(160m, settlement.Amount);
    }

    [Fact]
    public async Task Settlement_rate_selection_is_isolated_by_organization_and_environment()
    {
        await using var db = CreateDb();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        db.WorkCenterCostRates.AddRange(
            Rate(7, 80m, DateTimeOffset.Parse("2026-08-01T00:00:00Z"), null),
            WorkCenterCostRate.Define(
                "org-001", "env-other", "WC-01", 999m, "CNY",
                DateTimeOffset.Parse("2026-08-01T00:00:00Z"), null, 99,
                "system:test", "other environment rate", DateTimeOffset.Parse("2026-08-01T00:00:00Z")));
        await db.SaveChangesAsync();

        await new MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost(
                db, db, TestWorkOrderCostMutationLock.Instance,
                new OperationLaborSettlementOrchestrator(db, deadLetters))
            .HandleAsync(
                Settled("evt-settled-scoped", 1, AugustCompletedAtUtc,
                    2 * TimeSpan.TicksPerHour, ["RPT-SCOPED"]),
                CancellationToken.None);

        var settlement = await db.OperationLaborSettlements.SingleAsync();
        Assert.Equal("env-prod", settlement.EnvironmentId);
        Assert.Equal(7, settlement.RateRevision);
        Assert.Equal(160m, settlement.Amount);
    }

    [Fact]
    public async Task Covered_report_arriving_after_settlement_never_adds_theoretical_labor()
    {
        await using var db = CreateDb();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        db.WorkCenterCostRates.Add(Rate(
            7,
            80m,
            DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
            SeptemberStartsAtUtc));
        await db.SaveChangesAsync();

        await new MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost(db, db, TestWorkOrderCostMutationLock.Instance, new OperationLaborSettlementOrchestrator(db, deadLetters))
            .HandleAsync(Settled("evt-settled-r1", 1, AugustCompletedAtUtc, 2 * TimeSpan.TicksPerHour, ["RPT-001"]), CancellationToken.None);
        await new ProductionReportRecordedIntegrationEventHandlerForAccumulateLaborCost(db, deadLetters, db, TestWorkOrderCostMutationLock.Instance)
            .HandleAsync(Report("evt-report-001", "RPT-001", AugustCompletedAtUtc.AddMinutes(-10)), CancellationToken.None);

        var cost = await db.WorkOrderCosts.Include(x => x.Details).SingleAsync();
        Assert.Equal(160m, cost.LaborCost);
        Assert.Equal(1, cost.ReceivedReportCount);
        Assert.DoesNotContain(cost.Details, x => x.LaborBasis == LaborCostBasis.TheoreticalReport);
        Assert.Contains(cost.Details, x => x.LaborBasis == LaborCostBasis.UncostedReport && x.Amount == 0m);
    }

    [Fact]
    public async Task Void_exactly_reverses_the_frozen_rate_after_new_rate_revision_exists()
    {
        await using var db = CreateDb();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        db.WorkCenterCostRates.Add(Rate(
            7,
            80m,
            DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
            SeptemberStartsAtUtc));
        await db.SaveChangesAsync();
        var settled = Settled(
            "evt-settled-r1",
            1,
            AugustCompletedAtUtc,
            2 * TimeSpan.TicksPerHour,
            ["RPT-001"]);

        await new MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost(db, db, TestWorkOrderCostMutationLock.Instance, new OperationLaborSettlementOrchestrator(db, deadLetters))
            .HandleAsync(settled, CancellationToken.None);
        db.WorkCenterCostRates.Add(Rate(
            9,
            999m,
            DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
            SeptemberStartsAtUtc));
        await db.SaveChangesAsync();

        var voided = Voided("evt-void-r1", settled, SeptemberStartsAtUtc.AddDays(1));
        var consumer = new MesOperationActualTimeSettlementVoidedIntegrationEventHandlerForReverseLaborCost(db, db, TestWorkOrderCostMutationLock.Instance, new OperationLaborSettlementOrchestrator(db, deadLetters));
        await consumer.HandleAsync(voided, CancellationToken.None);
        await consumer.HandleAsync(voided with { EventId = "evt-void-r1-retry", IdempotencyKey = "actual-time:OP-001:1:voided:retry" }, CancellationToken.None);

        var cost = await db.WorkOrderCosts.Include(x => x.Details).SingleAsync();
        var reversal = await db.OperationLaborSettlementVoids.SingleAsync();
        Assert.Equal(0m, cost.LaborCost);
        Assert.Equal(-160m, reversal.Amount);
        Assert.Equal(7, reversal.RateRevision);
        Assert.Equal(80m, reversal.HourlyRate);
        Assert.Single(cost.Details, x => x.LaborBasis == LaborCostBasis.ActualOperationVoid);
    }

    [Fact]
    public async Task Reopen_settlement_with_an_incompatible_currency_fails_closed()
    {
        await using var db = CreateDb();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        db.WorkCenterCostRates.AddRange(
            Rate(7, 80m, DateTimeOffset.Parse("2026-08-01T00:00:00Z"), SeptemberStartsAtUtc),
            Rate(8, 88m, SeptemberStartsAtUtc, null, "USD"));
        await db.SaveChangesAsync();
        var consumer = new MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost(db, db, TestWorkOrderCostMutationLock.Instance, new OperationLaborSettlementOrchestrator(db, deadLetters));

        await consumer.HandleAsync(
            Settled("evt-settled-r1", 1, AugustCompletedAtUtc, 2 * TimeSpan.TicksPerHour, ["RPT-001"]),
            CancellationToken.None);
        await consumer.HandleAsync(
            Settled("evt-settled-r2", 2, SeptemberStartsAtUtc.AddDays(2), 90 * TimeSpan.TicksPerMinute, ["RPT-002"]),
            CancellationToken.None);

        var cost = await db.WorkOrderCosts.Include(x => x.Details).SingleAsync();
        Assert.Equal(160m, cost.LaborCost);
        Assert.Single(await db.OperationLaborSettlements.ToListAsync());
        var deadLetter = Assert.Single(await deadLetters.ListAsync(
            MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
        Assert.Equal("incompatible-work-order-labor-currency", deadLetter.FailureCode);
        Assert.DoesNotContain(await db.ProcessedIntegrationEvents.ToListAsync(), x => x.EventId == "evt-settled-r2");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Theoretical_usd_then_actual_cny_fails_closed_without_partial_commit(bool capitalized)
    {
        await using var db = CreateDb();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        db.WorkCenterCostRates.AddRange(
            Rate(7, 80m, DateTimeOffset.Parse("2026-08-01T00:00:00Z"), SeptemberStartsAtUtc, "USD"),
            Rate(8, 88m, SeptemberStartsAtUtc, null, "CNY"));
        await db.SaveChangesAsync();
        await new ProductionReportRecordedIntegrationEventHandlerForAccumulateLaborCost(
                db, deadLetters, db, TestWorkOrderCostMutationLock.Instance)
            .HandleAsync(Report("evt-report-usd", "RPT-USD", AugustCompletedAtUtc), CancellationToken.None);
        var cost = await db.WorkOrderCosts.Include(x => x.Details).SingleAsync();
        if (capitalized)
        {
            cost.Complete(10m, 1, 0, AugustCompletedAtUtc.AddMinutes(1));
            cost.Capitalize("MOVE-FG-USD", 10m, 16m, AugustCompletedAtUtc.AddMinutes(2));
            cost.RecordWipClearance(160m);
            await db.SaveChangesAsync();
        }

        await new MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost(
                db, db, TestWorkOrderCostMutationLock.Instance,
                new OperationLaborSettlementOrchestrator(db, deadLetters))
            .HandleAsync(
                Settled("evt-settled-cny", 1, SeptemberStartsAtUtc,
                    2 * TimeSpan.TicksPerHour, ["RPT-USD"]),
                CancellationToken.None);

        cost = await db.WorkOrderCosts.Include(x => x.Details).SingleAsync();
        Assert.Equal("USD", cost.LaborCurrencyCode);
        Assert.Equal(160m, cost.LaborCost);
        Assert.Single(cost.Details);
        Assert.Empty(await db.OperationLaborSettlements.ToListAsync());
        Assert.Empty(await db.OperationLaborSettlementStates.ToListAsync());
        Assert.Empty(await db.OperationLaborCoveredReports.ToListAsync());
        Assert.Empty(await db.JournalVouchers.ToListAsync());
        Assert.DoesNotContain(await db.ProcessedIntegrationEvents.ToListAsync(),
            x => x.EventId == "evt-settled-cny");
        Assert.Equal("incompatible-work-order-labor-currency",
            Assert.Single(await deadLetters.ListAsync(
                MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost.ConsumerName,
                IntegrationEventDeadLetterStatus.Pending,
                CancellationToken.None)).FailureCode);
    }

    [Fact]
    public async Task Existing_priced_labor_with_unknown_currency_fails_closed_without_backfill()
    {
        await using var db = CreateDb();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        db.WorkCenterCostRates.Add(Rate(
            7, 80m, DateTimeOffset.Parse("2026-08-01T00:00:00Z"), null));
        var cost = WorkOrderCost.Open("org-001", "env-prod", "WO-001", "FG-001");
        cost.RecordLabor("RPT-HISTORY", "WC-01", 2m, 80m, "CNY", false, AugustCompletedAtUtc);
        typeof(WorkOrderCost).GetProperty(nameof(WorkOrderCost.LaborCurrencyCode))!
            .SetValue(cost, null);
        db.WorkOrderCosts.Add(cost);
        await db.SaveChangesAsync();

        await new MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost(
                db, db, TestWorkOrderCostMutationLock.Instance,
                new OperationLaborSettlementOrchestrator(db, deadLetters))
            .HandleAsync(
                Settled("evt-settled-history", 1, AugustCompletedAtUtc,
                    2 * TimeSpan.TicksPerHour, ["RPT-HISTORY"]),
                CancellationToken.None);

        cost = await db.WorkOrderCosts.Include(x => x.Details).SingleAsync();
        Assert.Null(cost.LaborCurrencyCode);
        Assert.Equal(160m, cost.LaborCost);
        Assert.Empty(await db.OperationLaborSettlements.ToListAsync());
        Assert.Empty(await db.ProcessedIntegrationEvents.Where(x => x.EventId == "evt-settled-history").ToListAsync());
        Assert.Equal("incompatible-work-order-labor-currency",
            Assert.Single(await deadLetters.ListAsync(
                MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost.ConsumerName,
                IntegrationEventDeadLetterStatus.Pending,
                CancellationToken.None)).FailureCode);
    }

    [Fact]
    public async Task Missing_rate_leaves_no_successful_fact_and_the_same_event_can_be_replayed()
    {
        await using var db = CreateDb();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var settlement = Settled(
            "evt-settled-r1",
            1,
            AugustCompletedAtUtc,
            2 * TimeSpan.TicksPerHour,
            ["RPT-001"]);
        var consumer = new MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost(db, db, TestWorkOrderCostMutationLock.Instance, new OperationLaborSettlementOrchestrator(db, deadLetters));

        await consumer.HandleAsync(settlement, CancellationToken.None);

        Assert.Empty(await db.ProcessedIntegrationEvents.ToListAsync());
        Assert.Empty(await db.OperationLaborSettlements.ToListAsync());
        Assert.Empty(await db.OperationLaborCoveredReports.ToListAsync());
        Assert.Empty(await db.WorkOrderCosts.ToListAsync());

        db.WorkCenterCostRates.Add(Rate(
            7,
            80m,
            DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
            SeptemberStartsAtUtc));
        await db.SaveChangesAsync();
        await consumer.HandleAsync(settlement, CancellationToken.None);

        Assert.Single(await db.ProcessedIntegrationEvents.ToListAsync());
        Assert.Equal(160m, (await db.OperationLaborSettlements.SingleAsync()).Amount);
    }

    [Fact]
    public async Task Conflicting_payload_for_the_same_business_revision_is_dead_lettered_without_overwrite()
    {
        await using var db = CreateDb();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        db.WorkCenterCostRates.Add(Rate(
            7,
            80m,
            DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
            SeptemberStartsAtUtc));
        await db.SaveChangesAsync();
        var original = Settled(
            "evt-settled-r1",
            1,
            AugustCompletedAtUtc,
            2 * TimeSpan.TicksPerHour,
            ["RPT-001"]);
        var consumer = new MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost(db, db, TestWorkOrderCostMutationLock.Instance, new OperationLaborSettlementOrchestrator(db, deadLetters));
        await consumer.HandleAsync(original, CancellationToken.None);

        var conflict = original with
        {
            EventId = "evt-settled-r1-conflict",
            IdempotencyKey = "actual-time:OP-001:1:settled:conflict",
            Payload = original.Payload with { ActualLaborTicks = 3 * TimeSpan.TicksPerHour },
        };
        await consumer.HandleAsync(conflict, CancellationToken.None);

        var frozen = await db.OperationLaborSettlements.SingleAsync();
        Assert.Equal(2 * TimeSpan.TicksPerHour, frozen.ActualLaborTicks);
        Assert.Equal(160m, (await db.WorkOrderCosts.Include(x => x.Details).SingleAsync()).LaborCost);
        Assert.DoesNotContain(await db.ProcessedIntegrationEvents.ToListAsync(), x => x.EventId == conflict.EventId);
        var deadLetter = Assert.Single(await deadLetters.ListAsync(
            MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
        Assert.Equal("conflicting-operation-labor-settlement", deadLetter.FailureCode);
    }

    [Theory]
    [InlineData("WO-002", "WC-01")]
    [InlineData("WO-001", "WC-02")]
    public async Task Higher_revision_cannot_cross_the_frozen_work_order_or_work_center(
        string conflictingWorkOrderId,
        string conflictingWorkCenterId)
    {
        await using var db = CreateDb();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        db.WorkCenterCostRates.AddRange(
            Rate(7, 80m, DateTimeOffset.Parse("2026-08-01T00:00:00Z"), null),
            WorkCenterCostRate.Define(
                "org-001", "env-prod", "WC-02", 90m, "CNY",
                DateTimeOffset.Parse("2026-08-01T00:00:00Z"), null, 1,
                "operator:test", "alternate work center", DateTimeOffset.Parse("2026-08-01T00:00:00Z")));
        await db.SaveChangesAsync();
        var consumer = new MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost(
            db, db, TestWorkOrderCostMutationLock.Instance,
            new OperationLaborSettlementOrchestrator(db, deadLetters));

        await consumer.HandleAsync(
            Settled("evt-settled-r1", 1, AugustCompletedAtUtc,
                2 * TimeSpan.TicksPerHour, ["RPT-001"]),
            CancellationToken.None);
        var conflictingRevision = Settled(
            "evt-settled-r2-cross-scope", 2, AugustCompletedAtUtc.AddHours(1),
            3 * TimeSpan.TicksPerHour, ["RPT-002"])
            with
        {
            Payload = Settled(
                "evt-settled-r2-cross-scope", 2, AugustCompletedAtUtc.AddHours(1),
                3 * TimeSpan.TicksPerHour, ["RPT-002"]).Payload with
            {
                WorkOrderId = conflictingWorkOrderId,
                WorkCenterId = conflictingWorkCenterId,
            },
        };

        await consumer.HandleAsync(conflictingRevision, CancellationToken.None);

        Assert.Single(await db.OperationLaborSettlements.ToListAsync());
        Assert.Equal(1, (await db.OperationLaborSettlementStates.SingleAsync()).ActiveRevision);
        Assert.Equal(160m, (await db.WorkOrderCosts.Include(x => x.Details).SingleAsync()).LaborCost);
        Assert.DoesNotContain(await db.ProcessedIntegrationEvents.ToListAsync(),
            x => x.EventId == conflictingRevision.EventId);
        var deadLetter = Assert.Single(await deadLetters.ListAsync(
            MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
        Assert.Equal("conflicting-operation-labor-settlement", deadLetter.FailureCode);
    }

    [Theory]
    [InlineData("WO-002", "WC-01")]
    [InlineData("WO-001", "WC-02")]
    public async Task Higher_revision_void_cannot_reverse_the_frozen_amount_into_another_work_order_or_work_center(
        string conflictingWorkOrderId,
        string conflictingWorkCenterId)
    {
        await using var db = CreateDb();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        db.WorkCenterCostRates.AddRange(
            Rate(7, 80m, DateTimeOffset.Parse("2026-08-01T00:00:00Z"), null),
            WorkCenterCostRate.Define(
                "org-001", "env-prod", "WC-02", 90m, "CNY",
                DateTimeOffset.Parse("2026-08-01T00:00:00Z"), null, 1,
                "operator:test", "alternate work center", DateTimeOffset.Parse("2026-08-01T00:00:00Z")));
        await db.SaveChangesAsync();
        var settledConsumer = new MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost(
            db, db, TestWorkOrderCostMutationLock.Instance,
            new OperationLaborSettlementOrchestrator(db, deadLetters));
        var voidConsumer = new MesOperationActualTimeSettlementVoidedIntegrationEventHandlerForReverseLaborCost(
            db, db, TestWorkOrderCostMutationLock.Instance,
            new OperationLaborSettlementOrchestrator(db, deadLetters));
        await settledConsumer.HandleAsync(
            Settled("evt-settled-r1", 1, AugustCompletedAtUtc,
                2 * TimeSpan.TicksPerHour, ["RPT-001"]),
            CancellationToken.None);
        var revisionTwo = Settled(
            "evt-settled-r2-cross-scope", 2, AugustCompletedAtUtc.AddHours(1),
            3 * TimeSpan.TicksPerHour, ["RPT-002"]);
        revisionTwo = revisionTwo with
        {
            Payload = revisionTwo.Payload with
            {
                WorkOrderId = conflictingWorkOrderId,
                WorkCenterId = conflictingWorkCenterId,
            },
        };

        await voidConsumer.HandleAsync(
            Voided("evt-void-r2-cross-scope", revisionTwo, AugustCompletedAtUtc.AddHours(2)),
            CancellationToken.None);

        Assert.Single(await db.OperationLaborSettlements.ToListAsync());
        Assert.Empty(await db.OperationLaborSettlementVoids.ToListAsync());
        Assert.Equal(1, (await db.OperationLaborSettlementStates.SingleAsync()).ActiveRevision);
        Assert.Equal(160m, (await db.WorkOrderCosts.Include(x => x.Details).SingleAsync()).LaborCost);
        var deadLetter = Assert.Single(await deadLetters.ListAsync(
            MesOperationActualTimeSettlementVoidedIntegrationEventHandlerForReverseLaborCost.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
        Assert.Equal("conflicting-operation-labor-settlement-void", deadLetter.FailureCode);
    }

    [Fact]
    public async Task Void_first_then_late_settlement_converges_to_zero_and_blocks_old_theoretical_labor()
    {
        await using var db = CreateDb();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        db.WorkCenterCostRates.Add(Rate(
            7,
            80m,
            DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
            SeptemberStartsAtUtc));
        await db.SaveChangesAsync();
        var settlement = Settled(
            "evt-settled-r1",
            1,
            AugustCompletedAtUtc,
            2 * TimeSpan.TicksPerHour,
            ["RPT-001"]);

        await new MesOperationActualTimeSettlementVoidedIntegrationEventHandlerForReverseLaborCost(db, db, TestWorkOrderCostMutationLock.Instance, new OperationLaborSettlementOrchestrator(db, deadLetters))
            .HandleAsync(Voided("evt-void-r1", settlement, SeptemberStartsAtUtc.AddDays(1)), CancellationToken.None);
        await new MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost(db, db, TestWorkOrderCostMutationLock.Instance, new OperationLaborSettlementOrchestrator(db, deadLetters))
            .HandleAsync(settlement, CancellationToken.None);
        await new ProductionReportRecordedIntegrationEventHandlerForAccumulateLaborCost(db, deadLetters, db, TestWorkOrderCostMutationLock.Instance)
            .HandleAsync(Report("evt-report-001", "RPT-001", AugustCompletedAtUtc.AddMinutes(-10)), CancellationToken.None);

        var state = await db.OperationLaborSettlementStates.SingleAsync();
        var cost = await db.WorkOrderCosts.Include(x => x.Details).SingleAsync();
        Assert.Null(state.ActiveRevision);
        Assert.Equal(1, state.HighestRevision);
        Assert.Equal(0m, cost.LaborCost);
        Assert.DoesNotContain(cost.Details, x => x.LaborBasis == LaborCostBasis.TheoreticalReport);
        Assert.Single(await db.OperationLaborSettlements.ToListAsync());
        Assert.Single(await db.OperationLaborSettlementVoids.ToListAsync());
    }

    [Fact]
    public async Task Theoretical_report_then_void_first_then_late_settlement_converges_to_zero()
    {
        await using var db = CreateDb();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        db.WorkCenterCostRates.Add(Rate(
            7, 80m, DateTimeOffset.Parse("2026-08-01T00:00:00Z"), SeptemberStartsAtUtc));
        await db.SaveChangesAsync();
        var settlement = Settled(
            "evt-settled-r1", 1, AugustCompletedAtUtc,
            2 * TimeSpan.TicksPerHour, ["RPT-001"]);

        await new ProductionReportRecordedIntegrationEventHandlerForAccumulateLaborCost(db, deadLetters, db, TestWorkOrderCostMutationLock.Instance)
            .HandleAsync(Report("evt-report-001", "RPT-001", AugustCompletedAtUtc.AddMinutes(-10)), CancellationToken.None);
        await new MesOperationActualTimeSettlementVoidedIntegrationEventHandlerForReverseLaborCost(db, db, TestWorkOrderCostMutationLock.Instance, new OperationLaborSettlementOrchestrator(db, deadLetters))
            .HandleAsync(Voided("evt-void-r1", settlement, SeptemberStartsAtUtc), CancellationToken.None);
        await new MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost(db, db, TestWorkOrderCostMutationLock.Instance, new OperationLaborSettlementOrchestrator(db, deadLetters))
            .HandleAsync(settlement, CancellationToken.None);

        var cost = await db.WorkOrderCosts.Include(x => x.Details).SingleAsync();
        Assert.Equal(0m, cost.LaborCost);
        Assert.Contains(cost.Details, x => x.LaborBasis == LaborCostBasis.TheoreticalReportReplacement);
        Assert.DoesNotContain(cost.Details, x => x.LaborBasis == LaborCostBasis.ActualOperation);
    }

    [Fact]
    public async Task Reopen_after_void_uses_the_new_completion_period_rate()
    {
        await using var db = CreateDb();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        db.WorkCenterCostRates.AddRange(
            Rate(7, 80m, DateTimeOffset.Parse("2026-08-01T00:00:00Z"), SeptemberStartsAtUtc),
            Rate(8, 88m, SeptemberStartsAtUtc, null));
        await db.SaveChangesAsync();
        var settledConsumer = new MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost(db, db, TestWorkOrderCostMutationLock.Instance, new OperationLaborSettlementOrchestrator(db, deadLetters));
        var voidConsumer = new MesOperationActualTimeSettlementVoidedIntegrationEventHandlerForReverseLaborCost(db, db, TestWorkOrderCostMutationLock.Instance, new OperationLaborSettlementOrchestrator(db, deadLetters));
        var revisionOne = Settled(
            "evt-settled-r1",
            1,
            AugustCompletedAtUtc,
            2 * TimeSpan.TicksPerHour,
            ["RPT-001"]);

        await settledConsumer.HandleAsync(revisionOne, CancellationToken.None);
        await voidConsumer.HandleAsync(
            Voided("evt-void-r1", revisionOne, SeptemberStartsAtUtc.AddDays(1)),
            CancellationToken.None);
        await settledConsumer.HandleAsync(
            Settled("evt-settled-r2", 2, SeptemberStartsAtUtc.AddDays(2), 90 * TimeSpan.TicksPerMinute, ["RPT-002"]),
            CancellationToken.None);

        var snapshots = await db.OperationLaborSettlements.OrderBy(x => x.SettlementRevision).ToListAsync();
        var cost = await db.WorkOrderCosts.Include(x => x.Details).SingleAsync();
        Assert.Equal([7, 8], snapshots.Select(x => x.RateRevision).ToArray());
        Assert.Equal([160m, 132m], snapshots.Select(x => x.Amount).ToArray());
        Assert.Equal(132m, cost.LaborCost);
        Assert.Equal(2, (await db.OperationLaborSettlementStates.SingleAsync()).ActiveRevision);
    }

    [Fact]
    public async Task Reopen_may_reuse_a_covered_report_without_replacing_theoretical_labor_twice()
    {
        await using var db = CreateDb();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        db.WorkCenterCostRates.Add(Rate(
            7, 80m, DateTimeOffset.Parse("2026-08-01T00:00:00Z"), null));
        await db.SaveChangesAsync();
        var settledConsumer = new MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost(db, db, TestWorkOrderCostMutationLock.Instance, new OperationLaborSettlementOrchestrator(db, deadLetters));
        var voidConsumer = new MesOperationActualTimeSettlementVoidedIntegrationEventHandlerForReverseLaborCost(db, db, TestWorkOrderCostMutationLock.Instance, new OperationLaborSettlementOrchestrator(db, deadLetters));
        var revisionOne = Settled(
            "evt-settled-r1", 1, AugustCompletedAtUtc,
            2 * TimeSpan.TicksPerHour, ["RPT-001"]);

        await new ProductionReportRecordedIntegrationEventHandlerForAccumulateLaborCost(db, deadLetters, db, TestWorkOrderCostMutationLock.Instance)
            .HandleAsync(Report("evt-report-001", "RPT-001", AugustCompletedAtUtc.AddMinutes(-10)), CancellationToken.None);
        await settledConsumer.HandleAsync(revisionOne, CancellationToken.None);
        await voidConsumer.HandleAsync(
            Voided("evt-void-r1", revisionOne, SeptemberStartsAtUtc), CancellationToken.None);
        await settledConsumer.HandleAsync(
            Settled("evt-settled-r2", 2, SeptemberStartsAtUtc.AddDays(1),
                90 * TimeSpan.TicksPerMinute, ["RPT-001"]),
            CancellationToken.None);

        Assert.Empty(await deadLetters.ListAsync(
            MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
        Assert.Equal(120m, (await db.WorkOrderCosts.Include(x => x.Details).SingleAsync()).LaborCost);
        Assert.Single((await db.WorkOrderCosts.Include(x => x.Details).SingleAsync()).Details,
            x => x.LaborBasis == LaborCostBasis.TheoreticalReportReplacement);
        Assert.Single(await db.OperationLaborCoveredReports.ToListAsync());
        Assert.Equal(2, (await db.OperationLaborSettlementStates.SingleAsync()).ActiveRevision);
    }

    [Fact]
    public async Task Settlement_after_capitalization_posts_one_balanced_delta_without_revaluing_capitalized_cost()
    {
        await using var db = CreateDb();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        db.WorkCenterCostRates.Add(Rate(
            7, 80m, DateTimeOffset.Parse("2026-08-01T00:00:00Z"), null));
        await db.SaveChangesAsync();
        await new ProductionReportRecordedIntegrationEventHandlerForAccumulateLaborCost(db, deadLetters, db, TestWorkOrderCostMutationLock.Instance)
            .HandleAsync(Report("evt-report-001", "RPT-001", AugustCompletedAtUtc.AddMinutes(-10)), CancellationToken.None);
        var cost = await db.WorkOrderCosts.Include(x => x.Details).SingleAsync();
        cost.Complete(10m, 1, 0, AugustCompletedAtUtc);
        cost.Capitalize("MOVE-FG-001", 10m, 16m, AugustCompletedAtUtc.AddMinutes(1));
        cost.RecordWipClearance(160m);
        await db.SaveChangesAsync();

        await new MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost(db, db, TestWorkOrderCostMutationLock.Instance, new OperationLaborSettlementOrchestrator(db, deadLetters))
            .HandleAsync(
                Settled("evt-settled-r1", 1, AugustCompletedAtUtc,
                    90 * TimeSpan.TicksPerMinute, ["RPT-001"]),
                CancellationToken.None);

        cost = await db.WorkOrderCosts.Include(x => x.Details).SingleAsync();
        Assert.Equal(120m, cost.LaborCost);
        Assert.Equal(160m, cost.CapitalizedCost);
        Assert.Equal(120m, cost.WipClearedCost);
        var voucher = await db.JournalVouchers.Include(x => x.Lines).SingleAsync();
        Assert.Equal(voucher.Lines.Sum(x => x.DebitAmount), voucher.Lines.Sum(x => x.CreditAmount));
        Assert.Equal(40m, voucher.Lines.Sum(x => x.DebitAmount));
    }

    [Fact]
    public async Task Completion_event_without_finished_goods_posting_does_not_post_settle_or_void_variance()
    {
        await using var db = CreateDb();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        db.WorkCenterCostRates.Add(Rate(
            7, 80m, DateTimeOffset.Parse("2026-08-01T00:00:00Z"), null));
        await db.SaveChangesAsync();
        await new ProductionReportRecordedIntegrationEventHandlerForAccumulateLaborCost(
                db, deadLetters, db, TestWorkOrderCostMutationLock.Instance)
            .HandleAsync(Report("evt-report-pre-posting", "RPT-PRE-POSTING", AugustCompletedAtUtc.AddMinutes(-10)), CancellationToken.None);
        var cost = await db.WorkOrderCosts.Include(x => x.Details).SingleAsync();
        cost.Complete(10m, 1, 0, AugustCompletedAtUtc);
        await db.SaveChangesAsync();
        Assert.True(cost.CapitalizationPublished);
        Assert.Equal(0m, cost.CapitalizedQuantity);

        var settled = Settled(
            "evt-settled-pre-posting", 1, AugustCompletedAtUtc,
            90 * TimeSpan.TicksPerMinute, ["RPT-PRE-POSTING"]);
        await new MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost(
                db, db, TestWorkOrderCostMutationLock.Instance,
                new OperationLaborSettlementOrchestrator(db, deadLetters))
            .HandleAsync(settled, CancellationToken.None);
        await new MesOperationActualTimeSettlementVoidedIntegrationEventHandlerForReverseLaborCost(
                db, db, TestWorkOrderCostMutationLock.Instance,
                new OperationLaborSettlementOrchestrator(db, deadLetters))
            .HandleAsync(Voided("evt-void-pre-posting", settled, AugustCompletedAtUtc.AddMinutes(5)), CancellationToken.None);

        Assert.Empty(await db.JournalVouchers.ToListAsync());
        Assert.Equal(0m, (await db.WorkOrderCosts.Include(x => x.Details).SingleAsync()).LaborCost);
    }

    [Fact]
    public async Task Void_after_finished_goods_posting_posts_one_balanced_delta()
    {
        await using var db = CreateDb();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        db.WorkCenterCostRates.Add(Rate(
            7, 80m, DateTimeOffset.Parse("2026-08-01T00:00:00Z"), null));
        await db.SaveChangesAsync();
        var settled = Settled(
            "evt-settled-before-capitalization", 1, AugustCompletedAtUtc,
            90 * TimeSpan.TicksPerMinute, ["RPT-CAP-VOID"]);
        await new MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost(
                db, db, TestWorkOrderCostMutationLock.Instance,
                new OperationLaborSettlementOrchestrator(db, deadLetters))
            .HandleAsync(settled, CancellationToken.None);
        var cost = await db.WorkOrderCosts.Include(x => x.Details).SingleAsync();
        cost.Complete(10m, 1, 0, AugustCompletedAtUtc);
        cost.Capitalize("MOVE-FG-CAP-VOID", 10m, 12m, AugustCompletedAtUtc.AddMinutes(1));
        cost.RecordWipClearance(120m);
        await db.SaveChangesAsync();

        await new MesOperationActualTimeSettlementVoidedIntegrationEventHandlerForReverseLaborCost(
                db, db, TestWorkOrderCostMutationLock.Instance,
                new OperationLaborSettlementOrchestrator(db, deadLetters))
            .HandleAsync(Voided("evt-void-after-posting", settled, AugustCompletedAtUtc.AddMinutes(5)), CancellationToken.None);

        cost = await db.WorkOrderCosts.Include(x => x.Details).SingleAsync();
        Assert.Equal(0m, cost.LaborCost);
        Assert.Equal(0m, cost.WipClearedCost);
        var voucher = await db.JournalVouchers.Include(x => x.Lines).SingleAsync();
        Assert.Equal(voucher.Lines.Sum(x => x.DebitAmount), voucher.Lines.Sum(x => x.CreditAmount));
        Assert.Equal(120m, voucher.Lines.Sum(x => x.DebitAmount));
    }

    [Fact]
    public async Task Late_void_for_an_older_revision_does_not_supersede_the_active_revision()
    {
        await using var db = CreateDb();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        db.WorkCenterCostRates.Add(Rate(
            7, 80m, DateTimeOffset.Parse("2026-08-01T00:00:00Z"), null));
        await db.SaveChangesAsync();
        var settledConsumer = new MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost(
            db, db, TestWorkOrderCostMutationLock.Instance,
            new OperationLaborSettlementOrchestrator(db, deadLetters));
        var voidConsumer = new MesOperationActualTimeSettlementVoidedIntegrationEventHandlerForReverseLaborCost(
            db, db, TestWorkOrderCostMutationLock.Instance,
            new OperationLaborSettlementOrchestrator(db, deadLetters));
        var revisionOne = Settled(
            "evt-settled-r1", 1, AugustCompletedAtUtc,
            2 * TimeSpan.TicksPerHour, ["RPT-001"]);
        var revisionTwo = Settled(
            "evt-settled-r2", 2, AugustCompletedAtUtc.AddHours(1),
            3 * TimeSpan.TicksPerHour, ["RPT-002"]);

        await settledConsumer.HandleAsync(revisionOne, CancellationToken.None);
        await settledConsumer.HandleAsync(revisionTwo, CancellationToken.None);
        await voidConsumer.HandleAsync(
            Voided("evt-void-late-r1", revisionOne, AugustCompletedAtUtc.AddHours(2)),
            CancellationToken.None);

        var cost = await db.WorkOrderCosts.Include(x => x.Details).SingleAsync();
        var state = await db.OperationLaborSettlementStates.SingleAsync();
        Assert.Equal(240m, cost.LaborCost);
        Assert.Equal(2, state.ActiveRevision);
        Assert.DoesNotContain(cost.Details, x => x.SourceDocumentId.Contains("r2:superseded", StringComparison.Ordinal));
        Assert.Single(await db.OperationLaborSettlementVoids.ToListAsync());
    }

    [Fact]
    public async Task First_actual_settlement_switches_the_whole_work_order_and_late_reports_stay_uncosted()
    {
        await using var db = CreateDb();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        db.WorkCenterCostRates.Add(Rate(
            7, 80m, DateTimeOffset.Parse("2026-08-01T00:00:00Z"), null));
        await db.SaveChangesAsync();
        var reportConsumer = new ProductionReportRecordedIntegrationEventHandlerForAccumulateLaborCost(
            db, deadLetters, db, TestWorkOrderCostMutationLock.Instance);

        await reportConsumer.HandleAsync(
            Report("evt-report-op1", "RPT-OP1", AugustCompletedAtUtc.AddMinutes(-20), "OP-001"),
            CancellationToken.None);
        await reportConsumer.HandleAsync(
            Report("evt-report-op2", "RPT-OP2", AugustCompletedAtUtc.AddMinutes(-10), "OP-002"),
            CancellationToken.None);
        await new MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost(
                db, db, TestWorkOrderCostMutationLock.Instance,
                new OperationLaborSettlementOrchestrator(db, deadLetters))
            .HandleAsync(
                Settled("evt-settled-op1", 1, AugustCompletedAtUtc,
                    2 * TimeSpan.TicksPerHour, ["RPT-OP1"], "OP-001"),
                CancellationToken.None);
        await reportConsumer.HandleAsync(
            Report("evt-report-op2-late", "RPT-OP2-LATE", AugustCompletedAtUtc.AddMinutes(10), "OP-002"),
            CancellationToken.None);

        var cost = await db.WorkOrderCosts.Include(x => x.Details).SingleAsync();
        Assert.Equal(160m, cost.LaborCost);
        Assert.Equal(2, cost.Details.Count(x => x.LaborBasis == LaborCostBasis.TheoreticalReportReplacement));
        Assert.Single(cost.Details, x => x.LaborBasis == LaborCostBasis.ActualOperation);
        Assert.Contains(cost.Details, x => x.SourceDocumentId == "RPT-OP2-LATE" && x.LaborBasis == LaborCostBasis.UncostedReport);
    }

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"erp-operation-labor-{Guid.CreateVersion7():N}")
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private static WorkCenterCostRate Rate(
        int revision,
        decimal hourlyRate,
        DateTimeOffset effectiveFromUtc,
        DateTimeOffset? effectiveToUtc,
        string currencyCode = "CNY")
        => WorkCenterCostRate.Define(
            "org-001", "env-prod", "WC-01", hourlyRate, currencyCode,
            effectiveFromUtc, effectiveToUtc, revision,
            "system:test", "approved standard labor rate", effectiveFromUtc);

    private static ProductionReportRecordedIntegrationEvent Report(
        string eventId,
        string reportNo,
        DateTimeOffset reportedAtUtc,
        string operationTaskId = "OP-001")
        => new(
            eventId,
            MesIntegrationEventTypes.ProductionReportRecorded,
            1,
            reportedAtUtc,
            MesIntegrationEventSources.BusinessMes,
            reportNo,
            "WO-001",
            "org-001",
            "env-prod",
            "operator:test",
            $"report:{reportNo}",
            new ProductionReportRecordedPayload(
                reportNo,
                "WO-001",
                operationTaskId,
                "WC-01",
                null,
                10m,
                0m,
                0m,
                "ea",
                5m,
                reportedAtUtc,
                false,
                MaterialMovementCount: 0));

    private static MesOperationActualTimeSettledIntegrationEvent Settled(
        string eventId,
        long revision,
        DateTimeOffset completedAtUtc,
        long actualLaborTicks,
        IReadOnlyCollection<string> coveredReports,
        string operationTaskId = "OP-001")
        => new(
            eventId,
            MesIntegrationEventTypes.OperationActualTimeSettled,
            1,
            completedAtUtc.AddMinutes(1),
            MesIntegrationEventSources.BusinessMes,
            "correlation-001",
            "causation-001",
            "org-001",
            "env-prod",
            "operator:test",
            $"actual-time:{operationTaskId}:{revision}:settled",
            new OperationActualTimeSettledPayload(
                "WO-001",
                operationTaskId,
                "WC-01",
                revision,
                completedAtUtc,
                actualLaborTicks,
                actualLaborTicks,
                coveredReports));

    private static MesOperationActualTimeSettlementVoidedIntegrationEvent Voided(
        string eventId,
        MesOperationActualTimeSettledIntegrationEvent settled,
        DateTimeOffset voidedAtUtc)
        => new(
            eventId,
            MesIntegrationEventTypes.OperationActualTimeSettlementVoided,
            1,
            voidedAtUtc,
            MesIntegrationEventSources.BusinessMes,
            settled.CorrelationId,
            settled.EventId,
            settled.OrganizationId,
            settled.EnvironmentId,
            "operator:test",
            $"actual-time:{settled.Payload.OperationTaskId}:{settled.Payload.SettlementRevision}:voided",
            new OperationActualTimeSettlementVoidedPayload(
                settled.Payload.WorkOrderId,
                settled.Payload.OperationTaskId,
                settled.Payload.WorkCenterId,
                settled.Payload.SettlementRevision,
                settled.Payload.CompletedAtUtc,
                voidedAtUtc,
                settled.Payload.ActualLaborTicks,
                settled.Payload.ActualMachineTicks,
                settled.Payload.CoveredProductionReportNos));

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
