using Nerv.IIP.Contracts.ConnectorProtocol;
using Nerv.IIP.Contracts.Ops;
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
        var response = task.ToResponse();

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
        var attempt = Assert.Single(task.ToResponse().Attempts);
        Assert.Equal("started", attempt.Status);
        Assert.Throws<InvalidOperationResultException>(secondClaim);
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
    public void Abandoned_claim_can_be_retried_until_max_attempts()
    {
        var task = CreateTask();
        var firstDispatch = Claim(task, "attempt-000001", "audit-000002", "lease-001", "connector-host-001", maxAttempts: 2);

        task.AbandonLease(firstDispatch.LeaseId, "connector-host-001", "host-shutdown", AuditId("audit-000003"), Now.AddSeconds(10));
        var secondDispatch = Claim(task, "attempt-000002", "audit-000004", "lease-002", "connector-host-002", maxAttempts: 2);

        Assert.Equal("dispatched", task.ToFact().Status);
        Assert.Equal(2, secondDispatch.AttemptNo);
        Assert.Equal("connector-host-002", secondDispatch.ConnectorHostId);
        Assert.Equal(["abandoned", "started"], task.ToResponse().Attempts.Select(x => x.Status));
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
        Assert.Equal("started", Assert.Single(task.ToResponse().Attempts).Status);
    }

    private static OperationTask CreateTask()
    {
        var task = OperationTask.Create(TaskId(), CreateRequest("idem-001"), Template("lifecycle.restart"), Now);
        task.AssignInitialAuditId(AuditId());
        return task;
    }

    private static OperationTaskDispatchItem Claim(
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

    private static CreateOperationTaskRequest CreateRequest(string idempotencyKey)
    {
        return new CreateOperationTaskRequest(
            "org-001",
            "env-dev",
            "demo-api-001",
            "lifecycle.restart",
            idempotencyKey,
            "local-admin",
            "manual restart",
            "corr-001",
            new Dictionary<string, string> { ["signal"] = "restart" });
    }

    private static OperationResult Result(OperationTaskDispatchItem dispatch, string executionStatus, FailureReason? failure = null)
    {
        return new OperationResult(
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

    private static FailureReason Failure(string code, string message)
    {
        return new FailureReason(code, message, "connector", true, new Dictionary<string, string>());
    }

    private static OperationTaskId TaskId(string id = "op-000001") => new(id);
    private static OperationTemplateId TemplateId(string id = "opt-000001") => new(id);
    private static OperationAttemptId AttemptId(string id) => new(id);
    private static AuditRecordId AuditId(string id = "audit-000001") => new(id);
    private static OperationTemplateSnapshot Template(string operationCode) =>
        new(operationCode, Enabled: true, DefaultMaxAttempts: 3, DefaultLeaseDurationSeconds: 300, RequiresApproval: false);
}
