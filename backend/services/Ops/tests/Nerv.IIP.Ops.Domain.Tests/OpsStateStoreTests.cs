using Nerv.IIP.Contracts.ConnectorProtocol;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Ops.Domain;

namespace Nerv.IIP.Ops.Domain.Tests;

public sealed class OpsStateStoreTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-05-14T00:00:00Z");

    [Fact]
    public void Idempotency_key_returns_existing_task_without_creating_duplicate()
    {
        var store = new InMemoryOpsStateStore();
        var request = CreateRequest("idem-001");

        var first = store.Create(request, Now);
        var duplicate = store.Create(request, Now.AddSeconds(1));

        Assert.Equal(first.OperationTaskId, duplicate.OperationTaskId);
        Assert.Equal("queued", duplicate.Status);
        Assert.Single(duplicate.AuditRecords);
    }

    [Fact]
    public void Claiming_pending_task_prevents_another_host_from_claiming_same_task()
    {
        var store = new InMemoryOpsStateStore();
        var created = store.Create(CreateRequest("idem-001"), Now);

        var first = store.ClaimPending(new ClaimOperationTasksRequest("org-001", "env-dev", "connector-host-001", 1), Now.AddSeconds(1));
        var second = store.ClaimPending(new ClaimOperationTasksRequest("org-001", "env-dev", "connector-host-002", 1), Now.AddSeconds(2));
        var task = store.Get(created.OperationTaskId);

        var dispatch = Assert.Single(first.Items);
        Assert.Equal(created.OperationTaskId, dispatch.OperationTaskId);
        Assert.Empty(second.Items);
        Assert.Equal("dispatched", task.Status);
        Assert.Equal("connector-host-001", dispatch.ConnectorHostId);
        Assert.Equal("started", Assert.Single(task.Attempts).Status);
    }

    [Fact]
    public void Expired_claim_is_requeued_and_can_be_retried_by_another_host()
    {
        var store = new InMemoryOpsStateStore();
        store.Create(CreateRequest("idem-001"), Now);
        var first = Assert.Single(store.ClaimPending(new ClaimOperationTasksRequest("org-001", "env-dev", "connector-host-001", 1, LeaseDurationSeconds: 30, MaxAttempts: 2), Now.AddSeconds(1)).Items);

        var retried = store.ClaimPending(new ClaimOperationTasksRequest("org-001", "env-dev", "connector-host-002", 1, LeaseDurationSeconds: 30, MaxAttempts: 2), first.LeasedUntilUtc.AddSeconds(1));

        var second = Assert.Single(retried.Items);
        Assert.Equal(2, second.AttemptNo);
        Assert.Equal("connector-host-002", second.ConnectorHostId);
    }

    [Fact]
    public void Claimed_task_uses_template_attempt_and_lease_defaults()
    {
        var store = new InMemoryOpsStateStore();
        store.CreateTemplate(
            new CreateOperationTemplateRequest(
                "backup.snapshot",
                "Backup snapshot",
                "{}",
                "medium",
                4,
                900,
                RequiresApproval: false),
            Now);
        store.Create(CreateRequest("idem-template-defaults") with { OperationCode = "backup.snapshot" }, Now);

        var dispatch = Assert.Single(store.ClaimPending(
            new ClaimOperationTasksRequest("org-001", "env-dev", "connector-host-001", 1, LeaseDurationSeconds: 30, MaxAttempts: 1),
            Now.AddSeconds(1)).Items);

        Assert.Equal(4, dispatch.MaxAttempts);
        Assert.Equal(900, dispatch.LeaseDurationSeconds);
        Assert.Equal(Now.AddSeconds(901), dispatch.LeasedUntilUtc);
    }

    [Fact]
    public void Template_creation_normalizes_operation_code_before_duplicate_check()
    {
        var store = new InMemoryOpsStateStore();
        store.CreateTemplate(new CreateOperationTemplateRequest("backup.snapshot", "Backup", "{}", "low", 3, 300, false), Now);

        var duplicate = () => store.CreateTemplate(
            new CreateOperationTemplateRequest(" backup.snapshot ", "Backup", "{}", "low", 3, 300, false),
            Now);

        var ex = Assert.Throws<InvalidOperationTaskRequestException>(duplicate);
        Assert.Contains("already exists", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Result_with_wrong_connector_host_is_rejected_without_mutating_task()
    {
        var store = new InMemoryOpsStateStore();
        var created = store.Create(CreateRequest("idem-001"), Now);
        var dispatch = Assert.Single(store.ClaimPending(new ClaimOperationTasksRequest("org-001", "env-dev", "connector-host-001", 1), Now.AddSeconds(1)).Items);
        var mismatchedResult = Result(dispatch) with
        {
            Context = new ConnectorRequestContext("1.0", "1.0", "corr-001", Now.AddSeconds(20), "org-001", "env-dev", "connector-host-002")
        };

        Assert.Throws<InvalidOperationResultException>(() => store.RecordResult(mismatchedResult));
        var task = store.Get(created.OperationTaskId);
        Assert.Equal("dispatched", task.Status);
        Assert.Equal("started", Assert.Single(task.Attempts).Status);
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
            new Dictionary<string, string>());
    }

    private static OperationResult Result(OperationTaskDispatchItem dispatch)
    {
        return new OperationResult(
            new ConnectorRequestContext("1.0", "1.0", dispatch.CorrelationId, Now.AddSeconds(20), dispatch.OrganizationId, dispatch.EnvironmentId, dispatch.ConnectorHostId),
            dispatch.OperationTaskId,
            dispatch.AttemptId,
            dispatch.InstanceKey,
            dispatch.OperationCode,
            Now.AddSeconds(10),
            Now.AddSeconds(20),
            "succeeded",
            null,
            new Dictionary<string, string>());
    }
}
