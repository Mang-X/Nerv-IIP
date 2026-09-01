using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountingPeriodAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.MachineOverheadReconciliationAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkCenterMachineOverheadRateAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkOrderCostAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Finance;
using Nerv.IIP.Business.Erp.Web.Application.Queries.Finance;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Erp.Web.Tests;

public sealed class WorkCenterMachineOverheadReconciliationApplicationTests
{
    [Fact]
    public async Task Reconciliation_uses_only_active_scope_period_work_center_settlements()
    {
        await using var db = CreateDb();
        AddPeriodAndRate(db, "org-a", "env-a", "WC-01", "2026-08");
        var activeState = AddActiveSettlement(db, "org-a", "env-a", "WC-01", "2026-08", "OP-A", 1, 10);
        AddActiveSettlement(db, "org-a", "env-a", "WC-01", "2026-08", "OP-A", 2, 20, activeState);
        AddActiveSettlement(db, "org-other", "env-a", "WC-01", "2026-08", "OP-ORG", 1, 100);
        AddActiveSettlement(db, "org-a", "env-other", "WC-01", "2026-08", "OP-ENV", 1, 100);
        AddActiveSettlement(db, "org-a", "env-a", "WC-OTHER", "2026-08", "OP-WC", 1, 100);
        AddActiveSettlement(db, "org-a", "env-a", "WC-01", "2026-07", "OP-PERIOD", 1, 100);
        await db.SaveChangesAsync();

        await Reconcile(db, ActualFixed: 1_000m, ActualVariable: 500m);
        await db.SaveChangesAsync();

        var response = await new ListWorkCenterMachineOverheadReconciliationsQueryHandler(db).Handle(
            new("org-a", "env-a", "2026-08", "WC-01"), CancellationToken.None);
        var item = Assert.Single(response.Items);
        Assert.Equal(20m, item.AppliedMachineHours);
        Assert.Equal(600m, item.AppliedFixedAmount);
        Assert.Equal(200m, item.AppliedVariableAmount);
        Assert.Equal(800m, item.AppliedTotalAmount);
        Assert.Equal(400m, item.UnderOverAppliedFixedAmount);
        Assert.Equal(400m, item.UnallocatedFixedOverheadAmount);
        Assert.Equal(700m, item.UnderOverAppliedTotalAmount);
        Assert.Equal("open", response.AccountingPeriodStatus);
        Assert.Equal(MachineOverheadReadStatus.Available, response.ReconciliationStatus);
        Assert.Null(response.ReconciliationUnavailableReason);
        Assert.Equal(MachineOverheadReadStatus.Available, item.ReconciliationStatus);
        Assert.Null(item.UnavailableReason);
    }

    [Fact]
    public async Task Close_fails_closed_until_actual_pool_and_abnormal_downtime_are_resolved_then_supports_reopen()
    {
        await using var db = CreateDb();
        AddPeriodAndRate(db, "org-a", "env-a", "WC-01", "2026-08");
        await db.SaveChangesAsync();
        var close = new CloseAccountingPeriodCommandHandler(db, new PostgreSqlErpAdvisoryLockAllocator(db));
        var command = new CloseAccountingPeriodCommand(
            "org-a", "env-a", "2026-08", "user:controller", "month end complete");

        await Assert.ThrowsAsync<KnownException>(() => close.Handle(command, CancellationToken.None));

        await Reconcile(
            db, 30_000m, 10_000m,
            abnormalDowntimeTicks: 4 * TimeSpan.TicksPerHour,
            disposition: AbnormalDowntimeDisposition.Pending);
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<KnownException>(() => close.Handle(command, CancellationToken.None));

        await Reconcile(
            db, 30_000m, 10_000m,
            abnormalDowntimeTicks: 4 * TimeSpan.TicksPerHour,
            disposition: AbnormalDowntimeDisposition.PeriodExpense);
        await db.SaveChangesAsync();
        await close.Handle(command, CancellationToken.None);
        await db.SaveChangesAsync();
        Assert.Equal(AccountingPeriodStatus.Closed, (await db.AccountingPeriods.SingleAsync()).Status);
        var closedRead = await new ListWorkCenterMachineOverheadReconciliationsQueryHandler(db).Handle(
            new("org-a", "env-a", "2026-08", "WC-01"), CancellationToken.None);
        Assert.Equal("closed", closedRead.AccountingPeriodStatus);
        Assert.Equal(MachineOverheadReadStatus.Available, closedRead.ReconciliationStatus);

        await Assert.ThrowsAsync<InvalidOperationException>(() => close.Handle(command, CancellationToken.None));
        (await db.AccountingPeriods.SingleAsync()).Reopen("user:controller", "approved adjustment window");
        await db.SaveChangesAsync();
        await close.Handle(command with { Reason = "re-close after approved window" }, CancellationToken.None);
        await db.SaveChangesAsync();
        Assert.Equal(AccountingPeriodStatus.Closed, (await db.AccountingPeriods.SingleAsync()).Status);
    }

    [Fact]
    public async Task Period_read_distinguishes_unreconciled_not_applicable_and_missing_period()
    {
        await using var db = CreateDb();
        AddPeriodAndRate(db, "org-a", "env-a", "WC-01", "2026-08");
        db.WorkCenterMachineOverheadRates.Add(WorkCenterMachineOverheadRate.DefineNotApplicable(
            "org-a", "env-a", "WC-MANUAL", "2026-08", "CNY", 1,
            "system:test", "manual work center", new(2026, 7, 31, 16, 0, 0, TimeSpan.Zero)));
        await db.SaveChangesAsync();
        var handler = new ListWorkCenterMachineOverheadReconciliationsQueryHandler(db);

        var unreconciled = await handler.Handle(
            new("org-a", "env-a", "2026-08", "WC-01"), CancellationToken.None);
        Assert.Equal("open", unreconciled.AccountingPeriodStatus);
        Assert.Equal(MachineOverheadReadStatus.Unavailable, unreconciled.ReconciliationStatus);
        Assert.Equal("reconciliation_not_recorded", unreconciled.ReconciliationUnavailableReason);
        Assert.Empty(unreconciled.Items);

        var notApplicable = await handler.Handle(
            new("org-a", "env-a", "2026-08", "WC-MANUAL"), CancellationToken.None);
        Assert.Equal(MachineOverheadReadStatus.NotApplicable, notApplicable.ReconciliationStatus);
        Assert.Equal("machine_overhead_not_applicable", notApplicable.ReconciliationUnavailableReason);
        Assert.Empty(notApplicable.Items);

        var missing = await handler.Handle(
            new("org-a", "env-a", "2099-01", "WC-01"), CancellationToken.None);
        Assert.Null(missing.AccountingPeriodStatus);
        Assert.Equal(MachineOverheadReadStatus.Unavailable, missing.ReconciliationStatus);
        Assert.Equal("accounting_period_not_found", missing.ReconciliationUnavailableReason);
    }

    [Fact]
    public async Task Close_rejects_stale_rate_and_changed_active_settlement_snapshots()
    {
        await using var db = CreateDb();
        AddPeriodAndRate(db, "org-a", "env-a", "WC-01", "2026-08");
        AddActiveSettlement(db, "org-a", "env-a", "WC-01", "2026-08", "OP-A", 1, 10);
        await db.SaveChangesAsync();
        await Reconcile(db, 1_000m, 500m);
        await db.SaveChangesAsync();

        var state = await db.OperationMachineOverheadSettlementStates.SingleAsync();
        AddActiveSettlement(db, "org-a", "env-a", "WC-01", "2026-08", "OP-A", 2, 20, state);
        await db.SaveChangesAsync();
        var readHandler = new ListWorkCenterMachineOverheadReconciliationsQueryHandler(db);
        var changedSnapshotRead = await readHandler.Handle(
            new("org-a", "env-a", "2026-08", "WC-01"), CancellationToken.None);
        Assert.Equal(MachineOverheadReadStatus.Unavailable, changedSnapshotRead.ReconciliationStatus);
        Assert.Equal("active_settlement_changed", changedSnapshotRead.ReconciliationUnavailableReason);
        Assert.Equal("active_settlement_changed", Assert.Single(changedSnapshotRead.Items).UnavailableReason);
        var close = new CloseAccountingPeriodCommandHandler(db, new PostgreSqlErpAdvisoryLockAllocator(db));
        await Assert.ThrowsAsync<KnownException>(() => close.Handle(
            new("org-a", "env-a", "2026-08", "user:controller", "close"), CancellationToken.None));

        await Reconcile(db, 1_000m, 500m);
        db.WorkCenterMachineOverheadRates.Add(WorkCenterMachineOverheadRate.DefineApplicable(
            "org-a", "env-a", "WC-01", "2026-08", 31_000m, 10_000m, 1_000m,
            "CNY", 2, "user:controller", "approved rate correction",
            new DateTimeOffset(2026, 8, 31, 13, 0, 0, TimeSpan.Zero)));
        await db.SaveChangesAsync();
        var changedRateRead = await readHandler.Handle(
            new("org-a", "env-a", "2026-08", "WC-01"), CancellationToken.None);
        Assert.Equal(MachineOverheadReadStatus.Unavailable, changedRateRead.ReconciliationStatus);
        Assert.Equal("machine_overhead_rate_changed", changedRateRead.ReconciliationUnavailableReason);
        await Assert.ThrowsAsync<KnownException>(() => close.Handle(
            new("org-a", "env-a", "2026-08", "user:controller", "close"), CancellationToken.None));
    }

    [Fact]
    public async Task Close_still_requires_reconciliation_when_latest_rate_becomes_not_applicable_after_active_settlement()
    {
        await using var db = CreateDb();
        AddPeriodAndRate(db, "org-a", "env-a", "WC-01", "2026-08");
        AddActiveSettlement(db, "org-a", "env-a", "WC-01", "2026-08", "OP-A", 1, 10);
        db.WorkCenterMachineOverheadRates.Add(WorkCenterMachineOverheadRate.DefineNotApplicable(
            "org-a", "env-a", "WC-01", "2026-08", "CNY", 2,
            "system:test", "future allocation disabled after active settlement", new(2026, 8, 31, 15, 0, 0, TimeSpan.Zero)));
        await db.SaveChangesAsync();

        var close = new CloseAccountingPeriodCommandHandler(db, new PostgreSqlErpAdvisoryLockAllocator(db));
        var exception = await Assert.ThrowsAsync<KnownException>(() => close.Handle(
            new("org-a", "env-a", "2026-08", "user:controller", "period close"),
            CancellationToken.None));

        Assert.Contains("缺少机器制造费用实际池归集核对", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reconciliation_rejects_missing_rate_and_currency_mismatch()
    {
        await using var missingRateDb = CreateDb();
        missingRateDb.AccountingPeriods.Add(Period("org-a", "env-a", "2026-08"));
        await missingRateDb.SaveChangesAsync();
        await Assert.ThrowsAsync<KnownException>(() => Reconcile(missingRateDb, 1m, 1m));

        await using var currencyDb = CreateDb();
        AddPeriodAndRate(currencyDb, "org-a", "env-a", "WC-01", "2026-08");
        await currencyDb.SaveChangesAsync();
        await Assert.ThrowsAsync<KnownException>(() => Reconcile(currencyDb, 1m, 1m, currencyCode: "USD"));
    }

    [Fact]
    public async Task Reconciliation_rejects_active_settlement_currency_mismatch()
    {
        await using var db = CreateDb();
        AddPeriodAndRate(db, "org-a", "env-a", "WC-01", "2026-08");
        AddActiveSettlement(db, "org-a", "env-a", "WC-01", "2026-08", "OP-USD", 1, 1, currencyCode: "USD");
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<KnownException>(() => Reconcile(db, 1m, 1m));
    }

    [Fact]
    public async Task Reconciliation_freezes_actual_pool_to_six_decimal_to_even_before_persistence()
    {
        await using var db = CreateDb();
        AddPeriodAndRate(db, "org-a", "env-a", "WC-01", "2026-08");
        await db.SaveChangesAsync();

        await Reconcile(db, 1.0000005m, 2.0000015m);
        await db.SaveChangesAsync();

        var reconciliation = await db.WorkCenterMachineOverheadReconciliations.SingleAsync();
        Assert.Equal(1.000000m, reconciliation.ActualFixedOverheadAmount);
        Assert.Equal(2.000002m, reconciliation.ActualVariableOverheadAmount);
        Assert.Equal(3.000002m, reconciliation.ActualTotalOverheadAmount);
        Assert.Equal(3.000002m, reconciliation.UnderOverAppliedTotalAmount);
    }

    [Fact]
    public async Task Period_read_rejects_reconciliation_currency_that_differs_from_latest_rate()
    {
        await using var db = CreateDb();
        AddPeriodAndRate(db, "org-a", "env-a", "WC-01", "2026-08");
        await db.SaveChangesAsync();
        var rate = await db.WorkCenterMachineOverheadRates.SingleAsync();
        db.WorkCenterMachineOverheadReconciliations.Add(WorkCenterMachineOverheadReconciliation.Record(
            "org-a", "env-a", "WC-01", "2026-08", rate.Id, rate.Revision, "USD",
            0m, 0m, 0, 0m, 0m, 0m, 0, AbnormalDowntimeDisposition.None, 1,
            "system:test", "ledger:test", "currency mutation", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        var response = await new ListWorkCenterMachineOverheadReconciliationsQueryHandler(db).Handle(
            new("org-a", "env-a", "2026-08", "WC-01"), CancellationToken.None);

        Assert.Equal(MachineOverheadReadStatus.Unavailable, response.ReconciliationStatus);
        Assert.Equal("currency_conflict", response.ReconciliationUnavailableReason);
        Assert.Equal("currency_conflict", Assert.Single(response.Items).UnavailableReason);
    }

    [Fact]
    public async Task Reconciliation_history_has_bounded_stable_pagination()
    {
        await using var db = CreateDb();
        AddPeriodAndRate(db, "org-a", "env-a", "WC-01", "2026-08");
        await db.SaveChangesAsync();
        await Reconcile(db, 1_000m, 100m);
        await Reconcile(db, 2_000m, 200m);
        await Reconcile(db, 3_000m, 300m);
        await db.SaveChangesAsync();

        var response = await new ListWorkCenterMachineOverheadReconciliationsQueryHandler(db).Handle(
            new("org-a", "env-a", "2026-08", "WC-01", PageNumber: 2, PageSize: 1),
            CancellationToken.None);

        Assert.Equal(3, response.TotalCount);
        Assert.Equal(2, response.PageNumber);
        Assert.Equal(1, response.PageSize);
        var historicalItem = Assert.Single(response.Items);
        Assert.Equal(2, historicalItem.Revision);
        Assert.Equal(MachineOverheadReadStatus.Unavailable, historicalItem.ReconciliationStatus);
        Assert.Equal("superseded_reconciliation", historicalItem.UnavailableReason);
    }

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"erp-machine-reconciliation-{Guid.CreateVersion7():N}")
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private static void AddPeriodAndRate(
        ApplicationDbContext db,
        string organizationId,
        string environmentId,
        string workCenterId,
        string periodCode)
    {
        db.AccountingPeriods.Add(Period(organizationId, environmentId, periodCode));
        db.WorkCenterMachineOverheadRates.Add(WorkCenterMachineOverheadRate.DefineApplicable(
            organizationId, environmentId, workCenterId, periodCode,
            30_000m, 10_000m, 1_000m, "CNY", 1,
            "system:test", "approved predetermined rate",
            new DateTimeOffset(2026, 7, 31, 16, 0, 0, TimeSpan.Zero)));
    }

    private static AccountingPeriod Period(string organizationId, string environmentId, string periodCode)
        => AccountingPeriod.Open(
            organizationId, environmentId, periodCode,
            periodCode == "2026-07" ? new(2026, 7, 1) : new(2026, 8, 1),
            periodCode == "2026-07" ? new(2026, 7, 31) : new(2026, 8, 31));

    private static OperationMachineOverheadSettlementState AddActiveSettlement(
        ApplicationDbContext db,
        string organizationId,
        string environmentId,
        string workCenterId,
        string periodCode,
        string operationTaskId,
        long revision,
        long machineHours,
        OperationMachineOverheadSettlementState? existingState = null,
        string currencyCode = "CNY")
    {
        var rate = db.WorkCenterMachineOverheadRates.Local.FirstOrDefault(x =>
            x.OrganizationId == organizationId && x.EnvironmentId == environmentId
            && x.WorkCenterId == workCenterId && x.AccountingPeriodCode == periodCode)
            ?? WorkCenterMachineOverheadRate.DefineApplicable(
                organizationId, environmentId, workCenterId, periodCode,
                30_000m, 10_000m, 1_000m, "CNY", 1,
                "system:test", "distractor rate", new(2026, 7, 31, 16, 0, 0, TimeSpan.Zero));
        if (db.Entry(rate).State == EntityState.Detached) db.WorkCenterMachineOverheadRates.Add(rate);
        var completedAt = periodCode == "2026-07"
            ? new DateTimeOffset(2026, 7, 31, 12, 0, 0, TimeSpan.Zero)
            : new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
        db.OperationMachineOverheadSettlements.Add(OperationMachineOverheadSettlement.CreateApplied(
            organizationId, environmentId, $"WO-{operationTaskId}", operationTaskId, workCenterId,
            revision, completedAt, $"DEVICE-{operationTaskId}", machineHours * TimeSpan.TicksPerHour,
            "single-device-active-minus-explicit-pause-v1", rate.Id, periodCode, rate.Revision,
            currencyCode, 30m, 10m, $"evt-{operationTaskId}-{revision}", new string('a', 64)));
        var state = existingState ?? OperationMachineOverheadSettlementState.Open(
            organizationId, environmentId, operationTaskId);
        state.ApplySettlement(revision);
        if (existingState is null) db.OperationMachineOverheadSettlementStates.Add(state);
        return state;
    }

    private static async Task Reconcile(
        ApplicationDbContext db,
        decimal ActualFixed,
        decimal ActualVariable,
        long abnormalDowntimeTicks = 0,
        AbnormalDowntimeDisposition disposition = AbnormalDowntimeDisposition.None,
        string currencyCode = "CNY")
    {
        var handler = new ReconcileWorkCenterMachineOverheadCommandHandler(
            db, new PostgreSqlErpAdvisoryLockAllocator(db));
        await handler.Handle(new(
            "org-a", "env-a", "WC-01", "2026-08",
            ActualFixed, ActualVariable, currencyCode,
            abnormalDowntimeTicks, disposition,
            "user:accountant", "ledger:2026-08", "month-end reconciliation",
            new DateTimeOffset(2026, 8, 31, 16, 0, 0, TimeSpan.Zero)), CancellationToken.None);
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
