using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountingPeriodAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkCenterMachineOverheadRateAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkOrderCostAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Erp.Web.Tests;

public sealed class OperationMachineOverheadSettlementHandlerTests
{
    private static readonly DateTimeOffset AugustCompletedAtUtc =
        DateTimeOffset.Parse("2026-08-31T15:50:00Z");

    [Fact]
    public async Task Authoritative_settlement_freezes_completion_period_rate_and_records_machine_overhead()
    {
        await using var db = CreateDb();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        AddPeriodAndRate(db, "2026-08", new(2026, 8, 1), new(2026, 8, 31), 7, 30m, 10m);
        await db.SaveChangesAsync();

        await Consumer(db, deadLetters).HandleAsync(
            Settled("evt-machine-r1", 1, AugustCompletedAtUtc, 2 * TimeSpan.TicksPerHour),
            CancellationToken.None);

        var settlement = await db.OperationMachineOverheadSettlements.SingleAsync();
        var cost = await db.WorkOrderCosts.Include(x => x.Details).SingleAsync();
        Assert.Equal("2026-08", settlement.AccountingPeriodCode);
        Assert.Equal(7, settlement.RateRevision);
        Assert.Equal(60m, settlement.FixedAmount);
        Assert.Equal(20m, settlement.VariableAmount);
        Assert.Equal(80m, settlement.Amount);
        Assert.Equal(80m, cost.MachineOverheadCost);
        Assert.Equal(0m, cost.LaborCost);
        Assert.Single(cost.Details, x => x.Type == WorkOrderCostDetailType.MachineOverhead);
    }

    [Fact]
    public async Task Duplicate_void_and_reopen_use_frozen_negative_then_select_the_new_period_rate()
    {
        await using var db = CreateDb();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        AddPeriodAndRate(db, "2026-08", new(2026, 8, 1), new(2026, 8, 31), 7, 30m, 10m);
        AddPeriodAndRate(db, "2026-09", new(2026, 9, 1), new(2026, 9, 30), 8, 40m, 10m);
        await db.SaveChangesAsync();
        var settledConsumer = Consumer(db, deadLetters);
        var voidConsumer = VoidConsumer(db, deadLetters);
        var revisionOne = Settled("evt-machine-r1", 1, AugustCompletedAtUtc, 2 * TimeSpan.TicksPerHour);

        await settledConsumer.HandleAsync(revisionOne, CancellationToken.None);
        await settledConsumer.HandleAsync(revisionOne, CancellationToken.None);
        await voidConsumer.HandleAsync(
            Voided("evt-machine-r1-void", revisionOne, DateTimeOffset.Parse("2026-09-01T00:10:00Z")),
            CancellationToken.None);
        var revisionTwo = Settled(
            "evt-machine-r2", 2, DateTimeOffset.Parse("2026-09-01T01:00:00Z"),
            2 * TimeSpan.TicksPerHour);
        await settledConsumer.HandleAsync(revisionTwo, CancellationToken.None);

        var snapshots = await db.OperationMachineOverheadSettlements.OrderBy(x => x.SettlementRevision).ToArrayAsync();
        var reversal = await db.OperationMachineOverheadSettlementVoids.SingleAsync();
        var cost = await db.WorkOrderCosts.Include(x => x.Details).SingleAsync();
        Assert.Equal(2, snapshots.Length);
        Assert.Equal("2026-08", snapshots[0].AccountingPeriodCode);
        Assert.Equal("2026-09", snapshots[1].AccountingPeriodCode);
        Assert.Equal(-snapshots[0].FixedAmount, reversal.FixedAmount);
        Assert.Equal(-snapshots[0].VariableAmount, reversal.VariableAmount);
        Assert.Equal(-snapshots[0].Amount, reversal.Amount);
        Assert.Equal(100m, cost.MachineOverheadCost);
        Assert.Equal(2, (await db.OperationMachineOverheadSettlementStates.SingleAsync()).ActiveRevision);
        Assert.Single(cost.Details, x => x.MachineOverheadBasis == MachineOverheadCostBasis.ActualOperation
            && x.SourceDocumentId.Contains("r2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Out_of_order_old_revision_is_frozen_for_audit_but_cannot_replace_the_active_cost()
    {
        await using var db = CreateDb();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        AddPeriodAndRate(db, "2026-08", new(2026, 8, 1), new(2026, 8, 31), 7, 30m, 10m);
        await db.SaveChangesAsync();
        var consumer = Consumer(db, deadLetters);

        await consumer.HandleAsync(
            Settled("evt-machine-r2", 2, AugustCompletedAtUtc.AddHours(1), 3 * TimeSpan.TicksPerHour),
            CancellationToken.None);
        await consumer.HandleAsync(
            Settled("evt-machine-r1", 1, AugustCompletedAtUtc, 2 * TimeSpan.TicksPerHour),
            CancellationToken.None);

        Assert.Equal(2, await db.OperationMachineOverheadSettlements.CountAsync());
        Assert.Equal(120m, (await db.WorkOrderCosts.Include(x => x.Details).SingleAsync()).MachineOverheadCost);
        Assert.Equal(2, (await db.OperationMachineOverheadSettlementStates.SingleAsync()).ActiveRevision);
    }

    [Fact]
    public async Task Explicit_not_applicable_is_zero_but_unavailable_or_applicability_conflict_fails_closed()
    {
        await using var db = CreateDb();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        db.AccountingPeriods.Add(AccountingPeriod.Open(
            "org-001", "env-prod", "2026-08", new(2026, 8, 1), new(2026, 8, 31)));
        db.WorkCenterMachineOverheadRates.Add(WorkCenterMachineOverheadRate.DefineNotApplicable(
            "org-001", "env-prod", "WC-01", "2026-08", "CNY", 1,
            "system:test", "explicitly no machine overhead", AugustCompletedAtUtc.AddDays(-30)));
        await db.SaveChangesAsync();
        var consumer = Consumer(db, deadLetters);

        await consumer.HandleAsync(
            Settled("evt-unavailable", 1, AugustCompletedAtUtc, 0, MesMachineTimeFactStatus.Unavailable),
            CancellationToken.None);
        Assert.Empty(await db.OperationMachineOverheadSettlements.ToListAsync());
        Assert.Empty(await db.ProcessedIntegrationEvents.ToListAsync());
        Assert.Equal("unavailable-machine-time-fact", Assert.Single(await deadLetters.ListAsync(
            MesOperationActualTimeSettledV2IntegrationEventHandlerForAccumulateMachineOverhead.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None)).FailureCode);

        await consumer.HandleAsync(
            Settled("evt-applicability-conflict", 1, AugustCompletedAtUtc, 0),
            CancellationToken.None);
        Assert.Empty(await db.OperationMachineOverheadSettlements.ToListAsync());

        await consumer.HandleAsync(
            Settled("evt-not-applicable", 1, AugustCompletedAtUtc, 0, MesMachineTimeFactStatus.NotApplicable),
            CancellationToken.None);
        var snapshot = await db.OperationMachineOverheadSettlements.SingleAsync();
        Assert.Equal(MachineOverheadApplicability.NotApplicable, snapshot.Applicability);
        Assert.Null(snapshot.ActualMachineTicks);
        Assert.Equal(0m, snapshot.Amount);
        Assert.Equal(0m, (await db.WorkOrderCosts.Include(x => x.Details).SingleAsync()).MachineOverheadCost);
    }

    [Fact]
    public async Task Missing_rate_remains_replayable_and_authoritative_zero_is_a_real_zero_snapshot()
    {
        await using var db = CreateDb();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var consumer = Consumer(db, deadLetters);
        var settlement = Settled("evt-zero", 1, AugustCompletedAtUtc, 0);

        await consumer.HandleAsync(settlement, CancellationToken.None);
        Assert.Empty(await db.ProcessedIntegrationEvents.ToListAsync());
        Assert.Empty(await db.OperationMachineOverheadSettlements.ToListAsync());

        AddPeriodAndRate(db, "2026-08", new(2026, 8, 1), new(2026, 8, 31), 7, 30m, 10m);
        await db.SaveChangesAsync();
        await consumer.HandleAsync(settlement, CancellationToken.None);

        var snapshot = await db.OperationMachineOverheadSettlements.SingleAsync();
        Assert.Equal(0, snapshot.ActualMachineTicks);
        Assert.Equal(0m, snapshot.Amount);
        Assert.Single(await db.ProcessedIntegrationEvents.ToListAsync());
    }

    [Fact]
    public async Task Currency_conflict_does_not_freeze_a_machine_snapshot_or_inbox_success()
    {
        await using var db = CreateDb();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        AddPeriodAndRate(db, "2026-08", new(2026, 8, 1), new(2026, 8, 31), 7, 30m, 10m);
        var cost = WorkOrderCost.Open("org-001", "env-prod", "WO-001", "SKU-001");
        cost.RecordLabor("RPT-USD", "WC-01", 1m, 80m, "USD", false, AugustCompletedAtUtc);
        db.WorkOrderCosts.Add(cost);
        await db.SaveChangesAsync();

        await Consumer(db, deadLetters).HandleAsync(
            Settled("evt-currency-conflict", 1, AugustCompletedAtUtc, TimeSpan.TicksPerHour),
            CancellationToken.None);

        Assert.Empty(await db.OperationMachineOverheadSettlements.ToListAsync());
        Assert.Empty(await db.ProcessedIntegrationEvents.Where(x => x.EventId == "evt-currency-conflict").ToListAsync());
        Assert.Equal("incompatible-work-order-machine-overhead-currency", Assert.Single(await deadLetters.ListAsync(
            MesOperationActualTimeSettledV2IntegrationEventHandlerForAccumulateMachineOverhead.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None)).FailureCode);
    }

    private static MesOperationActualTimeSettledV2IntegrationEventHandlerForAccumulateMachineOverhead Consumer(
        ApplicationDbContext db,
        InMemoryIntegrationEventDeadLetterStore deadLetters)
        => new(db, db, TestWorkOrderCostMutationLock.Instance,
            new OperationMachineOverheadSettlementOrchestrator(db, deadLetters));

    private static MesOperationActualTimeSettlementVoidedV2IntegrationEventHandlerForReverseMachineOverhead VoidConsumer(
        ApplicationDbContext db,
        InMemoryIntegrationEventDeadLetterStore deadLetters)
        => new(db, db, TestWorkOrderCostMutationLock.Instance,
            new OperationMachineOverheadSettlementOrchestrator(db, deadLetters));

    private static void AddPeriodAndRate(
        ApplicationDbContext db,
        string periodCode,
        DateOnly start,
        DateOnly end,
        int revision,
        decimal fixedRate,
        decimal variableRate)
    {
        db.AccountingPeriods.Add(AccountingPeriod.Open("org-001", "env-prod", periodCode, start, end));
        db.WorkCenterMachineOverheadRates.Add(WorkCenterMachineOverheadRate.DefineApplicable(
            "org-001", "env-prod", "WC-01", periodCode,
            fixedRate * 1_000m, variableRate * 1_000m, 1_000m,
            "CNY", revision, "system:test", "approved machine overhead rate", start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)));
    }

    private static MesOperationActualTimeSettledV2IntegrationEvent Settled(
        string eventId,
        long revision,
        DateTimeOffset completedAtUtc,
        long billableMachineTicks,
        MesMachineTimeFactStatus status = MesMachineTimeFactStatus.Available)
        => new(
            eventId,
            MesIntegrationEventTypes.OperationActualTimeSettled,
            MesIntegrationEventVersions.V2,
            completedAtUtc.AddMinutes(1),
            MesIntegrationEventSources.BusinessMes,
            "correlation-001",
            "causation-001",
            "org-001",
            "env-prod",
            "operator:test",
            $"actual-time:OP-001:{revision}:settled:v2",
            new OperationActualTimeSettledV2Payload(
                "WO-001", "OP-001", "WC-01", revision, completedAtUtc,
                TimeSpan.TicksPerHour, billableMachineTicks, [],
                status == MesMachineTimeFactStatus.Available ? "DEVICE-001" : null,
                status,
                status == MesMachineTimeFactStatus.Available ? billableMachineTicks : null,
                status == MesMachineTimeFactStatus.Available
                    ? MesMachineTimeBasisCodes.SingleDeviceActiveMinusExplicitPauseV1
                    : null));

    private static MesOperationActualTimeSettlementVoidedV2IntegrationEvent Voided(
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

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"erp-operation-machine-overhead-{Guid.CreateVersion7():N}")
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

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
