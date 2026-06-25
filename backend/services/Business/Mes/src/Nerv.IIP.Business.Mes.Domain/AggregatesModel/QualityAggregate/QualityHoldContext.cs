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
        DateTimeOffset recordedAtUtc)
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
        DateTimeOffset recordedAtUtc)
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
            recordedAtUtc);
    }

    public void ApplyInspectionResult(
        string inspectionRecordId,
        string? inspectionPlanId,
        string result,
        string eventType,
        string? dispositionReason,
        DateTimeOffset recordedAtUtc)
    {
        if (recordedAtUtc < RecordedAtUtc)
        {
            return;
        }

        InspectionRecordId = DomainGuard.Required(inspectionRecordId, nameof(inspectionRecordId));
        InspectionPlanId = string.IsNullOrWhiteSpace(inspectionPlanId) ? null : inspectionPlanId.Trim();
        Result = DomainGuard.Required(result, nameof(result));
        EventType = DomainGuard.Required(eventType, nameof(eventType));
        DispositionReason = string.IsNullOrWhiteSpace(dispositionReason) ? null : dispositionReason.Trim();
        RecordedAtUtc = recordedAtUtc;
        Active = IsBlockingResult(result, eventType);
    }

    private static bool IsBlockingResult(string result, string eventType)
    {
        return string.Equals(eventType, "quality.InspectionRejected", StringComparison.Ordinal) ||
            string.Equals(result, "rejected", StringComparison.OrdinalIgnoreCase);
    }
}
