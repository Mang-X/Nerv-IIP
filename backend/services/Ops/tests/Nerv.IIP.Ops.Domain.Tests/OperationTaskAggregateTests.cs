using System.Reflection;
using System.Text.Json;
using Nerv.IIP.Contracts.ConnectorProtocol;
using Nerv.IIP.Ops.Domain;
using Nerv.IIP.Ops.Domain.AggregatesModel.OperationTemplateAggregate;
using Nerv.IIP.Ops.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Ops.Domain.DomainEvents;

namespace Nerv.IIP.Ops.Domain.Tests;

public sealed class OperationTaskAggregateTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-05-14T00:00:00Z");

    [Fact]
    public void Create_initializes_queued_task_and_idempotency_scope()
    {
        var task = OperationTask.Create(TaskId(), CreateRequest("idem-001"), Template("lifecycle.restart"), Now);
        task.AssignInitialAuditId(AuditId());

        var fact = task.ToFact();
        var response = task.ToDetailFact();

        Assert.Equal("op-000001", fact.OperationTaskId);
        Assert.Equal("queued", fact.Status);
        Assert.Equal("idem-001", fact.IdempotencyKey);
        Assert.Equal("org-001\u001fenv-dev\u001fidem-001", OperationTask.GetIdempotencyScope(fact.OrganizationId, fact.EnvironmentId, fact.IdempotencyKey));
        Assert.Equal("operation.requested", Assert.Single(response.AuditRecords).Action);
    }

    [Fact]
    public void Create_rejects_unsupported_operation_code()
    {
        var request = CreateRequest("idem-unsupported") with { OperationCode = "lifecycle.unsupported" };

        var ex = Assert.Throws<InvalidOperationTaskRequestException>(() => OperationTask.Create(TaskId(), request, Template("lifecycle.restart"), Now));

        Assert.Contains("Unsupported operation code", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_rejects_disabled_operation_template()
    {
        var template = Template("lifecycle.restart") with { Enabled = false };

        var ex = Assert.Throws<InvalidOperationTaskRequestException>(() => OperationTask.Create(TaskId(), CreateRequest("idem-disabled"), template, Now));

        Assert.Contains("disabled operation template", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Template_can_describe_operation_defaults()
    {
        var template = OperationTemplate.Create(
            TemplateId(),
            "backup.snapshot",
            "Backup snapshot",
            """{"type":"object"}""",
            "medium",
            5,
            900,
            requiresApproval: false,
            Now);

        Assert.Equal("backup.snapshot", template.OperationCode);
        Assert.Equal(5, template.DefaultMaxAttempts);
        Assert.Equal(900, template.DefaultLeaseDurationSeconds);
        Assert.True(template.Enabled);
    }

    [Fact]
    public void Template_rejects_unknown_risk_level()
    {
        var create = () => OperationTemplate.Create(
            TemplateId(),
            "backup.snapshot",
            "Backup snapshot",
            "{}",
            "experimental",
            3,
            300,
            requiresApproval: false,
            Now);

        var ex = Assert.Throws<InvalidOperationTaskRequestException>(create);
        Assert.Contains("risk level", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Claim_moves_queued_task_to_dispatched_and_blocks_second_claim()
    {
        var task = CreateTask();

        var dispatch = Claim(task, "attempt-000001", "audit-000002", "lease-001", "connector-host-001");
        var secondClaim = () => Claim(task, "attempt-000002", "audit-000003", "lease-002", "connector-host-002");

        Assert.Equal("dispatched", task.ToFact().Status);
        Assert.Equal("attempt-000001", dispatch.AttemptId);
        Assert.Equal("connector-host-001", dispatch.ConnectorHostId);
        var attempt = Assert.Single(task.ToDetailFact().Attempts);
        Assert.Equal("started", attempt.Status);
        Assert.Throws<InvalidOperationResultException>(secondClaim);
    }

    [Fact]
    public void Approval_required_task_waits_for_approval_before_claim()
    {
        var task = OperationTask.Create(TaskId(), CreateRequest("idem-approval-001"), Template("lifecycle.restart", requiresApproval: true), Now);
        task.AssignPendingAuditIds([AuditId("audit-000001"), AuditId("audit-000002")]);

        var claimBeforeApproval = () => Claim(task, "attempt-000001", "audit-000004", "lease-001", "connector-host-001");

        Assert.Equal("approval-pending", task.ToFact().Status);
        var pending = task.ToFact().Approval;
        Assert.NotNull(pending);
        Assert.Equal("pending", pending.Status);
        Assert.Contains(task.ToDetailFact().AuditRecords, x => x.Action == "operation.approval-requested");
        Assert.Throws<InvalidOperationResultException>(claimBeforeApproval);

        task.Approve(
            new DecideOperationApprovalInput("org-001", "env-dev", "ops-approver", "approved for maintenance window", "corr-approval"),
            AuditId("audit-000003"),
            Now.AddMinutes(1));

        Assert.Equal("queued", task.ToFact().Status);
        var approved = task.ToFact().Approval;
        Assert.NotNull(approved);
        Assert.Equal("approved", approved.Status);
        var dispatch = Claim(task, "attempt-000001", "audit-000004", "lease-001", "connector-host-001");
        Assert.Equal("attempt-000001", dispatch.AttemptId);
    }

    [Fact]
    public void Approval_required_task_can_be_rejected_as_terminal()
    {
        var task = OperationTask.Create(TaskId(), CreateRequest("idem-reject-001"), Template("lifecycle.restart", requiresApproval: true), Now);
        task.AssignPendingAuditIds([AuditId("audit-000001"), AuditId("audit-000002")]);

        task.Reject(
            new DecideOperationApprovalInput("org-001", "env-dev", "ops-approver", "not in change window", "corr-reject"),
            AuditId("audit-000003"),
            Now.AddMinutes(1));

        Assert.Equal("rejected", task.ToFact().Status);
        var approval = task.ToFact().Approval;
        Assert.NotNull(approval);
        Assert.Equal("rejected", approval.Status);
        Assert.Equal("ops-approver", approval.DecidedBy);
        Assert.Throws<InvalidOperationResultException>(() => Claim(task, "attempt-000001", "audit-000004", "lease-001", "connector-host-001"));
    }

    [Fact]
    public void Assign_initial_audit_id_requires_single_pending_audit()
    {
        var task = OperationTask.Create(TaskId(), CreateRequest("idem-approval-guard-001"), Template("lifecycle.restart", requiresApproval: true), Now);

        var assignInitialAudit = () => task.AssignInitialAuditId(AuditId("audit-000001"));

        Assert.Throws<InvalidOperationTaskRequestException>(assignInitialAudit);
    }

    [Fact]
    public void Completion_moves_dispatched_task_to_completed()
    {
        var task = CreateTask();
        var dispatch = Claim(task, "attempt-000001", "audit-000002", "lease-001", "connector-host-001");

        var response = task.RecordResult(Result(dispatch, "succeeded"), AuditId("audit-000003"));

        Assert.Equal("completed", response.Status);
        var attempt = Assert.Single(response.Attempts);
        Assert.Equal("completed", attempt.Status);
        Assert.Contains(response.AuditRecords, x => x.Action == "operation.completed");
    }

    [Fact]
    public void Audit_records_raise_specific_audit_recorded_domain_events()
    {
        var task = OperationTask.Create(TaskId(), CreateRequest("idem-001"), Template("lifecycle.restart"), Now);
        task.AssignInitialAuditId(AuditId("audit-000001"));

        var requestedEvents = task.GetDomainEvents().ToArray();
        Assert.IsType<OperationTaskCreatedDomainEvent>(requestedEvents[0]);
        var requestedAudit = Assert.IsType<AuditRecordedDomainEvent>(requestedEvents[1]);
        Assert.Equal("audit-000001", requestedAudit.AuditRecord.Id.Id);
        Assert.Equal("operation.requested", requestedAudit.AuditRecord.Action);

        task.ClearDomainEvents();
        var dispatch = Claim(task, "attempt-000001", "audit-000002", "lease-001", "connector-host-001");
        var claimedEvents = task.GetDomainEvents().ToArray();
        Assert.IsType<OperationTaskDispatchedDomainEvent>(claimedEvents[0]);
        var claimedAudit = Assert.IsType<AuditRecordedDomainEvent>(claimedEvents[1]);
        Assert.Equal("audit-000002", claimedAudit.AuditRecord.Id.Id);
        Assert.Equal("operation.claimed", claimedAudit.AuditRecord.Action);

        task.ClearDomainEvents();
        task.RecordResult(Result(dispatch, "succeeded"), AuditId("audit-000003"));
        var completedEvents = task.GetDomainEvents().ToArray();
        Assert.IsType<OperationResultRecordedDomainEvent>(completedEvents[0]);
        Assert.IsType<OperationTaskCompletedDomainEvent>(completedEvents[1]);
        var completedAudit = Assert.IsType<AuditRecordedDomainEvent>(completedEvents[2]);
        Assert.Equal("audit-000003", completedAudit.AuditRecord.Id.Id);
        Assert.Equal("operation.completed", completedAudit.AuditRecord.Action);
    }

    [Fact]
    public void Audit_records_include_tamper_evident_integrity_hash()
    {
        var task = OperationTask.Create(TaskId(), CreateRequest("idem-001"), Template("lifecycle.restart"), Now);
        task.AssignInitialAuditId(AuditId("audit-000001"));

        var audit = Assert.Single(task.AuditRecords);

        Assert.True(audit.HasValidIntegrityHash());
        Assert.StartsWith("sha256:", audit.IntegrityHash, StringComparison.Ordinal);
        Assert.Equal(
            AuditRecord.ComputeIntegrityHash(
                "audit-000001",
                "op-000001",
                1,
                string.Empty,
                "operation.requested",
                "local-admin",
                Now,
                "corr-001"),
            audit.IntegrityHash);
    }

    [Fact]
    public void Audit_records_include_monotonic_chain_metadata()
    {
        var task = OperationTask.Create(TaskId(), CreateRequest("idem-chain-001"), Template("lifecycle.restart"), Now);
        task.AssignInitialAuditId(AuditId("audit-000001"));
        var dispatch = Claim(task, "attempt-000001", "audit-000002", "lease-001", "connector-host-001");
        task.RecordResult(Result(dispatch, "succeeded"), AuditId("audit-000003"));

        var records = task.AuditRecords.OrderBy(x => x.SequenceNo).ToArray();

        Assert.Equal([1L, 2L, 3L], records.Select(x => x.SequenceNo).ToArray());
        Assert.Equal(string.Empty, records[0].PreviousIntegrityHash);
        Assert.Equal(records[0].IntegrityHash, records[1].PreviousIntegrityHash);
        Assert.Equal(records[1].IntegrityHash, records[2].PreviousIntegrityHash);
        Assert.All(records, x => Assert.True(x.HasValidIntegrityHash()));
    }

    [Fact]
    public void Approval_decision_rejects_requester_self_approval()
    {
        var task = OperationTask.Create(TaskId(), CreateRequest("idem-self-approval-001"), Template("lifecycle.restart", requiresApproval: true), Now);
        task.AssignPendingAuditIds([AuditId("audit-000001"), AuditId("audit-000002")]);

        var approve = () => task.Approve(
            new DecideOperationApprovalInput("org-001", "env-dev", "local-admin", "self approval", "corr-self-approval"),
            AuditId("audit-000003"),
            Now.AddMinutes(1));

        var ex = Assert.Throws<InvalidOperationTaskRequestException>(approve);
        Assert.Contains("requester cannot approve", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("approval-pending", task.ToFact().Status);
    }

    [Fact]
    public void Failed_result_moves_dispatched_task_to_failed_and_rejects_duplicate_result()
    {
        var task = CreateTask();
        var dispatch = Claim(task, "attempt-000001", "audit-000002", "lease-001", "connector-host-001");

        var response = task.RecordResult(Result(dispatch, "failed", Failure("restart-failed", "process exited")), AuditId("audit-000003"));
        var duplicate = () => task.RecordResult(Result(dispatch, "failed", Failure("restart-failed", "process exited")), AuditId("audit-000004"));

        Assert.Equal("failed", response.Status);
        var attempt = Assert.Single(response.Attempts);
        Assert.Equal("failed", attempt.Status);
        Assert.Equal("restart-failed", attempt.FailureCode);
        Assert.Throws<InvalidOperationResultException>(duplicate);
    }

    [Fact]
    public void Attempt_fact_reads_failure_json_with_legacy_connector_failure_shape()
    {
        var task = CreateTask();
        Claim(task, "attempt-000001", "audit-000002", "lease-001", "connector-host-001");
        var attempt = Assert.Single(task.Attempts);
        var legacyFailureJson = JsonSerializer.Serialize(
            new FailureReason(
                "restart-failed",
                "process exited",
                "connector",
                Retryable: true,
                new Dictionary<string, string> { ["exitCode"] = "1" }),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        typeof(OperationAttempt)
            .GetProperty(nameof(OperationAttempt.FailureJson), BindingFlags.Instance | BindingFlags.Public)!
            .SetValue(attempt, legacyFailureJson);

        var failure = Assert.Single(task.ToDetailFact().Attempts).Failure;

        Assert.NotNull(failure);
        Assert.Equal("restart-failed", failure.Code);
        Assert.Equal("process exited", failure.Message);
        Assert.Equal("connector", failure.Category);
        Assert.True(failure.Retryable);
        Assert.Equal("1", failure.Detail["exitCode"]);
    }

    [Fact]
    public void Abandoned_claim_can_be_retried_until_max_attempts()
    {
        var task = CreateTask();
        var firstDispatch = Claim(task, "attempt-000001", "audit-000002", "lease-001", "connector-host-001", maxAttempts: 2);

        task.AbandonLease(firstDispatch.LeaseId, "connector-host-001", "host-shutdown", AuditId("audit-000003"), Now.AddSeconds(10));
        var secondDispatch = Claim(task, "attempt-000002", "audit-000004", "lease-002", "connector-host-002", maxAttempts: 2);

        Assert.Equal("dispatched", task.ToFact().Status);
        Assert.Equal(2, secondDispatch.AttemptNo);
        Assert.Equal("connector-host-002", secondDispatch.ConnectorHostId);
        Assert.Equal(["abandoned", "started"], task.ToDetailFact().Attempts.Select(x => x.Status));
    }

    [Fact]
    public void Abandoning_final_attempt_marks_task_failed()
    {
        var task = CreateTask();
        var dispatch = Claim(task, "attempt-000001", "audit-000002", "lease-001", "connector-host-001", maxAttempts: 1);

        var response = task.AbandonLease(dispatch.LeaseId, "connector-host-001", "host-shutdown", AuditId("audit-000003"), Now.AddSeconds(10));

        Assert.Equal("failed", response.Status);
        Assert.Equal("abandoned", Assert.Single(response.Attempts).Status);
    }

    [Fact]
    public void Result_context_must_match_active_attempt()
    {
        var task = CreateTask();
        var dispatch = Claim(task, "attempt-000001", "audit-000002", "lease-001", "connector-host-001");
        var mismatched = Result(dispatch, "succeeded") with
        {
            Context = new ConnectorRequestContext("1.0", "1.0", "corr-001", Now, "org-001", "env-dev", "connector-host-002")
        };

        Assert.Throws<InvalidOperationResultException>(() => task.RecordResult(mismatched, AuditId("audit-000003")));
        Assert.Equal("dispatched", task.ToFact().Status);
        Assert.Equal("started", Assert.Single(task.ToDetailFact().Attempts).Status);
    }

    private static OperationTask CreateTask()
    {
        var task = OperationTask.Create(TaskId(), CreateRequest("idem-001"), Template("lifecycle.restart"), Now);
        task.AssignInitialAuditId(AuditId());
        return task;
    }

    private static OperationTaskDispatchFact Claim(
        OperationTask task,
        string attemptId,
        string auditId,
        string leaseId,
        string connectorHostId,
        int maxAttempts = 3)
    {
        return task.Claim(
            AttemptId(attemptId),
            AuditId(auditId),
            leaseId,
            connectorHostId,
            Now.AddSeconds(1),
            TimeSpan.FromMinutes(5),
            maxAttempts);
    }

    private static CreateOperationTaskInput CreateRequest(string idempotencyKey)
    {
        return new CreateOperationTaskInput(
            "org-001",
            "env-dev",
            "demo-api-001",
            "lifecycle.restart",
            idempotencyKey,
            "local-admin",
            "corr-001",
            new Dictionary<string, string> { ["signal"] = "restart" });
    }

    private static OperationResultInput Result(OperationTaskDispatchFact dispatch, string executionStatus, OperationFailureFact? failure = null)
    {
        return new OperationResultInput(
            new ConnectorRequestContext(
                "1.0",
                "1.0",
                dispatch.CorrelationId,
                Now.AddSeconds(20),
                dispatch.OrganizationId,
                dispatch.EnvironmentId,
                dispatch.ConnectorHostId),
            dispatch.OperationTaskId,
            dispatch.AttemptId,
            dispatch.InstanceKey,
            dispatch.OperationCode,
            Now.AddSeconds(10),
            Now.AddSeconds(20),
            executionStatus,
            failure,
            new Dictionary<string, string>());
    }

    private static OperationFailureFact Failure(string code, string message)
    {
        return new OperationFailureFact(code, message, "connector", true, new Dictionary<string, string>());
    }

    private static OperationTaskId TaskId(string id = "op-000001") => new(id);
    private static OperationTemplateId TemplateId(string id = "opt-000001") => new(id);
    private static OperationAttemptId AttemptId(string id) => new(id);
    private static AuditRecordId AuditId(string id = "audit-000001") => new(id);
    private static OperationTemplateSnapshot Template(string operationCode, bool requiresApproval = false) =>
        new(operationCode, Enabled: true, DefaultMaxAttempts: 3, DefaultLeaseDurationSeconds: 300, RequiresApproval: requiresApproval);
}
