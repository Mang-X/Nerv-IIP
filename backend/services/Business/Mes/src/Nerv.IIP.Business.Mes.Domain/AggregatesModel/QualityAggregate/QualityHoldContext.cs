namespace Nerv.IIP.Business.Mes.Domain.AggregatesModel.QualityAggregate;

public partial record QualityHoldContextId : IGuidStronglyTypedId;

public sealed class QualityHoldContext : Entity<QualityHoldContextId>, IAggregateRoot
{
    private QualityHoldContext()
    {
    }

    private QualityHoldContext(
        string organizationId,
        string environmentId,
        string workOrderId,
        string? operationTaskId,
        string sourceService,
        string sourceDocumentId,
        string inspectionRecordId,
        string? inspectionPlanId,
        string result,
        string eventType,
        string? dispositionReason,
        DateTimeOffset recordedAtUtc,
        string actor)
    {
        OrganizationId = DomainGuard.Required(organizationId, nameof(organizationId));
        EnvironmentId = DomainGuard.Required(environmentId, nameof(environmentId));
        WorkOrderId = DomainGuard.Required(workOrderId, nameof(workOrderId));
        OperationTaskId = string.IsNullOrWhiteSpace(operationTaskId) ? null : operationTaskId.Trim();
        SourceService = DomainGuard.Required(sourceService, nameof(sourceService));
        SourceDocumentId = DomainGuard.Required(sourceDocumentId, nameof(sourceDocumentId));
        InspectionRecordId = DomainGuard.Required(inspectionRecordId, nameof(inspectionRecordId));
        InspectionPlanId = string.IsNullOrWhiteSpace(inspectionPlanId) ? null : inspectionPlanId.Trim();
        Result = DomainGuard.Required(result, nameof(result));
        EventType = DomainGuard.Required(eventType, nameof(eventType));
        DispositionReason = string.IsNullOrWhiteSpace(dispositionReason) ? null : dispositionReason.Trim();
        RecordedAtUtc = recordedAtUtc;
        Active = IsBlockingResult(result, eventType);
        if (Active)
        {
            RecordHoldAudit(inspectionRecordId, InspectionPlanId, DispositionReason, recordedAtUtc, actor);
        }
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string WorkOrderId { get; private set; } = string.Empty;
    public string? OperationTaskId { get; private set; }
    public string SourceService { get; private set; } = string.Empty;
    public string SourceDocumentId { get; private set; } = string.Empty;
    public string InspectionRecordId { get; private set; } = string.Empty;
    public string? InspectionPlanId { get; private set; }
    public string Result { get; private set; } = string.Empty;
    public string EventType { get; private set; } = string.Empty;
    public string? DispositionReason { get; private set; }
    public DateTimeOffset RecordedAtUtc { get; private set; }
    public bool Active { get; private set; }
    public string? HeldInspectionRecordId { get; private set; }
    public string? HeldInspectionDocumentId { get; private set; }
    public string? HoldReason { get; private set; }
    public DateTimeOffset? HeldAtUtc { get; private set; }
    public string? HeldBy { get; private set; }
    public string? ReleaseInspectionRecordId { get; private set; }
    public string? ReleaseReason { get; private set; }
    public DateTimeOffset? ReleasedAtUtc { get; private set; }
    public string? ReleasedBy { get; private set; }
    public string? ReleaseSource { get; private set; }

    public static QualityHoldContext Capture(
        string organizationId,
        string environmentId,
        string workOrderId,
        string? operationTaskId,
        string sourceService,
        string sourceDocumentId,
        string inspectionRecordId,
        string? inspectionPlanId,
        string result,
        string eventType,
        string? dispositionReason,
        DateTimeOffset recordedAtUtc,
        string actor = "quality")
    {
        return new QualityHoldContext(
            organizationId,
            environmentId,
            workOrderId,
            operationTaskId,
            sourceService,
            sourceDocumentId,
            inspectionRecordId,
            inspectionPlanId,
            result,
            eventType,
            dispositionReason,
            recordedAtUtc,
            actor);
    }

    public bool ApplyInspectionResult(
        string inspectionRecordId,
        string? inspectionPlanId,
        string result,
        string eventType,
        string? dispositionReason,
        DateTimeOffset recordedAtUtc,
        string actor = "quality")
    {
        if (recordedAtUtc < RecordedAtUtc)
        {
            return false;
        }

        var wasActive = Active;
        InspectionRecordId = DomainGuard.Required(inspectionRecordId, nameof(inspectionRecordId));
        InspectionPlanId = string.IsNullOrWhiteSpace(inspectionPlanId) ? null : inspectionPlanId.Trim();
        Result = DomainGuard.Required(result, nameof(result));
        EventType = DomainGuard.Required(eventType, nameof(eventType));
        DispositionReason = string.IsNullOrWhiteSpace(dispositionReason) ? null : dispositionReason.Trim();
        RecordedAtUtc = recordedAtUtc;
        Active = IsBlockingResult(result, eventType);
        if (Active)
        {
            RecordHoldAudit(inspectionRecordId, InspectionPlanId, DispositionReason, recordedAtUtc, actor);
            return !wasActive;
        }

        if (wasActive)
        {
            RecordReleaseAudit(inspectionRecordId, DispositionReason ?? "Quality inspection released the hold.", recordedAtUtc, actor, eventType);
            return true;
        }

        return false;
    }

    public bool ForceRelease(string reason, string actor, DateTimeOffset releasedAtUtc)
    {
        if (!Active)
        {
            return false;
        }

        if (HeldAtUtc.HasValue && releasedAtUtc < HeldAtUtc.Value)
        {
            throw new KnownException("Quality hold release time cannot be earlier than the hold time.");
        }

        Active = false;
        RecordReleaseAudit(null, reason, releasedAtUtc, actor, "manual-force-release");
        return true;
    }

    private static bool IsBlockingResult(string result, string eventType)
    {
        return string.Equals(eventType, "quality.InspectionRejected", StringComparison.Ordinal) ||
            string.Equals(result, "rejected", StringComparison.OrdinalIgnoreCase);
    }

    private void RecordHoldAudit(string inspectionRecordId, string? inspectionDocumentId, string? reason, DateTimeOffset heldAtUtc, string actor)
    {
        HeldInspectionRecordId = DomainGuard.Required(inspectionRecordId, nameof(inspectionRecordId));
        HeldInspectionDocumentId = string.IsNullOrWhiteSpace(inspectionDocumentId) ? null : inspectionDocumentId.Trim();
        HoldReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        HeldAtUtc = heldAtUtc;
        HeldBy = DomainGuard.Required(actor, nameof(actor));
        ReleaseInspectionRecordId = null;
        ReleaseReason = null;
        ReleasedAtUtc = null;
        ReleasedBy = null;
        ReleaseSource = null;
    }

    private void RecordReleaseAudit(
        string? inspectionRecordId,
        string reason,
        DateTimeOffset releasedAtUtc,
        string actor,
        string releaseSource)
    {
        ReleaseInspectionRecordId = string.IsNullOrWhiteSpace(inspectionRecordId) ? null : inspectionRecordId.Trim();
        ReleaseReason = DomainGuard.Required(reason, nameof(reason));
        ReleasedAtUtc = releasedAtUtc;
        ReleasedBy = DomainGuard.Required(actor, nameof(actor));
        ReleaseSource = DomainGuard.Required(releaseSource, nameof(releaseSource));
    }
}
