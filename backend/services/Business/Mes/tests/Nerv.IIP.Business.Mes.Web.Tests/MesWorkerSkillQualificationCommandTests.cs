using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Approvals;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Errors;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MesWorkerSkillQualificationCommandTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-26T08:00:00Z");

    [Fact]
    public async Task Dispatch_rejects_unqualified_worker_without_mutating_assignment()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        SeedSingleTask(dbContext, assignedUserId: null, requiredSkillCode: "cnc-operation");
        await dbContext.SaveChangesAsync();
        var handler = new AssignDispatchTaskCommandHandler(dbContext, RejectingGate.Instance);

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new AssignDispatchTaskCommand(
                "org-001", "env-dev", "OP-10", "worker-001", null, null, Now,
                AssignedUserName: "操作员甲"),
            CancellationToken.None));

        Assert.Contains("技能已过期", exception.Message, StringComparison.Ordinal);
        var task = await dbContext.OperationTasks.SingleAsync();
        Assert.Null(task.AssignedUserId);
        Assert.Null(task.AssignedAtUtc);
    }

    [Fact]
    public async Task Qualified_worker_can_be_dispatched_with_exact_frozen_scope_and_skill()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        SeedSingleTask(dbContext, assignedUserId: null, requiredSkillCode: "cnc-operation");
        await dbContext.SaveChangesAsync();
        var handler = new AssignDispatchTaskCommandHandler(dbContext, ExactQualificationGate.Instance);

        await handler.Handle(
            new AssignDispatchTaskCommand(
                "org-001", "env-dev", "OP-10", "worker-001", null, null, Now,
                AssignedUserName: "操作员甲"),
            CancellationToken.None);

        var task = await dbContext.OperationTasks.SingleAsync();
        Assert.Equal("worker-001", task.AssignedUserId);
        Assert.Equal("操作员甲", task.AssignedUserName);
    }

    // Break caught: self-claim must traverse the same frozen-skill gate and persist claimant/time.
    [Fact]
    public async Task Qualified_worker_can_self_claim_an_unassigned_task()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        SeedSingleTask(dbContext, assignedUserId: null, requiredSkillCode: "cnc-operation");
        await dbContext.SaveChangesAsync();
        var handler = new ClaimDispatchTaskCommandHandler(dbContext, ExactQualificationGate.Instance);

        var response = await handler.Handle(
            new ClaimDispatchTaskCommand(
                "org-001",
                "env-dev",
                "OP-10",
                "worker-001",
                "操作员甲",
                null,
                "SHIFT-A",
                Now,
                "user:worker-001",
                "claim-qualified-001",
                "TEAM-A",
                "甲班"),
            CancellationToken.None);

        Assert.Equal("OP-10", response.ReferenceId);
        var task = await dbContext.OperationTasks.SingleAsync();
        Assert.Equal("worker-001", task.AssignedUserId);
        Assert.Equal("操作员甲", task.AssignedUserName);
        Assert.Equal("SHIFT-A", task.ShiftId);
        Assert.Equal("TEAM-A", task.TeamId);
        Assert.Equal(Now, task.AssignedAtUtc);
    }

    [Fact]
    public async Task Self_claim_rejects_unqualified_worker_without_mutating_assignment_or_receipt()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        SeedSingleTask(dbContext, assignedUserId: null, requiredSkillCode: "cnc-operation");
        await dbContext.SaveChangesAsync();
        var handler = new ClaimDispatchTaskCommandHandler(dbContext, RejectingGate.Instance);

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new ClaimDispatchTaskCommand(
                "org-001", "env-dev", "OP-10", "worker-001", "操作员甲", null,
                "SHIFT-A", Now, "user:worker-001", "claim-unqualified-001"),
            CancellationToken.None));

        Assert.Contains("技能已过期", exception.Message, StringComparison.Ordinal);
        Assert.Null((await dbContext.OperationTasks.SingleAsync()).AssignedUserId);
        Assert.Empty(await dbContext.CodeIdempotencyKeys.ToArrayAsync());
    }

    [Fact]
    public async Task Same_claim_key_replays_across_resolved_context_changes_and_rejects_a_different_intent()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        SeedSingleTask(dbContext, assignedUserId: null, requiredSkillCode: "cnc-operation");
        await dbContext.SaveChangesAsync();
        var handler = new ClaimDispatchTaskCommandHandler(dbContext, ExactQualificationGate.Instance);
        var first = new ClaimDispatchTaskCommand(
            "org-001", "env-dev", "OP-10", "worker-001", "操作员甲", null,
            "SHIFT-A", Now, "user:worker-001", "claim-replay-001");

        var accepted = await handler.Handle(first, CancellationToken.None);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        var replay = await handler.Handle(first with
        {
            AssignedUserName = "操作员甲（新姓名）",
            DeviceAssetId = "device-B",
            ShiftId = "SHIFT-B",
            AssignedAtUtc = Now.AddMinutes(1),
            Actor = "user:worker-001-refreshed",
            TeamId = "TEAM-B",
            TeamName = "乙班",
        }, CancellationToken.None);

        Assert.Equal(accepted.ReferenceId, replay.ReferenceId);
        Assert.Equal(accepted.AcceptedAtUtc, replay.AcceptedAtUtc);
        var persistedTask = await dbContext.OperationTasks.SingleAsync();
        Assert.Equal("操作员甲", persistedTask.AssignedUserName);
        Assert.Null(persistedTask.DeviceAssetId);
        Assert.Equal("SHIFT-A", persistedTask.ShiftId);
        Assert.Null(persistedTask.TeamId);
        Assert.Equal(Now, persistedTask.AssignedAtUtc);
        var participant = Assert.Single(await dbContext.OperationTaskParticipants.ToArrayAsync());
        Assert.Equal("worker-001", participant.WorkerId);
        Assert.Equal("操作员甲", participant.WorkerName);
        await Assert.ThrowsAsync<MesIdempotencyConflictException>(() => handler.Handle(
            first with { AssignedUserId = "worker-002", AssignedUserName = "操作员乙" },
            CancellationToken.None));
        await Assert.ThrowsAsync<MesIdempotencyConflictException>(() => handler.Handle(
            first with { OperationTaskId = "OP-11" },
            CancellationToken.None));
    }

    [Fact]
    public async Task Claim_intent_fingerprint_preserves_ordinal_field_boundaries()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.WorkOrders.Add(ReleasedWorkOrder("WO-001"));
        dbContext.OperationTasks.Add(NewTask("WO-001", "A|B", 10, null));
        await dbContext.SaveChangesAsync();
        var handler = new ClaimDispatchTaskCommandHandler(dbContext);
        var first = new ClaimDispatchTaskCommand(
            "org-001", "env-dev", "A|B", "C", "操作员甲", null,
            null, Now, "user:C", "claim-boundary-001");

        await handler.Handle(first, CancellationToken.None);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        await Assert.ThrowsAsync<MesIdempotencyConflictException>(() => handler.Handle(
            first with { OperationTaskId = "A", AssignedUserId = "B|C" },
            CancellationToken.None));
        await Assert.ThrowsAsync<MesIdempotencyConflictException>(() => handler.Handle(
            first with { OperationTaskId = "a|b" },
            CancellationToken.None));
    }

    [Fact]
    public async Task Ordinary_start_rechecks_assigned_worker_and_preserves_queued_state_when_skill_expired()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        SeedSingleTask(dbContext, assignedUserId: "worker-001", requiredSkillCode: "cnc-operation");
        await dbContext.SaveChangesAsync();
        var handler = new ChangeOperationTaskStateCommandHandler(dbContext, RejectingGate.Instance);

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new ChangeOperationTaskStateCommand(
                "org-001", "env-dev", "OP-10", "start", Now, "start-skill-expired"),
            CancellationToken.None));

        Assert.Contains("技能已过期", exception.Message, StringComparison.Ordinal);
        Assert.Equal(
            OperationTaskLifecycleStatus.Queued,
            (await dbContext.OperationTasks.SingleAsync()).Status);
        Assert.Empty(await dbContext.CodeIdempotencyKeys.ToArrayAsync());
    }

    [Fact]
    public async Task Authorized_start_rechecks_assigned_worker_and_records_no_authorization_when_skill_expired()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        SeedAuthorizedStartTask(dbContext);
        await dbContext.SaveChangesAsync();
        var handler = new AuthorizeAndStartOperationTaskCommandHandler(
            dbContext,
            ApprovedStartClient.Instance,
            new FakeTimeProvider(Now),
            RejectingGate.Instance);

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new AuthorizeAndStartOperationTaskCommand(
                "org-001", "env-dev", "OP-20", "设备故障，授权跳站", "approval-001",
                "correlation-001", "authorize-skill-expired"),
            CancellationToken.None));

        Assert.Contains("技能已过期", exception.Message, StringComparison.Ordinal);
        Assert.Equal(
            OperationTaskLifecycleStatus.Queued,
            (await dbContext.OperationTasks.SingleAsync(x => x.OperationTaskIdValue == "OP-20")).Status);
        Assert.Empty(await dbContext.OperationTaskStartAuthorizations.ToArrayAsync());
    }

    private static void SeedSingleTask(
        ApplicationDbContext dbContext,
        string? assignedUserId,
        string? requiredSkillCode)
    {
        dbContext.WorkOrders.Add(ReleasedWorkOrder("WO-001"));
        var task = NewTask("WO-001", "OP-10", 10, requiredSkillCode);
        if (assignedUserId is not null)
        {
            task.Assign(assignedUserId, null, null, Now, assignedUserName: "操作员甲");
        }

        dbContext.OperationTasks.Add(task);
    }

    private static void SeedAuthorizedStartTask(ApplicationDbContext dbContext)
    {
        dbContext.WorkOrders.Add(ReleasedWorkOrder("WO-001"));
        dbContext.OperationTasks.Add(NewTask("WO-001", "OP-10", 10, null));
        var target = NewTask("WO-001", "OP-20", 20, "cnc-operation");
        target.Assign("worker-001", null, null, Now, assignedUserName: "操作员甲");
        dbContext.OperationTasks.Add(target);
    }

    private static WorkOrder ReleasedWorkOrder(string workOrderId)
    {
        var workOrder = WorkOrder.Create(
            "org-001", "env-dev", workOrderId, "SKU-FG", "PV-001", 10m, 1,
            Now.AddDays(1), "PCS");
        workOrder.MarkReleased();
        workOrder.RecordMaterialRequirementSnapshot(
            WorkOrder.MaterialRequirementSnapshotNoRequirementsStatus,
            Now.AddHours(-1));
        return workOrder;
    }

    private static OperationTask NewTask(
        string workOrderId,
        string operationTaskId,
        int sequence,
        string? requiredSkillCode) =>
        OperationTask.Queue(
            "org-001", "env-dev", workOrderId, operationTaskId, sequence, $"WC-{sequence}", [],
            Now.AddHours(-1), TimeSpan.FromHours(1), requiredSkillCode: requiredSkillCode);

    private sealed class RejectingGate : IMesWorkerSkillQualificationGate
    {
        public static readonly RejectingGate Instance = new();

        public Task EnsureQualifiedAsync(
            string organizationId,
            string environmentId,
            string? assignedUserId,
            string? requiredSkillCode,
            CancellationToken cancellationToken) =>
            throw new KnownException("人员技能已过期。");
    }

    private sealed class ExactQualificationGate : IMesWorkerSkillQualificationGate
    {
        public static readonly ExactQualificationGate Instance = new();

        public Task EnsureQualifiedAsync(
            string organizationId,
            string environmentId,
            string? assignedUserId,
            string? requiredSkillCode,
            CancellationToken cancellationToken)
        {
            if (organizationId != "org-001" || environmentId != "env-dev" ||
                assignedUserId != "worker-001" || requiredSkillCode != "cnc-operation")
            {
                throw new KnownException("资格查询范围不闭合。");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class ApprovedStartClient : IMesOperationTaskStartApprovalClient
    {
        public static readonly ApprovedStartClient Instance = new();

        public Task<MesOperationTaskStartApproval?> GetApprovedAsync(
            string approvalChainId,
            string organizationId,
            string environmentId,
            string operationTaskId,
            string workOrderId,
            CancellationToken cancellationToken) =>
            Task.FromResult<MesOperationTaskStartApproval?>(new(approvalChainId, "user:supervisor-001"));
    }
}
