namespace Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.DeviceControlCommandAggregate;

public partial record DeviceControlCommandId : IGuidStronglyTypedId;

/// <summary>
/// Ledger projection of a device control command dispatched through Ops. The authoritative
/// execution record is the Ops operation task; this row keeps the IndustrialTelemetry-owned
/// business fact (which device/tag/value was commanded, by whom, and its dispatch-time status)
/// so the read-face can list command history and resolve a single command's context. It never
/// stores PLC/DCS/SCADA credentials or connection payloads.
/// </summary>
public sealed class DeviceControlCommand : Entity<DeviceControlCommandId>, IAggregateRoot
{
    private DeviceControlCommand()
    {
    }

    private DeviceControlCommand(
        string operationTaskId,
        string organizationId,
        string environmentId,
        string connectorHostId,
        string instanceKey,
        string deviceAssetId,
        string commandType,
        string? tagKey,
        string? value,
        string? parametersJson,
        string requestedBy,
        string reason,
        string idempotencyKey,
        string correlationId,
        string status,
        string? approvalStatus,
        DateTimeOffset requestedAtUtc)
    {
        Id = new DeviceControlCommandId(Guid.CreateVersion7());
        OperationTaskId = IndustrialTelemetryText.Required(operationTaskId, nameof(operationTaskId));
        OrganizationId = IndustrialTelemetryText.Required(organizationId, nameof(organizationId));
        EnvironmentId = IndustrialTelemetryText.Required(environmentId, nameof(environmentId));
        ConnectorHostId = IndustrialTelemetryText.Required(connectorHostId, nameof(connectorHostId));
        InstanceKey = IndustrialTelemetryText.Required(instanceKey, nameof(instanceKey));
        DeviceAssetId = IndustrialTelemetryText.Required(deviceAssetId, nameof(deviceAssetId));
        CommandType = IndustrialTelemetryText.RequiredLower(commandType, nameof(commandType));
        TagKey = IndustrialTelemetryText.Optional(tagKey)?.ToLowerInvariant();
        Value = IndustrialTelemetryText.Optional(value);
        ParametersJson = IndustrialTelemetryText.Optional(parametersJson);
        RequestedBy = IndustrialTelemetryText.Required(requestedBy, nameof(requestedBy));
        Reason = IndustrialTelemetryText.Required(reason, nameof(reason));
        IdempotencyKey = IndustrialTelemetryText.Required(idempotencyKey, nameof(idempotencyKey));
        CorrelationId = IndustrialTelemetryText.Required(correlationId, nameof(correlationId));
        Status = IndustrialTelemetryText.Required(status, nameof(status));
        ApprovalStatus = IndustrialTelemetryText.Optional(approvalStatus);
        RequestedAtUtc = requestedAtUtc;
        RequestedAtUnixTimeMilliseconds = requestedAtUtc.ToUnixTimeMilliseconds();
        RecordedAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>Stable Ops operation task identifier; also the external command id used by the read-face.</summary>
    public string OperationTaskId { get; private set; } = string.Empty;
    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string ConnectorHostId { get; private set; } = string.Empty;
    public string InstanceKey { get; private set; } = string.Empty;
    public string DeviceAssetId { get; private set; } = string.Empty;
    public string CommandType { get; private set; } = string.Empty;
    public string? TagKey { get; private set; }
    public string? Value { get; private set; }

    /// <summary>JSON object of the parameter-set command inputs (tag key to value); null for single-tag commands.</summary>
    public string? ParametersJson { get; private set; }
    public string RequestedBy { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string CorrelationId { get; private set; } = string.Empty;

    /// <summary>Dispatch-time Ops task status snapshot; the single-command read-face refreshes this live from Ops.</summary>
    public string Status { get; private set; } = string.Empty;

    /// <summary>Dispatch-time Ops approval status snapshot, when the command required approval.</summary>
    public string? ApprovalStatus { get; private set; }
    public DateTimeOffset RequestedAtUtc { get; private set; }

    /// <summary>Requested UTC time as Unix time milliseconds for provider-neutral history range filtering and ordering.</summary>
    public long RequestedAtUnixTimeMilliseconds { get; private set; }
    public DateTimeOffset RecordedAtUtc { get; private set; }

    /// <summary>UTC time the Ops task reached a terminal outcome (completed/failed/rejected); null while in flight.</summary>
    public DateTimeOffset? FinishedAtUtc { get; private set; }

    /// <summary>Machine-readable failure code from Ops when the command failed; null otherwise.</summary>
    public string? FailureCode { get; private set; }

    private static readonly string[] TerminalStatuses = ["completed", "failed", "rejected"];

    /// <summary>Whether the command has reached a terminal Ops outcome and must not transition again.</summary>
    public bool IsTerminal => TerminalStatuses.Contains(Status);

    /// <summary>
    /// Advance the ledger to a terminal Ops outcome (completed/failed/rejected) driven by Ops
    /// OperationTaskCompleted/Failed/ApprovalRejected events. Idempotent: the first terminal outcome
    /// wins, so duplicate or out-of-order delivery does not overwrite the recorded result.
    /// </summary>
    public void ApplyOpsOutcome(string terminalStatus, DateTimeOffset finishedAtUtc, string? failureCode)
    {
        var normalized = IndustrialTelemetryText.RequiredLower(terminalStatus, nameof(terminalStatus));
        if (!TerminalStatuses.Contains(normalized))
        {
            throw new ArgumentOutOfRangeException(nameof(terminalStatus), "Device control outcome must be completed, failed or rejected.");
        }

        if (IsTerminal)
        {
            return;
        }

        Status = normalized;
        FinishedAtUtc = finishedAtUtc;
        FailureCode = IndustrialTelemetryText.Optional(failureCode);
        ApprovalStatus = ResolveApprovalStatus(normalized, ApprovalStatus);
    }

    // Reflect the approval outcome on the ledger so the history read-face shows the real approval terminal
    // instead of the dispatch-time snapshot: a rejected task is approval-rejected; a task that reached
    // completed/failed must have cleared approval, so a still-pending snapshot becomes approved. An
    // auto-approved (not-required) or already-decided snapshot is left untouched.
    private static string? ResolveApprovalStatus(string terminalStatus, string? currentApprovalStatus)
    {
        if (terminalStatus == "rejected")
        {
            return "rejected";
        }

        return string.IsNullOrWhiteSpace(currentApprovalStatus)
            || string.Equals(currentApprovalStatus, "pending", StringComparison.OrdinalIgnoreCase)
            ? "approved"
            : currentApprovalStatus;
    }

    public static DeviceControlCommand Record(
        string operationTaskId,
        string organizationId,
        string environmentId,
        string connectorHostId,
        string instanceKey,
        string deviceAssetId,
        string commandType,
        string? tagKey,
        string? value,
        string? parametersJson,
        string requestedBy,
        string reason,
        string idempotencyKey,
        string correlationId,
        string status,
        string? approvalStatus,
        DateTimeOffset requestedAtUtc)
    {
        return new DeviceControlCommand(
            operationTaskId,
            organizationId,
            environmentId,
            connectorHostId,
            instanceKey,
            deviceAssetId,
            commandType,
            tagKey,
            value,
            parametersJson,
            requestedBy,
            reason,
            idempotencyKey,
            correlationId,
            status,
            approvalStatus,
            requestedAtUtc);
    }
}
