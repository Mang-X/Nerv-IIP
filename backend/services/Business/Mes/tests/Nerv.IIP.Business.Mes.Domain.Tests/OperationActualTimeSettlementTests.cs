using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;

namespace Nerv.IIP.Business.Mes.Domain.Tests;

public sealed class OperationActualTimeSettlementTests
{
    [Fact]
    public void Completion_freezes_one_monotonic_actual_time_settlement()
    {
        var startedAtUtc = DateTimeOffset.Parse("2026-08-26T01:00:00Z");
        var task = CreateTask(startedAtUtc);
        task.Start(startedAtUtc);

        task.Complete(
            startedAtUtc.AddHours(2),
            ["PR-002", "PR-001", "PR-002"]);

        Assert.Equal(1, task.ActualTimeSettlementRevision);
        var settled = Assert.Single(task.GetDomainEvents().OfType<OperationActualTimeSettledDomainEvent>());
        Assert.Equal(1, settled.Settlement.SettlementRevision);
        Assert.Equal(TimeSpan.FromHours(2).Ticks, settled.Settlement.ActualLaborTicks);
        Assert.Equal(TimeSpan.FromHours(2).Ticks, settled.Settlement.ActualMachineTicks);
        Assert.Equal(["PR-001", "PR-002"], settled.Settlement.CoveredProductionReportNos);
    }

    [Fact]
    public void Reopen_voids_the_active_settlement_with_its_original_snapshot()
    {
        var startedAtUtc = DateTimeOffset.Parse("2026-08-26T01:00:00Z");
        var voidedAtUtc = startedAtUtc.AddHours(3);
        var task = CreateTask(startedAtUtc);
        task.Start(startedAtUtc);
        task.Complete(startedAtUtc.AddHours(2), ["PR-001", "PR-002"]);
        var settlement = Settlement(task);
        task.ClearDomainEvents();

        task.ReopenAfterReportReversal(settlement, voidedAtUtc);

        Assert.Equal(OperationTaskLifecycleStatus.InProgress, task.Status);
        Assert.Equal(1, task.ActualTimeSettlementRevision);
        Assert.Equal(voidedAtUtc, task.ExistingStartUtc);
        Assert.Equal(0, task.LaborTimeTicks);
        Assert.Equal(0, task.MachineTimeTicks);
        var voided = Assert.Single(task.GetDomainEvents().OfType<OperationActualTimeSettlementVoidedDomainEvent>());
        Assert.Equal(voidedAtUtc, voided.VoidedAtUtc);
        Assert.Equal(1, voided.Settlement.SettlementRevision);
        Assert.Equal(TimeSpan.FromHours(2).Ticks, voided.Settlement.ActualLaborTicks);
        Assert.Equal(TimeSpan.FromHours(2).Ticks, voided.Settlement.ActualMachineTicks);
        Assert.Equal(["PR-001", "PR-002"], voided.Settlement.CoveredProductionReportNos);
    }

    [Fact]
    public void Completion_after_reopen_uses_a_higher_revision()
    {
        var startedAtUtc = DateTimeOffset.Parse("2026-08-26T01:00:00Z");
        var task = CreateTask(startedAtUtc);
        task.Start(startedAtUtc);
        task.Complete(startedAtUtc.AddHours(1), ["PR-001"]);
        task.ReopenAfterReportReversal(Settlement(task), startedAtUtc.AddHours(2));
        task.ClearDomainEvents();

        task.Complete(startedAtUtc.AddHours(3), ["PR-001", "PR-REV-001", "PR-002"]);

        Assert.Equal(2, task.ActualTimeSettlementRevision);
        var settled = Assert.Single(task.GetDomainEvents().OfType<OperationActualTimeSettledDomainEvent>());
        Assert.Equal(2, settled.Settlement.SettlementRevision);
        Assert.Equal(TimeSpan.FromHours(1).Ticks, settled.Settlement.ActualLaborTicks);
        Assert.Equal(TimeSpan.FromHours(1).Ticks, settled.Settlement.ActualMachineTicks);
        Assert.Equal(["PR-001", "PR-002", "PR-REV-001"], settled.Settlement.CoveredProductionReportNos);
    }

    [Fact]
    public void Reopening_a_non_completed_task_does_not_publish_a_void()
    {
        var startedAtUtc = DateTimeOffset.Parse("2026-08-26T01:00:00Z");
        var task = CreateTask(startedAtUtc);
        task.Start(startedAtUtc);

        task.ReopenAfterReportReversal(
            new OperationActualTimeSettlementSnapshot(
                "org-001", "env-dev", "WO-001", "OP-001", "WC-001", 0,
                startedAtUtc, 0, 0, []),
            startedAtUtc.AddMinutes(30));

        Assert.Equal(0, task.ActualTimeSettlementRevision);
        Assert.Empty(task.GetDomainEvents().OfType<OperationActualTimeSettlementVoidedDomainEvent>());
    }

    [Fact]
    public void Legacy_completed_task_without_a_settlement_fails_closed_on_reversal()
    {
        var startedAtUtc = DateTimeOffset.Parse("2026-08-26T01:00:00Z");
        var task = OperationTask.Create(
            "org-001", "env-dev", "WO-001", "OP-001",
            OperationTaskLifecycleStatus.Completed, 10, "WC-001", [], startedAtUtc,
            TimeSpan.FromHours(1), startedAtUtc, startedAtUtc.AddHours(1));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            task.ReopenAfterReportReversal(
                new OperationActualTimeSettlementSnapshot(
                    "org-001", "env-dev", "WO-001", "OP-001", "WC-001", 0,
                    startedAtUtc.AddHours(1), TimeSpan.FromHours(1).Ticks,
                    TimeSpan.FromHours(1).Ticks, []),
                startedAtUtc.AddHours(2)));

        Assert.Contains("settlement", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(OperationTaskLifecycleStatus.Completed, task.Status);
        Assert.Empty(task.GetDomainEvents().OfType<OperationActualTimeSettlementVoidedDomainEvent>());
    }

    [Fact]
    public void Repeated_completion_does_not_create_another_settlement_revision()
    {
        var startedAtUtc = DateTimeOffset.Parse("2026-08-26T01:00:00Z");
        var task = CreateTask(startedAtUtc);
        task.Start(startedAtUtc);
        task.Complete(startedAtUtc.AddHours(1), ["PR-001"]);
        task.ClearDomainEvents();

        Assert.Throws<InvalidOperationException>(() =>
            task.Complete(startedAtUtc.AddHours(2), ["PR-001"]));

        Assert.Equal(1, task.ActualTimeSettlementRevision);
        Assert.Empty(task.GetDomainEvents().OfType<OperationActualTimeSettledDomainEvent>());
    }

    [Fact]
    public void Repeated_reopen_does_not_publish_another_void_or_change_the_revision()
    {
        var startedAtUtc = DateTimeOffset.Parse("2026-08-26T01:00:00Z");
        var task = CreateTask(startedAtUtc);
        task.Start(startedAtUtc);
        task.Complete(startedAtUtc.AddHours(1), ["PR-001"]);
        var settlement = Settlement(task);
        task.ReopenAfterReportReversal(settlement, startedAtUtc.AddHours(2));
        task.ClearDomainEvents();

        task.ReopenAfterReportReversal(settlement, startedAtUtc.AddHours(3));

        Assert.Equal(1, task.ActualTimeSettlementRevision);
        Assert.Empty(task.GetDomainEvents().OfType<OperationActualTimeSettlementVoidedDomainEvent>());
    }

    [Fact]
    public void Old_void_lineage_remains_older_than_the_recompleted_settlement()
    {
        var startedAtUtc = DateTimeOffset.Parse("2026-08-26T01:00:00Z");
        var task = CreateTask(startedAtUtc);
        task.Start(startedAtUtc);
        task.Complete(startedAtUtc.AddHours(1), ["PR-001"]);
        var settlement = Settlement(task);
        task.ClearDomainEvents();
        task.ReopenAfterReportReversal(settlement, startedAtUtc.AddHours(2));
        var oldVoid = Assert.Single(task.GetDomainEvents().OfType<OperationActualTimeSettlementVoidedDomainEvent>());
        task.ClearDomainEvents();

        task.Complete(startedAtUtc.AddHours(3), ["PR-001", "PR-REV-001", "PR-002"]);

        var current = Assert.Single(task.GetDomainEvents().OfType<OperationActualTimeSettledDomainEvent>());
        Assert.Equal(1, oldVoid.Settlement.SettlementRevision);
        Assert.Equal(2, current.Settlement.SettlementRevision);
        Assert.True(oldVoid.Settlement.SettlementRevision < current.Settlement.SettlementRevision);
    }

    private static OperationTask CreateTask(DateTimeOffset earliestStartUtc) =>
        OperationTask.Queue(
            "org-001",
            "env-dev",
            "WO-001",
            "OP-001",
            10,
            "WC-001",
            [],
            earliestStartUtc,
            TimeSpan.FromHours(1));

    private static OperationActualTimeSettlementSnapshot Settlement(OperationTask task) =>
        Assert.Single(task.GetDomainEvents().OfType<OperationActualTimeSettledDomainEvent>()).Settlement;
}
