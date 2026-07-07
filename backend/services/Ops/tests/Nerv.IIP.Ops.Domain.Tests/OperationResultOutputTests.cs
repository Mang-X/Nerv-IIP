using Nerv.IIP.Contracts.ConnectorProtocol;
using Nerv.IIP.Ops.Domain;
using Nerv.IIP.Ops.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Ops.Domain.AggregatesModel.OperationTemplateAggregate;

namespace Nerv.IIP.Ops.Domain.Tests;

public sealed class OperationResultOutputTests
{
    [Fact]
    public void Record_result_persists_connector_output_for_device_receipt_audit()
    {
        var task = OperationTask.Create(
            new OperationTaskId("op-000001"),
            new CreateOperationTaskInput(
                "org-001",
                "env-dev",
                "opcua-line1",
                "device.control.command",
                "idem-001",
                "user:operator-001",
                "corr-device-control",
                new Dictionary<string, string>
                {
                    ["commandType"] = "write-tag",
                    ["deviceAssetId"] = "DEV-CNC-01",
                    ["tagKey"] = "spindle.speed",
                    ["value"] = "80"
                }),
            OperationTemplate.CreateSnapshot("device.control.command", enabled: true, 1, 300, requiresApproval: false),
            DateTimeOffset.Parse("2026-07-07T00:00:00Z"));
        task.AssignPendingAuditIds([new AuditRecordId("audit-000001")]);
        var dispatch = task.Claim(
            new OperationAttemptId("attempt-000001"),
            new AuditRecordId("audit-000002"),
            "lease-000001",
            "connector-host-001",
            DateTimeOffset.Parse("2026-07-07T00:01:00Z"),
            TimeSpan.FromMinutes(5),
            1);

        task.RecordResult(
            new OperationResultInput(
                new ConnectorRequestContext("1.0", "1.0", "corr-device-control", DateTimeOffset.Parse("2026-07-07T00:02:00Z"), "org-001", "env-dev", "connector-host-001"),
                dispatch.OperationTaskId,
                dispatch.AttemptId,
                dispatch.InstanceKey,
                dispatch.OperationCode,
                DateTimeOffset.Parse("2026-07-07T00:01:00Z"),
                DateTimeOffset.Parse("2026-07-07T00:02:00Z"),
                "succeeded",
                null,
                new Dictionary<string, string>
                {
                    ["deviceReceiptCode"] = "opcua.write.accepted",
                    ["nodeId"] = "ns=2;s=Line1.SpindleSpeed",
                    ["writtenValue"] = "80"
                }),
            new AuditRecordId("audit-000003"));

        var attempt = Assert.Single(task.ToDetailFact().Attempts);
        Assert.Equal("opcua.write.accepted", attempt.Output["deviceReceiptCode"]);
        Assert.Equal("80", attempt.Output["writtenValue"]);
        Assert.Contains(task.ToDetailFact().AuditRecords, x => x.Action == "operation.completed");
        Assert.Equal("80", task.ToDetailFact().Task.Parameters["value"]);
    }

    [Fact]
    public void Record_failed_result_persists_device_receipt_output_for_failure_audit()
    {
        var task = OperationTask.Create(
            new OperationTaskId("op-000002"),
            new CreateOperationTaskInput(
                "org-001",
                "env-dev",
                "opcua-line1",
                "device.control.command",
                "idem-002",
                "user:operator-001",
                "corr-device-control-failed",
                new Dictionary<string, string>
                {
                    ["commandType"] = "write-tag",
                    ["deviceAssetId"] = "DEV-CNC-01",
                    ["tagKey"] = "spindle.speed",
                    ["value"] = "999"
                }),
            OperationTemplate.CreateSnapshot("device.control.command", enabled: true, 1, 300, requiresApproval: false),
            DateTimeOffset.Parse("2026-07-07T00:00:00Z"));
        task.AssignPendingAuditIds([new AuditRecordId("audit-000011")]);
        var dispatch = task.Claim(
            new OperationAttemptId("attempt-000002"),
            new AuditRecordId("audit-000012"),
            "lease-000002",
            "connector-host-001",
            DateTimeOffset.Parse("2026-07-07T00:01:00Z"),
            TimeSpan.FromMinutes(5),
            1);

        task.RecordResult(
            new OperationResultInput(
                new ConnectorRequestContext("1.0", "1.0", "corr-device-control-failed", DateTimeOffset.Parse("2026-07-07T00:02:00Z"), "org-001", "env-dev", "connector-host-001"),
                dispatch.OperationTaskId,
                dispatch.AttemptId,
                dispatch.InstanceKey,
                dispatch.OperationCode,
                DateTimeOffset.Parse("2026-07-07T00:01:00Z"),
                DateTimeOffset.Parse("2026-07-07T00:02:00Z"),
                "failed",
                new OperationFailureFact("opcua.write.rejected", "Device rejected value.", "runtime", false, new Dictionary<string, string>()),
                new Dictionary<string, string>
                {
                    ["deviceReceiptCode"] = "BadOutOfRange",
                    ["deviceReceiptMessage"] = "Value is outside PLC range.",
                    ["writtenValue"] = "999"
                }),
            new AuditRecordId("audit-000013"));

        var detail = task.ToDetailFact();
        var attempt = Assert.Single(detail.Attempts);
        Assert.Equal("failed", attempt.Status);
        Assert.Equal("BadOutOfRange", attempt.Output["deviceReceiptCode"]);
        Assert.Equal("999", attempt.Output["writtenValue"]);
        Assert.Contains(detail.AuditRecords, x => x.Action == "operation.failed");
    }
}
