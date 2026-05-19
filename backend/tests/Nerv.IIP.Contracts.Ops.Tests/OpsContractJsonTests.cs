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
                3,
                null)],
            [new AuditRecordSummary("audit-000001", "op-000001", "operation.completed", "connector-host-001", DateTimeOffset.Parse("2026-05-15T00:00:02Z"), "corr-ops-001")]);

        var json = JsonSerializer.Serialize(source, JsonOptions);
        var result = JsonSerializer.Deserialize<OperationTaskResponse>(json, JsonOptions);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.True(root.TryGetProperty("operationTaskId", out var operationTaskId));
        Assert.Equal("op-000001", operationTaskId.GetString());
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

        Assert.NotNull(result);
        Assert.Equal("op-000001", result.OperationTaskId);
        Assert.Equal("completed", result.Status);
        Assert.Null(result.Attempts.Single().FailureCode);
        Assert.Equal("operation.completed", result.AuditRecords.Single().Action);
    }
}
