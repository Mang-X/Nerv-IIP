using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceInspectionAggregate;
using Nerv.IIP.Business.Maintenance.Domain.DomainEvents;

namespace Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;

public partial record MaintenanceWorkOrderId : IGuidStronglyTypedId;

public partial record SparePartLineId : IGuidStronglyTypedId;

public partial record MaintenanceWorkOrderLifecycleEventId : IGuidStronglyTypedId;

public enum MaintenanceWorkOrderAction
{
    Assign = 0,
    Accept = 1,
    Start = 2,
    Pause = 3,
    WaitForParts = 4,
    Resume = 5,
    Complete = 6,
    Verify = 7,
    Close = 8,
    Cancel = 9,
}

public enum MaintenanceWorkOrderStatus
{
    Open = 0,
    Completed = 1,
    Accepted = 2,
    InProgress = 3,
    Paused = 4,
    WaitingForParts = 5,
    Verified = 6,
    Closed = 7,
    Cancelled = 8,
}

public static class MaintenanceWorkOrderSourceTypes
{
    public const string Manual = "manual";
    public const string Alarm = "alarm";
    public const string Plan = "plan";
    public const string Inspection = "inspection";
}

public static class MaintenanceWorkOrderSourceActors
{
    public const string Inspection = "maintenanceInspection";
}

public sealed record SparePartLineDraft(string SkuCode, decimal Quantity, string? UomCode = null);

public sealed class MaintenanceWorkOrder : Entity<MaintenanceWorkOrderId>, IAggregateRoot
{
    private readonly List<SparePartLine> sparePartLines = [];

    private MaintenanceWorkOrder()
    {
    }

    private MaintenanceWorkOrder(
        string organizationId,
        string environmentId,
        string deviceAssetId,
        string priority,
        string? sourceAlarmId,
        string openedBy,
        string? sourcePlanCode = null,
        string? sourceType = null,
        string? sourceReferenceId = null,
        string? diagnosticDescription = null,
        string? failureModeCode = null,
        string? failureCauseCode = null,
        string? assignedTechnicianUserId = null,
        int? estimatedLaborMinutes = null)
    {
        Id = new MaintenanceWorkOrderId(Guid.CreateVersion7());
        OrganizationId = MaintenanceText.Required(organizationId, nameof(organizationId));
        EnvironmentId = MaintenanceText.Required(environmentId, nameof(environmentId));
        DeviceAssetId = MaintenanceText.Required(deviceAssetId, nameof(deviceAssetId));
        Priority = MaintenanceText.Required(priority, nameof(priority)).ToLowerInvariant();
        SourceAlarmId = MaintenanceText.Optional(sourceAlarmId);
        SourcePlanCode = MaintenanceText.Optional(sourcePlanCode);
        SourceType = MaintenanceText.Optional(sourceType);
        SourceReferenceId = MaintenanceText.Optional(sourceReferenceId);
        DiagnosticDescription = MaintenanceText.Optional(diagnosticDescription);
        FailureModeCode = MaintenanceText.Optional(failureModeCode);
        FailureCauseCode = MaintenanceText.Optional(failureCauseCode);
        AssignedTechnicianUserId = MaintenanceText.Optional(assignedTechnicianUserId);
        EstimatedLaborMinutes = estimatedLaborMinutes is null ? null : MaintenanceText.Positive(estimatedLaborMinutes.Value, nameof(estimatedLaborMinutes));
        OpenedBy = MaintenanceText.Required(openedBy, nameof(openedBy));
        Status = MaintenanceWorkOrderStatus.Open;
        OpenedAtUtc = DateTimeOffset.UtcNow;
        this.AddDomainEvent(new MaintenanceWorkOrderOpenedDomainEvent(this));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string DeviceAssetId { get; private set; } = string.Empty;
    public string Priority { get; private set; } = string.Empty;
    public string? SourceAlarmId { get; private set; }
    public string? SourcePlanCode { get; private set; }
    public string? SourceType { get; private set; }
    public string? SourceReferenceId { get; private set; }
    public string? DiagnosticDescription { get; private set; }
    public string? FailureModeCode { get; private set; }
    public string? FailureCauseCode { get; private set; }
    public string? AssignedTechnicianUserId { get; private set; }
    public string? AssignedTeamId { get; private set; }
    public string? ActualTechnicianUserId { get; private set; }
    public int? EstimatedLaborMinutes { get; private set; }
    public string OpenedBy { get; private set; } = string.Empty;
    public MaintenanceWorkOrderStatus Status { get; private set; }
    public DateTimeOffset OpenedAtUtc { get; private set; }
    public bool AlarmCleared { get; private set; }
    public DateTimeOffset? AlarmClearedAtUtc { get; private set; }
    public bool AssetUnavailable { get; private set; }
    public string? AssetUnavailableReason { get; private set; }
    public DateTimeOffset? AssetUnavailableFromUtc { get; private set; }
    public string? CompletionResult { get; private set; }
    public string? DowntimeReasonCode { get; private set; }
    public int? DowntimeMinutes { get; private set; }
    public int? ActualLaborMinutes { get; private set; }
    public decimal? SparePartCostAmount { get; private set; }
    public decimal? ExternalServiceCostAmount { get; private set; }
    public string? CostCurrencyCode { get; private set; }
    public DateTimeOffset? RepairStartedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public DateTimeOffset? AcceptedAtUtc { get; private set; }
    public DateTimeOffset? VerifiedAtUtc { get; private set; }
    public DateTimeOffset? ClosedAtUtc { get; private set; }
    public DateTimeOffset? CancelledAtUtc { get; private set; }
    public int Version { get; private set; }
    public IReadOnlyCollection<SparePartLine> SparePartLines => sparePartLines;

    public static MaintenanceWorkOrder OpenManual(
        string organizationId,
        string environmentId,
        string deviceAssetId,
        string priority,
        string openedBy,
        string? assignedTechnicianUserId = null,
        int? estimatedLaborMinutes = null)
    {
        return new MaintenanceWorkOrder(
            organizationId,
            environmentId,
            deviceAssetId,
            priority,
            null,
            openedBy,
            sourceType: MaintenanceWorkOrderSourceTypes.Manual,
            sourceReferenceId: null,
            assignedTechnicianUserId: assignedTechnicianUserId,
            estimatedLaborMinutes: estimatedLaborMinutes);
    }

    public static MaintenanceWorkOrder OpenFromPlan(
        string organizationId,
        string environmentId,
        string deviceAssetId,
        string planCode,
        string openedBy,
        string? sourceReferenceId = null)
    {
        var normalizedPlanCode = MaintenanceText.Required(planCode, nameof(planCode));
        return new MaintenanceWorkOrder(
            organizationId,
            environmentId,
            deviceAssetId,
            "planned",
            null,
            openedBy,
            normalizedPlanCode,
            sourceType: MaintenanceWorkOrderSourceTypes.Plan,
            sourceReferenceId: sourceReferenceId ?? normalizedPlanCode);
    }

    public static MaintenanceWorkOrder OpenFromAlarm(
        string organizationId,
        string environmentId,
        string deviceAssetId,
        string sourceAlarmId,
        string priority,
        string openedBy = "industrialTelemetry",
        string? diagnosticDescription = null,
        string? failureModeCode = null,
        string? failureCauseCode = null,
        string? assignedTechnicianUserId = null,
        int? estimatedLaborMinutes = null,
        string? sourceReferenceId = null)
    {
        var normalizedAlarmId = MaintenanceText.Required(sourceAlarmId, nameof(sourceAlarmId));
        return new MaintenanceWorkOrder(
            organizationId,
            environmentId,
            deviceAssetId,
            priority,
            normalizedAlarmId,
            openedBy,
            sourceType: MaintenanceWorkOrderSourceTypes.Alarm,
            sourceReferenceId: sourceReferenceId ?? normalizedAlarmId,
            diagnosticDescription: diagnosticDescription,
            failureModeCode: failureModeCode,
            failureCauseCode: failureCauseCode,
            assignedTechnicianUserId: assignedTechnicianUserId,
            estimatedLaborMinutes: estimatedLaborMinutes);
    }

    public static MaintenanceWorkOrder OpenFromInspection(
        string organizationId,
        string environmentId,
        string deviceAssetId,
        MaintenanceInspectionId inspectionId,
        string result,
        string openedBy = MaintenanceWorkOrderSourceActors.Inspection)
    {
        var diagnosticDescription = $"Maintenance inspection failed: {MaintenanceText.Required(result, nameof(result))}";
        return new MaintenanceWorkOrder(
            organizationId,
            environmentId,
            deviceAssetId,
            "high",
            null,
            openedBy,
            sourceType: MaintenanceWorkOrderSourceTypes.Inspection,
            sourceReferenceId: inspectionId.ToString(),
            diagnosticDescription: diagnosticDescription,
            failureModeCode: "inspection-failed",
            failureCauseCode: "inspection");
    }

    public void MarkRepairStarted(DateTimeOffset repairStartedAtUtc)
    {
        EnsureOpen();
        if (RepairStartedAtUtc is not null)
        {
            return;
        }

        var normalizedRepairStartedAtUtc = repairStartedAtUtc.ToUniversalTime();
        if (normalizedRepairStartedAtUtc < OpenedAtUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(repairStartedAtUtc),
                repairStartedAtUtc,
                "Repair start cannot be before work order opened time.");
        }

        RepairStartedAtUtc = normalizedRepairStartedAtUtc;
    }

    public void Assign(string? technicianUserId, string? teamId)
    {
        EnsureStatus(MaintenanceWorkOrderStatus.Open);
        var normalizedTechnician = MaintenanceText.Optional(technicianUserId);
        var normalizedTeam = MaintenanceText.Optional(teamId);
        if (normalizedTechnician is null && normalizedTeam is null)
        {
            throw new ArgumentException("A technician or team assignment is required.");
        }

        AssignedTechnicianUserId = normalizedTechnician;
        AssignedTeamId = normalizedTeam;
        IncrementVersion();
    }

    public void Accept(string technicianUserId)
    {
        EnsureStatus(MaintenanceWorkOrderStatus.Open);
        var normalizedTechnician = MaintenanceText.Required(technicianUserId, nameof(technicianUserId));
        if (AssignedTechnicianUserId is not null
            && !string.Equals(AssignedTechnicianUserId, normalizedTechnician, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The work order is assigned to another technician.");
        }

        AssignedTechnicianUserId = normalizedTechnician;
        Status = MaintenanceWorkOrderStatus.Accepted;
        AcceptedAtUtc = DateTimeOffset.UtcNow;
        IncrementVersion();
    }

    public void StartWork()
    {
        EnsureStatus(MaintenanceWorkOrderStatus.Accepted);
        Status = MaintenanceWorkOrderStatus.InProgress;
        RepairStartedAtUtc ??= DateTimeOffset.UtcNow;
        IncrementVersion();
    }

    public void Pause(bool waitingForParts)
    {
        EnsureStatus(MaintenanceWorkOrderStatus.InProgress);
        Status = waitingForParts
            ? MaintenanceWorkOrderStatus.WaitingForParts
            : MaintenanceWorkOrderStatus.Paused;
        IncrementVersion();
    }

    public void Resume()
    {
        EnsureStatus(MaintenanceWorkOrderStatus.Paused, MaintenanceWorkOrderStatus.WaitingForParts);
        Status = MaintenanceWorkOrderStatus.InProgress;
        IncrementVersion();
    }

    public void Finish(
        string result,
        string downtimeReasonCode,
        int downtimeMinutes,
        IEnumerable<SparePartLineDraft> spareParts,
        string technicianUserId,
        int? actualLaborMinutes = null,
        decimal? sparePartCostAmount = null,
        decimal? externalServiceCostAmount = null,
        string? costCurrencyCode = null)
    {
        EnsureStatus(MaintenanceWorkOrderStatus.InProgress);
        CompleteCore(
            result,
            downtimeReasonCode,
            downtimeMinutes,
            spareParts,
            actualLaborMinutes,
            sparePartCostAmount,
            externalServiceCostAmount,
            costCurrencyCode,
            technicianUserId);
        IncrementVersion();
    }

    public void Verify()
    {
        EnsureStatus(MaintenanceWorkOrderStatus.Completed);
        Status = MaintenanceWorkOrderStatus.Verified;
        VerifiedAtUtc = DateTimeOffset.UtcNow;
        IncrementVersion();
    }

    public void Close()
    {
        EnsureStatus(MaintenanceWorkOrderStatus.Verified);
        Status = MaintenanceWorkOrderStatus.Closed;
        ClosedAtUtc = DateTimeOffset.UtcNow;
        IncrementVersion();
    }

    public void Cancel()
    {
        EnsureStatus(
            MaintenanceWorkOrderStatus.Open,
            MaintenanceWorkOrderStatus.Accepted,
            MaintenanceWorkOrderStatus.InProgress,
            MaintenanceWorkOrderStatus.Paused,
            MaintenanceWorkOrderStatus.WaitingForParts);
        Status = MaintenanceWorkOrderStatus.Cancelled;
        CancelledAtUtc = DateTimeOffset.UtcNow;
        IncrementVersion();
    }

    public void MarkAlarmCleared(DateTimeOffset clearedAtUtc)
    {
        EnsureOpen();
        if (AlarmCleared)
        {
            return;
        }

        AlarmCleared = true;
        AlarmClearedAtUtc = clearedAtUtc.ToUniversalTime();
        this.AddDomainEvent(new MaintenanceWorkOrderAlarmClearedDomainEvent(this, AlarmClearedAtUtc.Value));
    }

    public void MarkAssetUnavailable(DateTimeOffset fromUtc, string reason)
    {
        EnsureOpen();
        var normalizedReason = MaintenanceText.Required(reason, nameof(reason));
        if (AssetUnavailable)
        {
            return;
        }

        AssetUnavailable = true;
        AssetUnavailableReason = normalizedReason;
        AssetUnavailableFromUtc = fromUtc;
        this.AddDomainEvent(new AssetUnavailableDomainEvent(this, normalizedReason, fromUtc));
    }

    public void Complete(
        string result,
        string downtimeReasonCode,
        int downtimeMinutes,
        IEnumerable<SparePartLineDraft> spareParts,
        int? actualLaborMinutes = null,
        decimal? sparePartCostAmount = null,
        decimal? externalServiceCostAmount = null,
        string? costCurrencyCode = null,
        string? actualTechnicianUserId = null)
    {
        EnsureStatus(MaintenanceWorkOrderStatus.Open, MaintenanceWorkOrderStatus.InProgress);
        CompleteCore(
            result,
            downtimeReasonCode,
            downtimeMinutes,
            spareParts,
            actualLaborMinutes,
            sparePartCostAmount,
            externalServiceCostAmount,
            costCurrencyCode,
            actualTechnicianUserId);
        IncrementVersion();
    }

    private void CompleteCore(
        string result,
        string downtimeReasonCode,
        int downtimeMinutes,
        IEnumerable<SparePartLineDraft> spareParts,
        int? actualLaborMinutes,
        decimal? sparePartCostAmount,
        decimal? externalServiceCostAmount,
        string? costCurrencyCode,
        string? actualTechnicianUserId)
    {
        CompletionResult = MaintenanceText.Required(result, nameof(result));
        DowntimeReasonCode = MaintenanceText.Required(downtimeReasonCode, nameof(downtimeReasonCode));
        DowntimeMinutes = MaintenanceText.Positive(downtimeMinutes, nameof(downtimeMinutes));
        ActualLaborMinutes = actualLaborMinutes is null ? null : MaintenanceText.Positive(actualLaborMinutes.Value, nameof(actualLaborMinutes));
        ActualTechnicianUserId = MaintenanceText.Optional(actualTechnicianUserId) ?? AssignedTechnicianUserId;
        SparePartCostAmount = NonNegative(sparePartCostAmount, nameof(sparePartCostAmount));
        ExternalServiceCostAmount = NonNegative(externalServiceCostAmount, nameof(externalServiceCostAmount));
        CostCurrencyCode = MaintenanceText.Optional(costCurrencyCode);
        sparePartLines.Clear();
        foreach (var part in spareParts)
        {
            var line = SparePartLine.Create(part);
            sparePartLines.Add(line);
            this.AddDomainEvent(new MaintenanceSparePartIssuedDomainEvent(this, line));
        }

        Status = MaintenanceWorkOrderStatus.Completed;
        CompletedAtUtc = DateTimeOffset.UtcNow;
        this.AddDomainEvent(new MaintenanceWorkOrderCompletedDomainEvent(this));
        if (AssetUnavailable)
        {
            this.AddDomainEvent(new AssetRestoredDomainEvent(this, CompletedAtUtc.Value));
        }
    }

    public SparePartLine AddSparePartLine(SparePartLineDraft draft)
    {
        EnsureOpen();
        var line = SparePartLine.Create(draft);
        sparePartLines.Add(line);
        this.AddDomainEvent(new MaintenanceSparePartIssuedDomainEvent(this, line));
        return line;
    }

    private void EnsureOpen()
    {
        if (Status is MaintenanceWorkOrderStatus.Completed
            or MaintenanceWorkOrderStatus.Verified
            or MaintenanceWorkOrderStatus.Closed
            or MaintenanceWorkOrderStatus.Cancelled
            || !Enum.IsDefined(Status))
        {
            throw new InvalidOperationException("Finished maintenance work orders are immutable.");
        }
    }

    private void EnsureStatus(params MaintenanceWorkOrderStatus[] allowed)
    {
        if (!Enum.IsDefined(Status) || !allowed.Contains(Status))
        {
            throw new InvalidOperationException($"Maintenance action is not allowed from status '{Status}'.");
        }
    }

    private void IncrementVersion() => Version = checked(Version + 1);

    private static decimal? NonNegative(decimal? value, string parameterName)
    {
        if (value is < 0m)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, $"{parameterName} cannot be negative.");
        }

        return value;
    }
}

public sealed class MaintenanceWorkOrderLifecycleEvent : Entity<MaintenanceWorkOrderLifecycleEventId>
{
    private MaintenanceWorkOrderLifecycleEvent()
    {
    }

    private MaintenanceWorkOrderLifecycleEvent(
        MaintenanceWorkOrder workOrder,
        MaintenanceWorkOrderAction action,
        MaintenanceWorkOrderStatus fromStatus,
        string actorPrincipalId,
        string? technicianUserId,
        string? teamId,
        string reason,
        string idempotencyKey,
        string payloadFingerprint,
        DateTimeOffset occurredAtUtc)
    {
        OrganizationId = workOrder.OrganizationId;
        EnvironmentId = workOrder.EnvironmentId;
        WorkOrderId = workOrder.Id;
        Action = action;
        FromStatus = fromStatus;
        ToStatus = workOrder.Status;
        ActorPrincipalId = MaintenanceText.Required(actorPrincipalId, nameof(actorPrincipalId));
        TechnicianUserId = MaintenanceText.Optional(technicianUserId);
        TeamId = MaintenanceText.Optional(teamId);
        Reason = MaintenanceText.Required(reason, nameof(reason));
        IdempotencyKey = MaintenanceText.Required(idempotencyKey, nameof(idempotencyKey));
        PayloadFingerprint = MaintenanceText.Required(payloadFingerprint, nameof(payloadFingerprint));
        ResultingVersion = workOrder.Version;
        OccurredAtUtc = occurredAtUtc.ToUniversalTime();
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public MaintenanceWorkOrderId WorkOrderId { get; private set; } = default!;
    public MaintenanceWorkOrderAction Action { get; private set; }
    public MaintenanceWorkOrderStatus FromStatus { get; private set; }
    public MaintenanceWorkOrderStatus ToStatus { get; private set; }
    public string ActorPrincipalId { get; private set; } = string.Empty;
    public string? TechnicianUserId { get; private set; }
    public string? TeamId { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string PayloadFingerprint { get; private set; } = string.Empty;
    public int ResultingVersion { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }

    public static MaintenanceWorkOrderLifecycleEvent Record(
        MaintenanceWorkOrder workOrder,
        MaintenanceWorkOrderAction action,
        MaintenanceWorkOrderStatus fromStatus,
        string actorPrincipalId,
        string? technicianUserId,
        string? teamId,
        string reason,
        string idempotencyKey,
        string payloadFingerprint,
        DateTimeOffset occurredAtUtc) =>
        new(workOrder, action, fromStatus, actorPrincipalId, technicianUserId, teamId, reason, idempotencyKey, payloadFingerprint, occurredAtUtc);
}

public sealed class SparePartLine : Entity<SparePartLineId>
{
    private SparePartLine()
    {
    }

    private SparePartLine(SparePartLineDraft draft)
    {
        Id = new SparePartLineId(Guid.CreateVersion7());
        SkuCode = MaintenanceText.Required(draft.SkuCode, nameof(draft.SkuCode));
        Quantity = MaintenanceText.Positive(draft.Quantity, nameof(draft.Quantity));
        UomCode = MaintenanceText.Optional(draft.UomCode);
    }

    public string SkuCode { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public string? UomCode { get; private set; }

    public static SparePartLine Create(SparePartLineDraft draft)
    {
        return new SparePartLine(draft);
    }
}
