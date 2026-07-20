using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceInspectionAggregate;
using Nerv.IIP.Business.Maintenance.Domain.DomainEvents;

namespace Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;

public partial record MaintenanceWorkOrderId : IGuidStronglyTypedId;

public partial record SparePartLineId : IGuidStronglyTypedId;

public enum MaintenanceWorkOrderStatus
{
    Open = 0,
    Completed = 1,
}

public static class MaintenanceWorkOrderSourceTypes
{
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
        EnsureOpen();
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
        if (Status == MaintenanceWorkOrderStatus.Completed)
        {
            throw new InvalidOperationException("Completed maintenance work orders are immutable.");
        }
    }

    private static decimal? NonNegative(decimal? value, string parameterName)
    {
        if (value is < 0m)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, $"{parameterName} cannot be negative.");
        }

        return value;
    }
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
