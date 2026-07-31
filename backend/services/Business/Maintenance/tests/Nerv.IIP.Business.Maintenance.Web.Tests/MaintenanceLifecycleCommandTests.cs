using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.DowntimeReasonAggregate;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;
using Nerv.IIP.Business.Maintenance.Web.Application.Commands;
using Nerv.IIP.Business.Maintenance.Web.Application.Errors;

namespace Nerv.IIP.Business.Maintenance.Web.Tests;

public sealed class MaintenanceLifecycleCommandTests
{
    [Fact]
    public async Task Assignment_and_lifecycle_actions_persist_actor_technician_reason_and_authoritative_version()
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();
        var workOrder = MaintenanceWorkOrder.OpenManual("org-001", "env-dev", "DEV-001", "high", "reporter-001");
        db.MaintenanceWorkOrders.Add(workOrder);
        db.DowntimeReasons.Add(DowntimeReason.Create("org-001", "env-dev", "failure", "Failure", "breakdown", "equipment"));
        await db.SaveChangesAsync();

        var assign = await new AssignMaintenanceWorkOrderCommandHandler(db).Handle(
            new AssignMaintenanceWorkOrderCommand(
                "org-001", "env-dev", workOrder.Id, "dispatcher-001", "tech-001", "team-001",
                "on-duty", "assign-001", 0),
            CancellationToken.None);
        await db.SaveChangesAsync();

        var accept = await new TransitionMaintenanceWorkOrderCommandHandler(db).Handle(
            Action(workOrder.Id, MaintenanceWorkOrderAction.Accept, "tech-001", "accepted", "accept-001", assign.Version),
            CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Equal(MaintenanceWorkOrderStatus.Accepted, accept.Status);
        Assert.Equal(2, accept.Version);
        Assert.Collection(
            await db.MaintenanceWorkOrderLifecycleEvents.OrderBy(x => x.OccurredAtUtc).ToArrayAsync(),
            assignment =>
            {
                Assert.Equal(MaintenanceWorkOrderAction.Assign, assignment.Action);
                Assert.Equal("dispatcher-001", assignment.ActorPrincipalId);
                Assert.Equal("tech-001", assignment.TechnicianUserId);
                Assert.Equal("team-001", assignment.TeamId);
                Assert.Equal("on-duty", assignment.Reason);
                Assert.Equal(1, assignment.ResultingVersion);
            },
            accepted =>
            {
                Assert.Equal(MaintenanceWorkOrderAction.Accept, accepted.Action);
                Assert.Equal("tech-001", accepted.ActorPrincipalId);
                Assert.Equal("tech-001", accepted.TechnicianUserId);
                Assert.Equal("accepted", accepted.Reason);
                Assert.Equal(2, accepted.ResultingVersion);
            });
    }

    [Fact]
    public async Task Lifecycle_action_replays_same_payload_and_rejects_stale_version_or_changed_payload()
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();
        var workOrder = MaintenanceWorkOrder.OpenManual(
            "org-001", "env-dev", "DEV-001", "high", "reporter-001", assignedTechnicianUserId: "tech-001");
        db.MaintenanceWorkOrders.Add(workOrder);
        await db.SaveChangesAsync();
        var handler = new TransitionMaintenanceWorkOrderCommandHandler(db);
        var command = Action(workOrder.Id, MaintenanceWorkOrderAction.Accept, "tech-001", "accepted", "accept-001", 0);

        var first = await handler.Handle(command, CancellationToken.None);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var replay = await new TransitionMaintenanceWorkOrderCommandHandler(db).Handle(command, CancellationToken.None);

        Assert.Equal(first, replay);
        await Assert.ThrowsAsync<MaintenanceIdempotencyConflictException>(() =>
            new TransitionMaintenanceWorkOrderCommandHandler(db).Handle(
                command with { Reason = "different" },
                CancellationToken.None));
        await Assert.ThrowsAsync<MaintenanceLifecycleConflictException>(() =>
            new TransitionMaintenanceWorkOrderCommandHandler(db).Handle(
                Action(workOrder.Id, MaintenanceWorkOrderAction.Start, "tech-001", "start", "start-001", 0),
                CancellationToken.None));
    }

    [Fact]
    public async Task Alarm_work_order_walks_through_pause_waiting_completion_verification_and_close_with_audit_history()
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();
        var workOrder = MaintenanceWorkOrder.OpenFromAlarm(
            "org-001", "env-dev", "DEV-001", "alarm-001", "critical", assignedTechnicianUserId: "tech-001");
        workOrder.MarkAssetUnavailable(DateTimeOffset.UtcNow, "alarm-raised");
        db.MaintenanceWorkOrders.Add(workOrder);
        db.DowntimeReasons.Add(DowntimeReason.Create("org-001", "env-dev", "failure", "Failure", "breakdown", "equipment"));
        await db.SaveChangesAsync();

        var handler = new TransitionMaintenanceWorkOrderCommandHandler(db);
        var version = 0;
        foreach (var (action, key) in new[]
                 {
                     (MaintenanceWorkOrderAction.Accept, "accept"),
                     (MaintenanceWorkOrderAction.Start, "start"),
                     (MaintenanceWorkOrderAction.Pause, "pause"),
                     (MaintenanceWorkOrderAction.Resume, "resume-after-pause"),
                     (MaintenanceWorkOrderAction.WaitForParts, "wait-for-parts"),
                     (MaintenanceWorkOrderAction.Resume, "resume-after-parts"),
                 })
        {
            var result = await handler.Handle(
                Action(workOrder.Id, action, "tech-001", key, $"lifecycle-{key}", version),
                CancellationToken.None);
            version = result.Version;
            await db.SaveChangesAsync();
        }

        var completed = await handler.Handle(
            Action(workOrder.Id, MaintenanceWorkOrderAction.Complete, "tech-001", "repair-complete", "lifecycle-complete", version) with
            {
                Result = "restored",
                DowntimeReasonCode = "failure",
                DowntimeMinutes = 30,
                ActualLaborMinutes = 45,
                SparePartCostAmount = 25m,
                ExternalServiceCostAmount = 10m,
                CostCurrencyCode = "CNY",
                SpareParts = [new MaintenanceSparePartInput("SPARE-001", 1m, "EA")],
            },
            CancellationToken.None);
        await db.SaveChangesAsync();
        var verified = await handler.Handle(
            Action(workOrder.Id, MaintenanceWorkOrderAction.Verify, "supervisor-001", "verified", "lifecycle-verify", completed.Version),
            CancellationToken.None);
        await db.SaveChangesAsync();
        var closed = await handler.Handle(
            Action(workOrder.Id, MaintenanceWorkOrderAction.Close, "supervisor-001", "closed", "lifecycle-close", verified.Version),
            CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Equal(MaintenanceWorkOrderStatus.Closed, closed.Status);
        Assert.Equal(9, closed.Version);
        Assert.Equal(9, await db.MaintenanceWorkOrderLifecycleEvents.CountAsync());
        var persisted = await db.MaintenanceWorkOrders.SingleAsync(x => x.Id == workOrder.Id);
        Assert.Equal(45, persisted.ActualLaborMinutes);
        Assert.Equal(25m, persisted.SparePartCostAmount);
        Assert.Equal(10m, persisted.ExternalServiceCostAmount);
        Assert.NotNull(persisted.ClosedAtUtc);
    }

    private static TransitionMaintenanceWorkOrderCommand Action(
        MaintenanceWorkOrderId workOrderId,
        MaintenanceWorkOrderAction action,
        string actor,
        string reason,
        string key,
        int version) =>
        new("org-001", "env-dev", workOrderId, action, actor, reason, key, version);
}
