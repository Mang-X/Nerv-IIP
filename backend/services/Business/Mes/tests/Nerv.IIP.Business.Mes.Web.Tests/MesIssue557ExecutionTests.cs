using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Production;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Approvals;
using Nerv.IIP.Business.Mes.Web.Application.Behaviors;
using Nerv.IIP.Business.Mes.Web.Application.Errors;
using Nerv.IIP.ServiceAuth;
using System.Net;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MesIssue557ExecutionTests
{
    private static readonly TimeProvider AuthorizationClock = new FixedTimeProvider(Utc("2026-06-29T09:00:00Z"));
    [Fact]
    public async Task Operation_action_lock_is_scoped_to_tenant_and_operation_task()
    {
        var settings = await new ChangeOperationTaskStateCommandLock().GetLockKeysAsync(
            new ChangeOperationTaskStateCommand(
                "org-001",
                "env-dev",
                "OP-10",
                "start",
                Utc("2026-06-29T08:30:00Z"),
                "operation-lock-intent"),
            CancellationToken.None);

        Assert.Equal("business-mes:operation-task-action:org-001:env-dev:OP-10", settings.LockKey);
        Assert.Equal(TimeSpan.FromSeconds(30), settings.AcquireTimeout);
    }

    [Fact]
    public async Task Return_line_side_material_lock_normalizes_guid_and_request_number_to_the_same_aggregate_key()
    {
        await using var dbContext = CreateDbContext(nameof(Return_line_side_material_lock_normalizes_guid_and_request_number_to_the_same_aggregate_key));
        var materialRequest = SeedReceivedMaterialIssue(dbContext, receivedQuantity: 5m);
        await dbContext.SaveChangesAsync();
        var lockProvider = new ReturnLineSideMaterialCommandLock(dbContext);
        var common = new ReturnLineSideMaterialCommand(
            "org-001", "env-dev", "", Utc("2026-06-29T09:00:00Z"), 1m, "lock-normalization");

        var byGuid = await lockProvider.GetLockKeysAsync(
            common with { RequestId = materialRequest.Id.Id.ToString("D") },
            CancellationToken.None);
        var byRequestNo = await lockProvider.GetLockKeysAsync(
            common with { RequestId = materialRequest.RequestNo },
            CancellationToken.None);

        Assert.Equal(byGuid.LockKey, byRequestNo.LockKey);
        Assert.Equal(
            $"business-mes:material-issue-return:org-001:env-dev:{materialRequest.Id.Id:D}",
            byGuid.LockKey);
    }

    [Fact]
    public async Task Return_line_side_material_concurrency_retry_replays_the_command_after_a_lost_save()
    {
        var databaseName = nameof(Return_line_side_material_concurrency_retry_replays_the_command_after_a_lost_save);
        await using var dbContext = CreateDbContext(databaseName);
        var materialRequest = SeedReceivedMaterialIssue(dbContext, receivedQuantity: 5m);
        await dbContext.SaveChangesAsync();
        _ = await dbContext.MaterialIssueRequests.SingleAsync(x => x.RequestNo == materialRequest.RequestNo);
        await using var winningContext = CreateDbContext(databaseName);
        var winningRequest = await winningContext.MaterialIssueRequests.SingleAsync(x => x.RequestNo == materialRequest.RequestNo);
        winningRequest.ReturnLineSideMaterial(
            Utc("2026-06-29T08:59:00Z"),
            1m,
            idempotencyKey: "winner");
        await winningContext.SaveChangesAsync();

        var behavior = new ReturnLineSideMaterialConcurrencyRetryBehavior<ReturnLineSideMaterialCommand, MesAcceptedResponse>(dbContext);
        var command = new ReturnLineSideMaterialCommand(
            "org-001", "env-dev", "MIR-001", Utc("2026-06-29T09:00:00Z"), 1m, "retry-normalization");
        var attempts = 0;

        var result = await behavior.Handle(
            command,
            async _ =>
            {
                attempts++;
                var currentRequest = await dbContext.MaterialIssueRequests
                    .SingleAsync(x => x.RequestNo == materialRequest.RequestNo);
                currentRequest.ReturnLineSideMaterial(
                    command.ReturnedAtUtc,
                    command.ReturnedQuantity,
                    idempotencyKey: command.IdempotencyKey);
                await dbContext.SaveChangesAsync();
                return new MesAcceptedResponse("Accepted", "MIR-001", command.ReturnedAtUtc);
            },
            CancellationToken.None);

        Assert.Equal("Accepted", result.Status);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task Operation_action_replay_with_the_same_intent_returns_the_authoritative_state()
    {
        await using var dbContext = CreateDbContext(nameof(Operation_action_replay_with_the_same_intent_returns_the_authoritative_state));
        var workOrder = WorkOrder.Create("org-001", "env-dev", "WO-001", "SKU-FG", "PV-001", 10m, 1, Utc("2026-06-30T08:00:00Z"), "PCS");
        workOrder.MarkReleased();
        workOrder.RecordMaterialRequirementSnapshot(
            WorkOrder.MaterialRequirementSnapshotNoRequirementsStatus,
            Utc("2026-06-29T08:00:00Z"));
        dbContext.WorkOrders.Add(workOrder);
        dbContext.OperationTasks.Add(OperationTask.Create(
            "org-001",
            "env-dev",
            "WO-001",
            "OP-10",
            OperationTaskLifecycleStatus.Queued,
            10,
            "WC-10",
            [],
            Utc("2026-06-29T08:00:00Z"),
            TimeSpan.FromHours(4),
            null,
            null));
        await dbContext.SaveChangesAsync();
        var handler = new ChangeOperationTaskStateCommandHandler(dbContext);
        var command = new ChangeOperationTaskStateCommand(
            "org-001",
            "env-dev",
            "OP-10",
            "start",
            Utc("2026-06-29T08:30:00Z"),
            "operation-intent-001");

        var first = await handler.Handle(command, CancellationToken.None);
        var replay = await handler.Handle(command, CancellationToken.None);

        Assert.Equal(first, replay);
        Assert.Equal(OperationTaskLifecycleStatus.InProgress, (await dbContext.OperationTasks.SingleAsync()).Status);
    }

    [Fact]
    public async Task Operation_action_rejects_reusing_the_same_key_for_a_different_payload()
    {
        await using var dbContext = CreateDbContext(nameof(Operation_action_rejects_reusing_the_same_key_for_a_different_payload));
        var workOrder = WorkOrder.Create("org-001", "env-dev", "WO-001", "SKU-FG", "PV-001", 10m, 1, Utc("2026-06-30T08:00:00Z"), "PCS");
        workOrder.MarkReleased();
        workOrder.RecordMaterialRequirementSnapshot(
            WorkOrder.MaterialRequirementSnapshotNoRequirementsStatus,
            Utc("2026-06-29T08:00:00Z"));
        dbContext.WorkOrders.Add(workOrder);
        dbContext.OperationTasks.Add(OperationTask.Create(
            "org-001",
            "env-dev",
            "WO-001",
            "OP-10",
            OperationTaskLifecycleStatus.Queued,
            10,
            "WC-10",
            [],
            Utc("2026-06-29T08:00:00Z"),
            TimeSpan.FromHours(4),
            null,
            null));
        await dbContext.SaveChangesAsync();
        var handler = new ChangeOperationTaskStateCommandHandler(dbContext);

        await handler.Handle(
            new ChangeOperationTaskStateCommand(
                "org-001",
                "env-dev",
                "OP-10",
                "start",
                Utc("2026-06-29T08:30:00Z"),
                "operation-intent-conflict"),
            CancellationToken.None);

        await Assert.ThrowsAsync<MesIdempotencyConflictException>(() => handler.Handle(
            new ChangeOperationTaskStateCommand(
                "org-001",
                "env-dev",
                "OP-10",
                "pause",
                Utc("2026-06-29T09:00:00Z"),
                "operation-intent-conflict"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Operation_action_replay_ignores_server_generated_changed_at()
    {
        await using var dbContext = CreateDbContext(nameof(Operation_action_replay_ignores_server_generated_changed_at));
        var workOrder = WorkOrder.Create("org-001", "env-dev", "WO-001", "SKU-FG", "PV-001", 10m, 1, Utc("2026-06-30T08:00:00Z"), "PCS");
        workOrder.MarkReleased();
        workOrder.RecordMaterialRequirementSnapshot(
            WorkOrder.MaterialRequirementSnapshotNoRequirementsStatus,
            Utc("2026-06-29T08:00:00Z"));
        dbContext.WorkOrders.Add(workOrder);
        dbContext.OperationTasks.Add(OperationTask.Create(
            "org-001",
            "env-dev",
            "WO-001",
            "OP-10",
            OperationTaskLifecycleStatus.Queued,
            10,
            "WC-10",
            [],
            Utc("2026-06-29T08:00:00Z"),
            TimeSpan.FromHours(4),
            null,
            null));
        await dbContext.SaveChangesAsync();
        var handler = new ChangeOperationTaskStateCommandHandler(dbContext);
        var first = new ChangeOperationTaskStateCommand(
            "org-001",
            "env-dev",
            "OP-10",
            "start",
            DateTimeOffset.Parse("2026-06-29T08:30:00Z"),
            "operation-time-intent");

        var result = await handler.Handle(first, CancellationToken.None);
        var equivalentOffsetReplay = await handler.Handle(
            first with { ChangedAtUtc = DateTimeOffset.Parse("2026-06-29T16:30:00+08:00") },
            CancellationToken.None);
        var laterServerTimestampReplay = await handler.Handle(
            first with { ChangedAtUtc = DateTimeOffset.Parse("2026-06-29T08:31:00Z") },
            CancellationToken.None);

        Assert.Equal(result, equivalentOffsetReplay);
        Assert.Equal(result, laterServerTimestampReplay);
    }

    [Fact]
    public async Task Operation_start_rejects_later_sequence_before_previous_operations_complete()
    {
        await using var dbContext = CreateDbContext(nameof(Operation_start_rejects_later_sequence_before_previous_operations_complete));
        SeedReleasedWorkOrderWithTwoOperations(dbContext, secondStatus: OperationTaskLifecycleStatus.Queued);
        await dbContext.SaveChangesAsync();

        var handler = new ChangeOperationTaskStateCommandHandler(dbContext);

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new ChangeOperationTaskStateCommand("org-001", "env-dev", "OP-20", "start", Utc("2026-06-29T09:00:00Z")),
            CancellationToken.None));

        Assert.Contains("前序工序", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Authorized_operation_start_bypasses_previous_operation_only_with_reason_authorizer_and_persistent_fact()
    {
        await using var dbContext = CreateDbContext(
            nameof(Authorized_operation_start_bypasses_previous_operation_only_with_reason_authorizer_and_persistent_fact));
        SeedReleasedWorkOrderWithTwoOperations(dbContext, secondStatus: OperationTaskLifecycleStatus.Queued);
        await dbContext.SaveChangesAsync();

        var result = await new AuthorizeAndStartOperationTaskCommandHandler(dbContext, ApprovedOperationTaskStartApprovalClient.Instance, AuthorizationClock).Handle(
            new AuthorizeAndStartOperationTaskCommand(
                "org-001",
                "env-dev",
                "OP-20",
                "设备临时故障，先行处理后续工序",
                "approval-1960-001",
                "correlation-1960-001",
                "skip-start-1960-001"),
            CancellationToken.None);

        Assert.Equal(OperationTaskLifecycleStatus.InProgress.ToString(), result.Status);
        await dbContext.SaveChangesAsync();
        var authorization = await dbContext.OperationTaskStartAuthorizations.SingleAsync();
        Assert.Equal("org-001", authorization.OrganizationId);
        Assert.Equal("env-dev", authorization.EnvironmentId);
        Assert.Equal("OP-20", authorization.OperationTaskId);
        Assert.Equal("approval-1960-001", authorization.ApprovalChainId);
        Assert.Equal("设备临时故障，先行处理后续工序", authorization.Reason);
        Assert.Equal("user:supervisor-001", authorization.AuthorizedBy);
        Assert.Equal("correlation-1960-001", authorization.CorrelationId);
    }

    [Fact]
    public async Task Authorized_operation_start_rejects_missing_reason_or_authorizer_without_changing_task()
    {
        await using var dbContext = CreateDbContext(
            nameof(Authorized_operation_start_rejects_missing_reason_or_authorizer_without_changing_task));
        SeedReleasedWorkOrderWithTwoOperations(dbContext, secondStatus: OperationTaskLifecycleStatus.Queued);
        await dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<KnownException>(() => new AuthorizeAndStartOperationTaskCommandHandler(dbContext, ApprovedOperationTaskStartApprovalClient.Instance, AuthorizationClock).Handle(
            new AuthorizeAndStartOperationTaskCommand(
                "org-001",
                "env-dev",
                "OP-20",
                " ",
                "approval-1960-002",
                "correlation-1960-002",
                "skip-start-1960-002"),
            CancellationToken.None));

        var task = await dbContext.OperationTasks.SingleAsync(x => x.OperationTaskIdValue == "OP-20");
        Assert.Equal(OperationTaskLifecycleStatus.Queued, task.Status);
        Assert.Empty(await dbContext.OperationTaskStartAuthorizations.ToArrayAsync());
    }

    [Fact]
    public async Task Authorization_facts_reject_update_and_delete_through_mes_uow()
    {
        await using var dbContext = CreateDbContext(nameof(Authorization_facts_reject_update_and_delete_through_mes_uow));
        SeedReleasedWorkOrderWithTwoOperations(dbContext, secondStatus: OperationTaskLifecycleStatus.Queued);
        await dbContext.SaveChangesAsync();
        await new AuthorizeAndStartOperationTaskCommandHandler(dbContext, ApprovedOperationTaskStartApprovalClient.Instance, AuthorizationClock)
            .Handle(new AuthorizeAndStartOperationTaskCommand(
                "org-001", "env-dev", "OP-20", "原因", "approval-1960-append-only", "corr-append-only", "idem-append-only"), CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var authorization = await dbContext.OperationTaskStartAuthorizations.SingleAsync();
        dbContext.Entry(authorization).Property(x => x.Reason).CurrentValue = "篡改";
        await Assert.ThrowsAsync<InvalidOperationException>(() => dbContext.SaveChangesAsync());
        dbContext.Entry(authorization).State = EntityState.Unchanged;

        dbContext.Remove(authorization);
        await Assert.ThrowsAsync<InvalidOperationException>(() => dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task Approval_http_client_requires_approved_exact_mes_scope_and_uses_internal_token()
    {
        using var httpClient = new HttpClient(new ApprovalResponseHandler(" approval-1960-http "))
        {
            BaseAddress = new Uri("http://approval.local"),
        };
        var client = new HttpMesOperationTaskStartApprovalClient(
            httpClient,
            new TestInternalServiceTokenProvider("internal-token"));

        var approved = await client.GetApprovedAsync(
            "approval-1960-http",
            "org-001",
            "env-dev",
            "OP-20",
            "WO-001",
            CancellationToken.None);
        var mismatched = await client.GetApprovedAsync(
            "approval-1960-http",
            "org-002",
            "env-dev",
            "OP-20",
            "WO-001",
            CancellationToken.None);

        Assert.Equal("approval-1960-http", approved?.ApprovalChainId);
        Assert.Equal("user:supervisor-001", approved?.AuthorizedBy);
        Assert.Null(mismatched);

        using var mismatchedHttpClient = new HttpClient(new ApprovalResponseHandler("approval-1960-other"))
        {
            BaseAddress = new Uri("http://approval.local"),
        };
        var mismatchedChain = await new HttpMesOperationTaskStartApprovalClient(
            mismatchedHttpClient,
            new TestInternalServiceTokenProvider("internal-token")).GetApprovedAsync(
                "approval-1960-http", "org-001", "env-dev", "OP-20", "WO-001", CancellationToken.None);
        Assert.Null(mismatchedChain);

        foreach (var statusCode in new[] { HttpStatusCode.NotFound, HttpStatusCode.Forbidden })
        {
            using var rejectedHttpClient = new HttpClient(new ApprovalStatusHandler(statusCode))
            {
                BaseAddress = new Uri("http://approval.local"),
            };
            var rejected = await new HttpMesOperationTaskStartApprovalClient(
                rejectedHttpClient,
                new TestInternalServiceTokenProvider("internal-token")).GetApprovedAsync(
                    "approval-1960-http", "org-001", "env-dev", "OP-20", "WO-001", CancellationToken.None);
            Assert.Null(rejected);
        }
    }

    [Fact]
    public async Task Authorized_operation_start_replays_same_intent_and_rejects_different_payload()
    {
        await using var dbContext = CreateDbContext(
            nameof(Authorized_operation_start_replays_same_intent_and_rejects_different_payload));
        SeedReleasedWorkOrderWithTwoOperations(dbContext, secondStatus: OperationTaskLifecycleStatus.Queued);
        await dbContext.SaveChangesAsync();
        var handler = new AuthorizeAndStartOperationTaskCommandHandler(dbContext, ApprovedOperationTaskStartApprovalClient.Instance, AuthorizationClock);
        var command = new AuthorizeAndStartOperationTaskCommand(
            "org-001",
            "env-dev",
            "OP-20",
            "设备临时故障，先行处理后续工序",
            "approval-1960-003",
            "correlation-1960-003",
            "skip-start-1960-003");

        var first = await handler.Handle(command, CancellationToken.None);
        var replay = await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Equal(first, replay);
        Assert.Single(await dbContext.OperationTaskStartAuthorizations.ToArrayAsync());
        await Assert.ThrowsAsync<MesIdempotencyConflictException>(() => handler.Handle(
            command with { Reason = "不同原因" }, CancellationToken.None));
        await Assert.ThrowsAsync<MesIdempotencyConflictException>(() => handler.Handle(
            command with { ApprovalChainId = "approval-1960-other" }, CancellationToken.None));
    }

    [Fact]
    public async Task Authorized_operation_start_canonicalizes_payload_before_persisting_and_replaying()
    {
        await using var dbContext = CreateDbContext(
            nameof(Authorized_operation_start_canonicalizes_payload_before_persisting_and_replaying));
        SeedReleasedWorkOrderWithTwoOperations(dbContext, secondStatus: OperationTaskLifecycleStatus.Queued);
        await dbContext.SaveChangesAsync();
        var handler = new AuthorizeAndStartOperationTaskCommandHandler(dbContext, ApprovedOperationTaskStartApprovalClient.Instance, AuthorizationClock);

        var first = await handler.Handle(new AuthorizeAndStartOperationTaskCommand(
            " org-001 ",
            " env-dev ",
            " OP-20 ",
            " 设备临时故障，先行处理后续工序 ",
            " approval-1960-canonical ",
            " correlation-1960-canonical ",
            " skip-start-1960-canonical "), CancellationToken.None);
        var replay = await handler.Handle(new AuthorizeAndStartOperationTaskCommand(
            "org-001",
            "env-dev",
            "OP-20",
            "设备临时故障，先行处理后续工序",
            "approval-1960-canonical",
            "correlation-1960-canonical",
            "skip-start-1960-canonical"), CancellationToken.None);

        Assert.Equal(first, replay);
        await dbContext.SaveChangesAsync();
        var authorization = Assert.Single(await dbContext.OperationTaskStartAuthorizations.ToArrayAsync());
        Assert.Equal("设备临时故障，先行处理后续工序", authorization.Reason);
        Assert.Equal("correlation-1960-canonical", authorization.CorrelationId);
        Assert.Equal("skip-start-1960-canonical", authorization.IdempotencyKey);
    }

    [Fact]
    public async Task Authorized_operation_start_does_not_use_a_previous_operation_from_another_tenant()
    {
        await using var dbContext = CreateDbContext(
            nameof(Authorized_operation_start_does_not_use_a_previous_operation_from_another_tenant));
        foreach (var (organizationId, sequence, operationTaskId) in new[]
        {
            ("org-001", 20, "OP-20"),
            ("org-002", 10, "OP-10"),
        })
        {
            var workOrder = WorkOrder.Create(
                organizationId,
                "env-dev",
                "WO-SCOPE",
                "SKU-FG",
                "PV-001",
                10m,
                1,
                Utc("2026-06-30T08:00:00Z"),
                "PCS");
            workOrder.MarkReleased();
            workOrder.RecordMaterialRequirementSnapshot(
                WorkOrder.MaterialRequirementSnapshotNoRequirementsStatus,
                Utc("2026-06-29T08:00:00Z"));
            dbContext.WorkOrders.Add(workOrder);
            dbContext.OperationTasks.Add(OperationTask.Create(
                organizationId,
                "env-dev",
                "WO-SCOPE",
                operationTaskId,
                OperationTaskLifecycleStatus.Queued,
                sequence,
                "WC-10",
                [],
                Utc("2026-06-29T08:00:00Z"),
                TimeSpan.FromHours(1),
                null,
                null));
        }
        await dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<KnownException>(() => new AuthorizeAndStartOperationTaskCommandHandler(dbContext, ApprovedOperationTaskStartApprovalClient.Instance, AuthorizationClock).Handle(
            new AuthorizeAndStartOperationTaskCommand(
                "org-001",
                "env-dev",
                "OP-20",
                "跨租户前序不应作为本工序依据",
                "approval-1960-scope",
                "correlation-1960-scope",
                "skip-start-1960-scope"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Operation_pause_on_queued_task_surfaces_lifecycle_conflict()
    {
        await using var dbContext = CreateDbContext(nameof(Operation_pause_on_queued_task_surfaces_lifecycle_conflict));
        var workOrder = WorkOrder.Create("org-001", "env-dev", "WO-001", "SKU-FG", "PV-001", 10m, 1, Utc("2026-06-30T08:00:00Z"), "PCS");
        workOrder.MarkReleased();
        workOrder.RecordMaterialRequirementSnapshot(
            WorkOrder.MaterialRequirementSnapshotNoRequirementsStatus,
            Utc("2026-06-29T08:00:00Z"));
        dbContext.WorkOrders.Add(workOrder);
        dbContext.OperationTasks.Add(OperationTask.Create(
            "org-001",
            "env-dev",
            "WO-001",
            "OP-10",
            OperationTaskLifecycleStatus.Queued,
            10,
            "WC-10",
            [],
            Utc("2026-06-29T08:00:00Z"),
            TimeSpan.FromHours(4),
            null,
            null));
        await dbContext.SaveChangesAsync();

        var handler = new ChangeOperationTaskStateCommandHandler(dbContext);

        var exception = await Assert.ThrowsAsync<MesLifecycleConflictException>(() => handler.Handle(
            new ChangeOperationTaskStateCommand("org-001", "env-dev", "OP-10", "pause", Utc("2026-06-30T09:00:00Z")),
            CancellationToken.None));

        Assert.Equal("pause", exception.Action);
        Assert.Equal(nameof(OperationTaskLifecycleStatus.Queued), exception.CurrentStatus);
    }

    [Theory]
    [InlineData("start", OperationTaskLifecycleStatus.Paused)]
    [InlineData("pause", OperationTaskLifecycleStatus.Queued)]
    [InlineData("resume", OperationTaskLifecycleStatus.InProgress)]
    [InlineData("complete", OperationTaskLifecycleStatus.Paused)]
    public async Task Operation_action_rejects_incompatible_persisted_status_as_lifecycle_conflict(
        string action,
        OperationTaskLifecycleStatus status)
    {
        await using var dbContext = CreateDbContext(
            $"{nameof(Operation_action_rejects_incompatible_persisted_status_as_lifecycle_conflict)}-{action}");
        var workOrder = WorkOrder.Create(
            "org-001",
            "env-dev",
            "WO-STATE",
            "SKU-FG",
            "PV-001",
            10m,
            1,
            Utc("2026-06-30T08:00:00Z"),
            "PCS");
        workOrder.MarkReleased();
        dbContext.WorkOrders.Add(workOrder);
        dbContext.OperationTasks.Add(OperationTask.Create(
            "org-001",
            "env-dev",
            "WO-STATE",
            "OP-STATE",
            status,
            10,
            "WC-10",
            [],
            Utc("2026-06-29T08:00:00Z"),
            TimeSpan.FromHours(4),
            null,
            null));
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<MesLifecycleConflictException>(() =>
            new ChangeOperationTaskStateCommandHandler(dbContext).Handle(
                new ChangeOperationTaskStateCommand(
                    "org-001",
                    "env-dev",
                    "OP-STATE",
                    action,
                    Utc("2026-06-30T09:00:00Z")),
                CancellationToken.None));

        Assert.Equal(action, exception.Action);
        Assert.Equal(status.ToString(), exception.CurrentStatus);
    }

    [Theory]
    [InlineData(MaterialIssueRequest.ReceivedStatus)]
    [InlineData(MaterialIssueRequest.CancelledStatus)]
    [InlineData(MaterialIssueRequest.ReturnRequestedStatus)]
    [InlineData(MaterialIssueRequest.ReservationExpiredStatus)]
    public async Task Line_side_receipt_rejects_terminal_persisted_status_as_lifecycle_conflict(
        string terminalStatus)
    {
        await using var dbContext = CreateDbContext(
            $"{nameof(Line_side_receipt_rejects_terminal_persisted_status_as_lifecycle_conflict)}-{terminalStatus}");
        var materialRequest = MaterialIssueRequest.Create(
            "org-001",
            "env-dev",
            "MIR-STATE",
            "WO-001",
            "OP-10",
            "MAT-001",
            "PCS",
            5m,
            Utc("2026-06-29T08:00:00Z"));
        switch (terminalStatus)
        {
            case MaterialIssueRequest.ReceivedStatus:
                materialRequest.ConfirmAndPostLineSideReceipt(MaterialSupplyTestFixtures.Locations, Utc("2026-06-29T08:30:00Z"), 5m, "LOT-1");
                break;
            case MaterialIssueRequest.CancelledStatus:
                materialRequest.CancelForWorkOrderCancellation(Utc("2026-06-29T08:30:00Z"));
                break;
            case MaterialIssueRequest.ReturnRequestedStatus:
                materialRequest.ConfirmAndPostLineSideReceipt(MaterialSupplyTestFixtures.Locations, Utc("2026-06-29T08:30:00Z"), 2m, "LOT-1");
                materialRequest.CancelForWorkOrderCancellation(Utc("2026-06-29T08:45:00Z"));
                break;
            case MaterialIssueRequest.ReservationExpiredStatus:
                materialRequest.MarkInventoryReservationExpired(Utc("2026-06-29T08:30:00Z"));
                break;
        }
        dbContext.MaterialIssueRequests.Add(materialRequest);
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<MesLifecycleConflictException>(() =>
            new ConfirmLineSideMaterialReceiptCommandHandler(dbContext, MaterialSupplyTestFixtures.Resolver).Handle(
                new ConfirmLineSideMaterialReceiptCommand(
                    "org-001",
                    "env-dev",
                    "MIR-STATE",
                    Utc("2026-06-29T09:00:00Z"),
                    1m),
                CancellationToken.None));

        Assert.Equal("confirm-line-side-receipt", exception.Action);
        Assert.Equal(terminalStatus, exception.CurrentStatus);
    }

    [Fact]
    public async Task Confirm_line_side_receipt_over_requested_quantity_surfaces_domain_rule_as_business_error()
    {
        await using var dbContext = CreateDbContext(nameof(Confirm_line_side_receipt_over_requested_quantity_surfaces_domain_rule_as_business_error));
        dbContext.MaterialIssueRequests.Add(MaterialIssueRequest.Create(
            "org-001",
            "env-dev",
            "MIR-001",
            "WO-001",
            "OP-10",
            "MAT-001",
            "PCS",
            5m,
            Utc("2026-06-29T08:00:00Z")));
        await dbContext.SaveChangesAsync();

        var handler = new ConfirmLineSideMaterialReceiptCommandHandler(dbContext, MaterialSupplyTestFixtures.Resolver);

        // Confirming more than requested trips ConfirmLineSideReceipt's ArgumentOutOfRangeException guard — a sibling
        // of InvalidOperationException that a catch(InvalidOperationException) would miss. It must still surface as a
        // KnownException business error, not an unhandled HTTP 500.
        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new ConfirmLineSideMaterialReceiptCommand(
                "org-001",
                "env-dev",
                "MIR-001",
                Utc("2026-06-29T09:00:00Z"),
                15m),
            CancellationToken.None));

        Assert.Contains("exceed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Operation_complete_rejects_later_sequence_before_previous_operations_complete()
    {
        await using var dbContext = CreateDbContext(nameof(Operation_complete_rejects_later_sequence_before_previous_operations_complete));
        SeedReleasedWorkOrderWithTwoOperations(dbContext, secondStatus: OperationTaskLifecycleStatus.InProgress);
        await dbContext.SaveChangesAsync();

        var handler = new ChangeOperationTaskStateCommandHandler(dbContext);

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new ChangeOperationTaskStateCommand("org-001", "env-dev", "OP-20", "complete", Utc("2026-06-29T10:00:00Z")),
            CancellationToken.None));

        Assert.Equal("前序工序尚未完成：工序 10。", exception.Message);
        Assert.DoesNotContain("OP-10", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("OP-20", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Operation_pause_resume_completion_deducts_paused_time_from_labor_and_machine_hours()
    {
        await using var dbContext = CreateDbContext(nameof(Operation_pause_resume_completion_deducts_paused_time_from_labor_and_machine_hours));
        var workOrder = WorkOrder.Create("org-001", "env-dev", "WO-001", "SKU-FG", "PV-001", 10m, 1, Utc("2026-06-30T08:00:00Z"), "PCS");
        workOrder.MarkReleased();
        workOrder.RecordMaterialRequirementSnapshot(
            WorkOrder.MaterialRequirementSnapshotNoRequirementsStatus,
            Utc("2026-06-29T08:00:00Z"));
        dbContext.WorkOrders.Add(workOrder);
        dbContext.OperationTasks.Add(OperationTask.Create(
            "org-001",
            "env-dev",
            "WO-001",
            "OP-10",
            OperationTaskLifecycleStatus.Queued,
            10,
            "WC-10",
            [],
            Utc("2026-06-29T08:00:00Z"),
            TimeSpan.FromHours(4),
            null,
            null));
        await dbContext.SaveChangesAsync();
        var handler = new ChangeOperationTaskStateCommandHandler(dbContext);

        await handler.Handle(new ChangeOperationTaskStateCommand("org-001", "env-dev", "OP-10", "start", Utc("2026-06-29T08:00:00Z")), CancellationToken.None);
        await handler.Handle(new ChangeOperationTaskStateCommand("org-001", "env-dev", "OP-10", "pause", Utc("2026-06-29T09:00:00Z")), CancellationToken.None);
        await handler.Handle(new ChangeOperationTaskStateCommand("org-001", "env-dev", "OP-10", "resume", Utc("2026-06-29T10:00:00Z")), CancellationToken.None);
        await handler.Handle(new ChangeOperationTaskStateCommand("org-001", "env-dev", "OP-10", "complete", Utc("2026-06-29T12:00:00Z")), CancellationToken.None);

        var task = await dbContext.OperationTasks.SingleAsync();
        Assert.Equal(TimeSpan.FromHours(1), ReadTimeSpan(task, "PausedDuration"));
        Assert.Equal(TimeSpan.FromHours(3), ReadTimeSpan(task, "LaborTime"));
        Assert.Equal(TimeSpan.FromHours(3), ReadTimeSpan(task, "MachineTime"));
    }

    [Fact]
    public async Task Scrap_report_requires_material_consumption_lots_to_drive_inventory_writeoff()
    {
        await using var dbContext = CreateDbContext(nameof(Scrap_report_requires_material_consumption_lots_to_drive_inventory_writeoff));
        SeedStartedOutputOperation(dbContext);
        await dbContext.SaveChangesAsync();
        var handler = new RecordProductionReportCommandHandler(dbContext);

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new RecordProductionReportCommand(
                "org-001",
                "env-dev",
                "WO-001",
                "OP-10",
                GoodQuantity: 0m,
                ScrapQuantity: 1m,
                CompletesOperation: false,
                ReportedAtUtc: Utc("2026-06-29T11:00:00Z")),
            CancellationToken.None));

        Assert.Contains("报废", exception.Message, StringComparison.Ordinal);
        Assert.Contains("耗料", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Non_output_operation_report_rejects_cumulative_good_quantity_above_twenty_percent()
    {
        await using var dbContext = CreateDbContext(nameof(Non_output_operation_report_rejects_cumulative_good_quantity_above_twenty_percent));
        var workOrder = WorkOrder.Create(
            "org-001",
            "env-dev",
            "WO-OVER-001",
            "SKU-FG",
            "PV-001",
            100m,
            1,
            Utc("2026-06-30T08:00:00Z"),
            "PCS",
            overReceiptTolerancePercent: 50m);
        workOrder.MarkReleased();
        workOrder.Start(Utc("2026-06-29T08:00:00Z"));
        dbContext.WorkOrders.Add(workOrder);
        dbContext.OperationTasks.AddRange(
            OperationTask.Create(
                "org-001", "env-dev", "WO-OVER-001", "OP-10",
                OperationTaskLifecycleStatus.InProgress, 10, "WC-10", [],
                Utc("2026-06-29T08:00:00Z"), TimeSpan.FromHours(1),
                Utc("2026-06-29T08:00:00Z"), null),
            OperationTask.Create(
                "org-001", "env-dev", "WO-OVER-001", "OP-20",
                OperationTaskLifecycleStatus.Queued, 20, "WC-20", [],
                Utc("2026-06-29T09:00:00Z"), TimeSpan.FromHours(1), null, null));
        dbContext.ProductionReports.Add(ProductionReport.Record(
            "org-001", "env-dev", "RPT-OVER-001", "WO-OVER-001", "OP-10",
            100m, 0m, false, Utc("2026-06-29T09:00:00Z")));
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            new RecordProductionReportCommandHandler(dbContext).Handle(
                new RecordProductionReportCommand(
                    "org-001", "env-dev", "WO-OVER-001", "OP-10",
                    GoodQuantity: 20.000001m,
                    ScrapQuantity: 0m,
                    CompletesOperation: true,
                    ReportedAtUtc: Utc("2026-06-29T10:00:00Z")),
                CancellationToken.None));

        Assert.Contains("生产工单 WO-OVER-001", exception.Message, StringComparison.Ordinal);
        Assert.Contains("工序 OP-10", exception.Message, StringComparison.Ordinal);
        Assert.Contains("累计合格数量", exception.Message, StringComparison.Ordinal);
        Assert.Contains("调整本次合格数量或工单计划量", exception.Message, StringComparison.Ordinal);
        Assert.Single(dbContext.ProductionReports);
    }

    [Fact]
    public async Task Output_operation_report_auto_generates_output_lot_and_persists_genealogy_breakpoint()
    {
        await using var dbContext = CreateDbContext(nameof(Output_operation_report_auto_generates_output_lot_and_persists_genealogy_breakpoint));
        SeedStartedOutputOperation(dbContext);
        await dbContext.SaveChangesAsync();
        var handler = new RecordProductionReportCommandHandler(dbContext);

        var result = await handler.Handle(
            new RecordProductionReportCommand(
                "org-001",
                "env-dev",
                "WO-001",
                "OP-10",
                GoodQuantity: 2m,
                ScrapQuantity: 0m,
                CompletesOperation: true,
                ReportedAtUtc: Utc("2026-06-29T11:00:00Z")),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var report = await dbContext.ProductionReports.SingleAsync(x => x.Id == result.Id);
        Assert.False(string.IsNullOrWhiteSpace(report.ProducedLotNo));
        var genealogy = await dbContext.OutputLotGenealogies.SingleAsync();
        Assert.Equal("WO-001", genealogy.WorkOrderId);
        Assert.Equal("OP-10", genealogy.OperationTaskId);
        Assert.Equal(report.ReportNo, genealogy.ReportNo);
        Assert.Equal(report.ProducedLotNo, genealogy.ProducedLotNo);
        Assert.Equal(2m, genealogy.Quantity);
    }

    [Fact]
    public async Task Output_operation_report_rejects_duplicate_explicit_output_lot_before_database_unique_constraint()
    {
        await using var dbContext = CreateDbContext(nameof(Output_operation_report_rejects_duplicate_explicit_output_lot_before_database_unique_constraint));
        SeedStartedOutputOperation(dbContext);
        await dbContext.SaveChangesAsync();
        var handler = new RecordProductionReportCommandHandler(dbContext);
        await handler.Handle(
            new RecordProductionReportCommand(
                "org-001",
                "env-dev",
                "WO-001",
                "OP-10",
                GoodQuantity: 1m,
                ScrapQuantity: 0m,
                CompletesOperation: false,
                ReportedAtUtc: Utc("2026-06-29T10:00:00Z"),
                ProducedLotNo: "LOT-DUP"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new RecordProductionReportCommand(
                "org-001",
                "env-dev",
                "WO-001",
                "OP-10",
                GoodQuantity: 1m,
                ScrapQuantity: 0m,
                CompletesOperation: false,
                ReportedAtUtc: Utc("2026-06-29T10:10:00Z"),
                ProducedLotNo: "LOT-DUP"),
            CancellationToken.None));

        Assert.Contains("产出批次", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Output_lot_genealogy_relational_constraints_reject_duplicate_lots()
    {
        await using var connection = await CreateOpenSqliteConnectionAsync();
        await using var dbContext = CreateSqliteDbContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        SeedStartedOutputOperation(dbContext);
        await dbContext.SaveChangesAsync();
        var firstReport = ProductionReport.Record(
            "org-001",
            "env-dev",
            "RPT-001",
            "WO-001",
            "OP-10",
            1m,
            0m,
            true,
            Utc("2026-06-29T10:00:00Z"),
            0m,
            producedLotNo: "LOT-DUP");
        var secondReport = ProductionReport.Record(
            "org-001",
            "env-dev",
            "RPT-002",
            "WO-001",
            "OP-10",
            1m,
            0m,
            false,
            Utc("2026-06-29T10:10:00Z"),
            0m,
            producedLotNo: "LOT-DUP");
        dbContext.ProductionReports.AddRange(firstReport, secondReport);
        dbContext.OutputLotGenealogies.Add(OutputLotGenealogy.Create(
            "org-001",
            "env-dev",
            "WO-001",
            "OP-10",
            "RPT-001",
            "LOT-DUP",
            null,
            1m,
            Utc("2026-06-29T10:00:00Z")));
        dbContext.OutputLotGenealogies.Add(OutputLotGenealogy.Create(
            "org-001",
            "env-dev",
            "WO-001",
            "OP-10",
            "RPT-002",
            "LOT-DUP",
            null,
            1m,
            Utc("2026-06-29T10:10:00Z")));

        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task Finished_goods_receipt_requires_existing_output_lot_for_same_work_order()
    {
        await using var dbContext = CreateDbContext(nameof(Finished_goods_receipt_requires_existing_output_lot_for_same_work_order));
        SeedStartedOutputOperation(dbContext);
        await dbContext.SaveChangesAsync();
        var handler = new CreateFinishedGoodsReceiptRequestCommandHandler(dbContext);

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new CreateFinishedGoodsReceiptRequestCommand(
                "org-001",
                "env-dev",
                "WO-001",
                "SKU-FG",
                2m,
                "PCS",
                Utc("2026-06-29T12:00:00Z"),
                10m,
                ProducedLotNo: "LOT-NOT-REPORTED"),
            CancellationToken.None));

        Assert.Contains("产出批次", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Return_line_side_material_command_reduces_received_quantity_and_emits_inventory_reversal_intent()
    {
        await using var dbContext = CreateDbContext(nameof(Return_line_side_material_command_reduces_received_quantity_and_emits_inventory_reversal_intent));
        var materialRequest = MaterialIssueRequest.Create(
            "org-001",
            "env-dev",
            "MIR-001",
            "WO-001",
            "OP-10",
            "MAT-001",
            "PCS",
            5m,
            Utc("2026-06-29T08:00:00Z"));
        materialRequest.ConfirmAndPostLineSideReceipt(MaterialSupplyTestFixtures.Locations, Utc("2026-06-29T08:30:00Z"), 5m, "LOT-MAT-001");
        materialRequest.ClearDomainEvents();
        dbContext.MaterialIssueRequests.Add(materialRequest);
        await dbContext.SaveChangesAsync();

        var handler = new ReturnLineSideMaterialCommandHandler(dbContext);

        await handler.Handle(
            new ReturnLineSideMaterialCommand(
                "org-001",
                "env-dev",
                "MIR-001",
                Utc("2026-06-29T09:00:00Z"),
                2m,
                "legacy-return-1"),
            CancellationToken.None);

        Assert.Equal(3m, materialRequest.ReceivedQuantity);
        var eventNames = materialRequest.GetDomainEvents().Select(x => x.GetType().Name).ToArray();
        Assert.Contains("MaterialLineSideReturnRequestedDomainEvent", eventNames);
        Assert.Contains("MaterialReturnedToWarehouseDomainEvent", eventNames);
    }

    [Fact]
    public async Task Return_line_side_material_replays_same_idempotency_key_without_a_second_return()
    {
        await using var dbContext = CreateDbContext(nameof(Return_line_side_material_replays_same_idempotency_key_without_a_second_return));
        var materialRequest = SeedReceivedMaterialIssue(dbContext, receivedQuantity: 5m);
        await dbContext.SaveChangesAsync();
        var handler = new ReturnLineSideMaterialCommandHandler(dbContext);
        var first = new ReturnLineSideMaterialCommand(
            "org-001", "env-dev", "MIR-001", Utc("2026-06-29T09:00:00Z"), 2m, "mes-return-intent-1");

        await handler.Handle(first, CancellationToken.None);
        materialRequest.ClearDomainEvents();
        await dbContext.SaveChangesAsync();

        await handler.Handle(first, CancellationToken.None);

        Assert.Equal(3m, materialRequest.ReceivedQuantity);
        Assert.Empty(materialRequest.GetDomainEvents());
    }

    [Fact]
    public async Task Return_line_side_material_rejects_reusing_idempotency_key_for_different_quantity()
    {
        await using var dbContext = CreateDbContext(nameof(Return_line_side_material_rejects_reusing_idempotency_key_for_different_quantity));
        var materialRequest = SeedReceivedMaterialIssue(dbContext, receivedQuantity: 5m);
        await dbContext.SaveChangesAsync();
        var handler = new ReturnLineSideMaterialCommandHandler(dbContext);
        const string key = "mes-return-intent-2";

        await handler.Handle(
            new ReturnLineSideMaterialCommand(
                "org-001", "env-dev", "MIR-001", Utc("2026-06-29T09:00:00Z"), 2m, key),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new ReturnLineSideMaterialCommand(
                "org-001", "env-dev", "MIR-001", Utc("2026-06-29T09:01:00Z"), 1m, key),
            CancellationToken.None));

        Assert.Contains("幂等键", exception.Message, StringComparison.Ordinal);
        Assert.Equal(3m, materialRequest.ReceivedQuantity);
    }

    [Fact]
    public async Task Return_line_side_material_rejects_quantity_already_consumed_by_production_report()
    {
        await using var dbContext = CreateDbContext(nameof(Return_line_side_material_rejects_quantity_already_consumed_by_production_report));
        var materialRequest = SeedReceivedMaterialIssue(dbContext, receivedQuantity: 5m);
        dbContext.ProductionReportMaterialConsumptions.Add(ProductionReportMaterialConsumption.Record(
            "org-001",
            "env-dev",
            "RPT-001",
            "WO-001",
            "OP-10",
            "MAT-001",
            "LOT-MAT-001",
            "PCS",
            3m,
            "MIR-001"));
        await dbContext.SaveChangesAsync();
        var handler = new ReturnLineSideMaterialCommandHandler(dbContext);

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new ReturnLineSideMaterialCommand(
                "org-001",
                "env-dev",
                "MIR-001",
                Utc("2026-06-29T09:00:00Z"),
                3m,
                "legacy-return-consumed"),
            CancellationToken.None));

        Assert.Contains("可退", exception.Message, StringComparison.Ordinal);
        Assert.Equal(5m, materialRequest.ReceivedQuantity);
    }

    [Fact]
    public async Task Return_line_side_material_clears_lot_when_return_reduces_received_quantity_to_zero()
    {
        await using var dbContext = CreateDbContext(nameof(Return_line_side_material_clears_lot_when_return_reduces_received_quantity_to_zero));
        var materialRequest = SeedReceivedMaterialIssue(dbContext, receivedQuantity: 5m);
        await dbContext.SaveChangesAsync();
        var handler = new ReturnLineSideMaterialCommandHandler(dbContext);

        await handler.Handle(
            new ReturnLineSideMaterialCommand(
                "org-001",
                "env-dev",
                "MIR-001",
                Utc("2026-06-29T09:00:00Z"),
                5m,
                "legacy-return-zero"),
            CancellationToken.None);

        Assert.Equal(0m, materialRequest.ReceivedQuantity);
        Assert.Null(materialRequest.MaterialLotId);
        Assert.Null(materialRequest.ReceivedAtUtc);
    }

    [Fact]
    public async Task Cancel_work_order_maps_received_material_without_lot_to_business_error()
    {
        await using var dbContext = CreateDbContext(nameof(Cancel_work_order_maps_received_material_without_lot_to_business_error));
        var workOrder = WorkOrder.Create("org-001", "env-dev", "WO-695-NOLOT", "SKU-FG", "PV-001", 10m, 1, Utc("2026-07-03T08:00:00Z"), "PCS");
        workOrder.MarkReleased();
        var materialRequest = MaterialIssueRequest.Create(
            "org-001",
            "env-dev",
            "MIR-695-NOLOT",
            "WO-695-NOLOT",
            "OP-10",
            "MAT-001",
            "PCS",
            5m,
            Utc("2026-07-03T07:00:00Z"));
        materialRequest.ConfirmAndPostLineSideReceipt(MaterialSupplyTestFixtures.Locations, Utc("2026-07-03T07:30:00Z"), 5m);
        materialRequest.ClearDomainEvents();
        dbContext.WorkOrders.Add(workOrder);
        dbContext.MaterialIssueRequests.Add(materialRequest);
        await dbContext.SaveChangesAsync();
        var handler = new CancelWorkOrderCommandHandler(dbContext);

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new CancelWorkOrderCommand("org-001", "env-dev", "WO-695-NOLOT", "plan cancelled", Utc("2026-07-03T09:00:00Z")),
            CancellationToken.None));

        Assert.Contains("received material lot", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    private static void SeedReleasedWorkOrderWithTwoOperations(ApplicationDbContext dbContext, OperationTaskLifecycleStatus secondStatus)
    {
        var workOrder = WorkOrder.Create("org-001", "env-dev", "WO-001", "SKU-FG", "PV-001", 10m, 1, Utc("2026-06-30T08:00:00Z"), "PCS");
        workOrder.MarkReleased();
        workOrder.RecordMaterialRequirementSnapshot(
            WorkOrder.MaterialRequirementSnapshotNoRequirementsStatus,
            Utc("2026-06-29T08:00:00Z"));
        dbContext.WorkOrders.Add(workOrder);
        dbContext.OperationTasks.Add(OperationTask.Create(
            "org-001",
            "env-dev",
            "WO-001",
            "OP-10",
            OperationTaskLifecycleStatus.Queued,
            10,
            "WC-10",
            [],
            Utc("2026-06-29T08:00:00Z"),
            TimeSpan.FromHours(1),
            null,
            null));
        dbContext.OperationTasks.Add(OperationTask.Create(
            "org-001",
            "env-dev",
            "WO-001",
            "OP-20",
            secondStatus,
            20,
            "WC-20",
            [],
            Utc("2026-06-29T09:00:00Z"),
            TimeSpan.FromHours(1),
            secondStatus == OperationTaskLifecycleStatus.InProgress ? Utc("2026-06-29T09:00:00Z") : null,
            null));
    }

    private static void SeedStartedOutputOperation(ApplicationDbContext dbContext)
    {
        var workOrder = WorkOrder.Create("org-001", "env-dev", "WO-001", "SKU-FG", "PV-001", 10m, 1, Utc("2026-06-30T08:00:00Z"), "PCS");
        workOrder.MarkReleased();
        workOrder.Start(Utc("2026-06-29T08:00:00Z"));
        dbContext.WorkOrders.Add(workOrder);
        dbContext.OperationTasks.Add(OperationTask.Create(
            "org-001",
            "env-dev",
            "WO-001",
            "OP-10",
            OperationTaskLifecycleStatus.InProgress,
            10,
            "WC-10",
            [],
            Utc("2026-06-29T08:00:00Z"),
            TimeSpan.FromHours(1),
            Utc("2026-06-29T08:00:00Z"),
            null));
    }

    private static MaterialIssueRequest SeedReceivedMaterialIssue(ApplicationDbContext dbContext, decimal receivedQuantity)
    {
        var materialRequest = MaterialIssueRequest.Create(
            "org-001",
            "env-dev",
            "MIR-001",
            "WO-001",
            "OP-10",
            "MAT-001",
            "PCS",
            5m,
            Utc("2026-06-29T08:00:00Z"));
        materialRequest.ConfirmAndPostLineSideReceipt(MaterialSupplyTestFixtures.Locations, Utc("2026-06-29T08:30:00Z"), receivedQuantity, "LOT-MAT-001");
        materialRequest.ClearDomainEvents();
        dbContext.MaterialIssueRequests.Add(materialRequest);
        return materialRequest;
    }

    private static TimeSpan ReadTimeSpan(OperationTask task, string propertyName)
    {
        var property = typeof(OperationTask).GetProperty(propertyName);
        Assert.NotNull(property);
        return Assert.IsType<TimeSpan>(property.GetValue(task));
    }

    private static DateTimeOffset Utc(string value) => DateTimeOffset.Parse(value);

    private static ApplicationDbContext CreateDbContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private static async Task<SqliteConnection> CreateOpenSqliteConnectionAsync()
    {
        var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    private static ApplicationDbContext CreateSqliteDbContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class ApprovedOperationTaskStartApprovalClient : IMesOperationTaskStartApprovalClient
    {
        public static ApprovedOperationTaskStartApprovalClient Instance { get; } = new();

        public Task<MesOperationTaskStartApproval?> GetApprovedAsync(
            string approvalChainId,
            string organizationId,
            string environmentId,
            string operationTaskId,
            string workOrderId,
            CancellationToken cancellationToken)
        {
            Assert.Equal("org-001", organizationId);
            Assert.Equal("env-dev", environmentId);
            Assert.Equal("OP-20", operationTaskId);
            Assert.Contains(workOrderId, new[] { "WO-001", "WO-SCOPE" });
            Assert.False(string.IsNullOrWhiteSpace(approvalChainId));
            return Task.FromResult<MesOperationTaskStartApproval?>(
                new MesOperationTaskStartApproval(approvalChainId, "user:supervisor-001"));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed record TestInternalServiceTokenProvider(string BearerToken) : IInternalServiceTokenProvider;

    private sealed class ApprovalResponseHandler(string chainId) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/api/business/v1/approvals/chains/approval-1960-http", request.RequestUri?.AbsolutePath);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("internal-token", request.Headers.Authorization?.Parameter);
            Assert.Null(request.Content);
            var json = """
                {"data":{"chainId":"CHAIN_ID","organizationId":"org-001","environmentId":"env-dev","status":"approved","sourceService":"business-mes","documentType":"mes-operation-task-start-authorization","documentId":"OP-20","documentLineId":"WO-001","decisions":[{"decisionId":"decision-1","stepNo":1,"actorType":"user","actorRef":"supervisor-001","decision":"approve","decidedAtUtc":"2026-06-29T08:00:00Z"}]},"success":true,"message":"","code":0}
                """.Replace("CHAIN_ID", chainId, StringComparison.Ordinal);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class ApprovalStatusHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode));
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
