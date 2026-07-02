using System.Text.Json;
using Nerv.IIP.Contracts.Ops;

namespace Nerv.IIP.Contracts.Ops.Tests;

public sealed class OpsContractJsonTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Operation_task_response_round_trips_with_web_json_options()
    {
        var source = new OperationTaskResponse(
            "op-000001",
            "org-001",
            "env-dev",
            "docker-container-local-demo-001",
            "lifecycle.restart",
            "completed",
            "local-admin",
            DateTimeOffset.Parse("2026-05-15T00:00:00Z"),
            new OperationApprovalSummary(
                "approved",
                "local-admin",
                DateTimeOffset.Parse("2026-05-15T00:00:00Z"),
                "ops-approver",
                DateTimeOffset.Parse("2026-05-15T00:00:30Z"),
                "approved"),
            "attempt-000001",
            [new OperationAttemptSummary(
                "attempt-000001",
                "completed",
                DateTimeOffset.Parse("2026-05-15T00:00:01Z"),
                DateTimeOffset.Parse("2026-05-15T00:00:02Z"),
                null,
                "lease-000001",
                DateTimeOffset.Parse("2026-05-15T00:00:01Z"),
                DateTimeOffset.Parse("2026-05-15T00:05:01Z"),
                1,
                300,
                3,
                null)],
            [new AuditRecordSummary("audit-000001", "op-000001", 1, "", "operation.completed", "connector-host-001", DateTimeOffset.Parse("2026-05-15T00:00:02Z"), "corr-ops-001", "sha256:contract-test")]);

        var json = JsonSerializer.Serialize(source, JsonOptions);
        var result = JsonSerializer.Deserialize<OperationTaskResponse>(json, JsonOptions);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.True(root.TryGetProperty("operationTaskId", out var operationTaskId));
        Assert.Equal("op-000001", operationTaskId.GetString());
        Assert.True(root.TryGetProperty("approval", out var approval));
        Assert.Equal("approved", approval.GetProperty("status").GetString());
        Assert.True(root.TryGetProperty("attempts", out var attempts));
        Assert.Equal(JsonValueKind.Array, attempts.ValueKind);
        Assert.True(attempts[0].TryGetProperty("failureCode", out var failureCode));
        Assert.Equal(JsonValueKind.Null, failureCode.ValueKind);
        Assert.True(attempts[0].TryGetProperty("leaseId", out var leaseId));
        Assert.Equal("lease-000001", leaseId.GetString());
        Assert.True(attempts[0].TryGetProperty("attemptNo", out var attemptNo));
        Assert.Equal(1, attemptNo.GetInt32());
        Assert.True(root.TryGetProperty("auditRecords", out var auditRecords));
        Assert.Equal(JsonValueKind.Array, auditRecords.ValueKind);
        Assert.Equal(1, auditRecords[0].GetProperty("sequenceNo").GetInt64());
        Assert.Equal("", auditRecords[0].GetProperty("previousIntegrityHash").GetString());

        Assert.NotNull(result);
        Assert.Equal("op-000001", result.OperationTaskId);
        Assert.Equal("completed", result.Status);
        Assert.Equal("approved", result.Approval?.Status);
        Assert.Null(result.Attempts.Single().FailureCode);
        Assert.Equal("operation.completed", result.AuditRecords.Single().Action);
    }

    [Fact]
    public void Audit_intent_contracts_round_trip_with_web_json_options()
    {
        var request = new SubmitAuditIntentRequest(
            "org-001",
            "env-dev",
            "op-000001",
            "manual.reviewed",
            "user:auditor",
            "corr-audit-001");
        var response = new AuditIntentResponse(
            "audit-000002",
            "op-000001",
            2,
            "sha256:previous",
            "manual.reviewed",
            "user:auditor",
            DateTimeOffset.Parse("2026-05-22T00:00:00Z"),
            "corr-audit-001",
            "sha256:contract-test");

        var requestJson = JsonSerializer.Serialize(request, JsonOptions);
        var responseJson = JsonSerializer.Serialize(response, JsonOptions);
        var requestResult = JsonSerializer.Deserialize<SubmitAuditIntentRequest>(requestJson, JsonOptions);
        var responseResult = JsonSerializer.Deserialize<AuditIntentResponse>(responseJson, JsonOptions);

        using var requestDocument = JsonDocument.Parse(requestJson);
        using var responseDocument = JsonDocument.Parse(responseJson);

        Assert.Equal("op-000001", requestDocument.RootElement.GetProperty("operationTaskId").GetString());
        Assert.Equal("manual.reviewed", requestDocument.RootElement.GetProperty("action").GetString());
        Assert.Equal("audit-000002", responseDocument.RootElement.GetProperty("auditRecordId").GetString());
        Assert.Equal(2, responseDocument.RootElement.GetProperty("sequenceNo").GetInt64());
        Assert.Equal("sha256:previous", responseDocument.RootElement.GetProperty("previousIntegrityHash").GetString());
        Assert.Equal("corr-audit-001", responseDocument.RootElement.GetProperty("correlationId").GetString());
        Assert.Equal("sha256:contract-test", responseDocument.RootElement.GetProperty("integrityHash").GetString());
        Assert.NotNull(requestResult);
        Assert.Equal("user:auditor", requestResult.Actor);
        Assert.NotNull(responseResult);
        Assert.Equal("manual.reviewed", responseResult.Action);
    }

    [Fact]
    public void Operation_task_completed_integration_event_round_trips_with_envelope_fields()
    {
        var source = new OperationTaskCompletedIntegrationEvent(
            "evt-ops-completed-001",
            "ops.OperationTaskCompleted",
            1,
            DateTimeOffset.Parse("2026-05-15T00:00:02Z"),
            "ops",
            "corr-ops-001",
            "op-000001",
            "org-001",
            "env-dev",
            "connector-host-001",
            "ops:operation-task-completed:op-000001:attempt-000001",
            new OperationTaskCompletedPayload(
                "op-000001",
                "attempt-000001",
                "docker-container-local-demo-001",
                "lifecycle.restart",
                DateTimeOffset.Parse("2026-05-15T00:00:02Z")));

        var json = JsonSerializer.Serialize(source, JsonOptions);
        var result = JsonSerializer.Deserialize<OperationTaskCompletedIntegrationEvent>(json, JsonOptions);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("ops.OperationTaskCompleted", root.GetProperty("eventType").GetString());
        Assert.Equal("ops:operation-task-completed:op-000001:attempt-000001", root.GetProperty("idempotencyKey").GetString());
        Assert.Equal("docker-container-local-demo-001", root.GetProperty("payload").GetProperty("instanceKey").GetString());
        Assert.NotNull(result);
        Assert.Equal("op-000001", result.Payload.OperationTaskId);
    }

    [Fact]
    public void Operation_task_requested_integration_event_round_trips_with_request_payload()
    {
        var source = new OperationTaskRequestedIntegrationEvent(
            "evt-ops-requested-001",
            "ops.OperationTaskRequested",
            1,
            DateTimeOffset.Parse("2026-05-15T00:00:00Z"),
            "ops",
            "corr-ops-001",
            "op-000001",
            "org-001",
            "env-dev",
            "local-admin",
            "ops:operation-task-requested:op-000001",
            new OperationTaskRequestedPayload(
                "op-000001",
                "docker-container-local-demo-001",
                "lifecycle.restart",
                "local-admin",
                DateTimeOffset.Parse("2026-05-15T00:00:00Z")));

        var json = JsonSerializer.Serialize(source, JsonOptions);
        var result = JsonSerializer.Deserialize<OperationTaskRequestedIntegrationEvent>(json, JsonOptions);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("ops.OperationTaskRequested", root.GetProperty("eventType").GetString());
        Assert.Equal(1, root.GetProperty("eventVersion").GetInt32());
        Assert.Equal("ops:operation-task-requested:op-000001", root.GetProperty("idempotencyKey").GetString());
        Assert.Equal("local-admin", root.GetProperty("payload").GetProperty("requestedBy").GetString());
        Assert.NotNull(result);
        Assert.Equal("op-000001", result.Payload.OperationTaskId);
    }

    [Fact]
    public void Operation_task_claimed_integration_event_round_trips_with_lease_payload()
    {
        var source = new OperationTaskClaimedIntegrationEvent(
            "evt-ops-claimed-001",
            "ops.OperationTaskClaimed",
            1,
            DateTimeOffset.Parse("2026-05-15T00:00:01Z"),
            "ops",
            "corr-ops-001",
            "op-000001",
            "org-001",
            "env-dev",
            "connector-host-001",
            "ops:operation-task-claimed:op-000001:attempt-000001",
            new OperationTaskClaimedPayload(
                "op-000001",
                "attempt-000001",
                "docker-container-local-demo-001",
                "lifecycle.restart",
                "lease-000001",
                DateTimeOffset.Parse("2026-05-15T00:00:01Z"),
                DateTimeOffset.Parse("2026-05-15T00:05:01Z"),
                1,
                3));

        var json = JsonSerializer.Serialize(source, JsonOptions);
        var result = JsonSerializer.Deserialize<OperationTaskClaimedIntegrationEvent>(json, JsonOptions);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("ops.OperationTaskClaimed", root.GetProperty("eventType").GetString());
        Assert.Equal("connector-host-001", root.GetProperty("actor").GetString());
        Assert.Equal("lease-000001", root.GetProperty("payload").GetProperty("leaseId").GetString());
        Assert.Equal(1, root.GetProperty("payload").GetProperty("attemptNo").GetInt32());
        Assert.NotNull(result);
        Assert.Equal("attempt-000001", result.Payload.AttemptId);
    }

    [Fact]
    public void Audit_recorded_integration_event_round_trips_with_audit_payload()
    {
        var source = new AuditRecordedIntegrationEvent(
            "evt-ops-audit-001",
            "ops.AuditRecorded",
            1,
            DateTimeOffset.Parse("2026-05-15T00:00:02Z"),
            "ops",
            "corr-ops-001",
            "op-000001",
            "org-001",
            "env-dev",
            "connector-host-001",
            "ops:audit-recorded:audit-000001",
            new AuditRecordedPayload(
                "audit-000001",
                "op-000001",
                "operation.completed",
                "connector-host-001",
                DateTimeOffset.Parse("2026-05-15T00:00:02Z")));

        var json = JsonSerializer.Serialize(source, JsonOptions);
        var result = JsonSerializer.Deserialize<AuditRecordedIntegrationEvent>(json, JsonOptions);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("ops.AuditRecorded", root.GetProperty("eventType").GetString());
        Assert.Equal("ops:audit-recorded:audit-000001", root.GetProperty("idempotencyKey").GetString());
        Assert.Equal("operation.completed", root.GetProperty("payload").GetProperty("action").GetString());
        Assert.NotNull(result);
        Assert.Equal("audit-000001", result.Payload.AuditRecordId);
    }

    [Fact]
    public void Operation_task_failed_integration_event_round_trips_with_failure_payload()
    {
        var source = new OperationTaskFailedIntegrationEvent(
            "evt-ops-failed-001",
            "ops.OperationTaskFailed",
            1,
            DateTimeOffset.Parse("2026-05-15T00:00:02Z"),
            "ops",
            "corr-ops-001",
            "op-000001",
            "org-001",
            "env-dev",
            "connector-host-001",
            "ops:operation-task-failed:op-000001:attempt-000001",
            new OperationTaskFailedPayload(
                "op-000001",
                "attempt-000001",
                "docker-container-local-demo-001",
                "lifecycle.restart",
                DateTimeOffset.Parse("2026-05-15T00:00:02Z"),
                "container-exited"));

        var json = JsonSerializer.Serialize(source, JsonOptions);
        var result = JsonSerializer.Deserialize<OperationTaskFailedIntegrationEvent>(json, JsonOptions);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("ops.OperationTaskFailed", root.GetProperty("eventType").GetString());
        Assert.Equal("container-exited", root.GetProperty("payload").GetProperty("failureCode").GetString());
        Assert.NotNull(result);
        Assert.Equal("container-exited", result.Payload.FailureCode);
    }

    [Fact]
    public void Operation_approval_events_round_trip_with_structured_payload()
    {
        var requested = new OperationApprovalRequestedIntegrationEvent(
            "evt-approval-requested-001",
            "ops.OperationApprovalRequested",
            1,
            DateTimeOffset.Parse("2026-05-26T09:00:00Z"),
            "ops",
            "corr-approval",
            "op-000001",
            "org-001",
            "env-dev",
            "local-admin",
            "ops:operation-approval-requested:op-000001",
            new OperationApprovalRequestedPayload(
                "op-000001",
                "docker-container-local-demo-001",
                "lifecycle.high-risk-restart",
                "local-admin",
                DateTimeOffset.Parse("2026-05-26T09:00:00Z")));
        var approved = new OperationApprovalApprovedIntegrationEvent(
            "evt-approval-approved-001",
            "ops.OperationApprovalApproved",
            1,
            DateTimeOffset.Parse("2026-05-26T09:01:00Z"),
            "ops",
            "corr-approval",
            "op-000001",
            "org-001",
            "env-dev",
            "ops-approver",
            "ops:operation-approval-approved:op-000001",
            new OperationApprovalDecidedPayload(
                "op-000001",
                "docker-container-local-demo-001",
                "lifecycle.high-risk-restart",
                "ops-approver",
                "approved",
                DateTimeOffset.Parse("2026-05-26T09:01:00Z")));

        var requestedResult = JsonSerializer.Deserialize<OperationApprovalRequestedIntegrationEvent>(
            JsonSerializer.Serialize(requested, JsonOptions),
            JsonOptions);
        var approvedResult = JsonSerializer.Deserialize<OperationApprovalApprovedIntegrationEvent>(
            JsonSerializer.Serialize(approved, JsonOptions),
            JsonOptions);

        Assert.NotNull(requestedResult);
        Assert.NotNull(approvedResult);
        Assert.Equal("ops.OperationApprovalRequested", requestedResult.EventType);
        Assert.Equal("op-000001", requestedResult.Payload.OperationTaskId);
        Assert.Equal("ops.OperationApprovalApproved", approvedResult.EventType);
        Assert.Equal("ops-approver", approvedResult.Payload.DecidedBy);
    }
}
