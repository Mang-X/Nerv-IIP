using Nerv.IIP.Contracts.Quality;

namespace Nerv.IIP.Business.Mes.Web.Tests;

internal static class MesInspectionResultEventFactory
{
    public static InspectionResultIntegrationEvent Create(
        string eventId,
        string eventType,
        string inspectionRecordId,
        string sourceDocumentId,
        DateTimeOffset occurredAtUtc,
        string inspectionPlanId,
        string skuCode,
        string sourceService,
        string? dispositionReason = null,
        string sourceType = "in-process",
        string? workOrderId = null,
        string? operationTaskId = null)
    {
        var result = eventType == QualityIntegrationEventTypes.InspectionPassed
            ? "passed"
            : eventType == QualityIntegrationEventTypes.InspectionConditionalReleased
                ? "conditional-release"
                : "rejected";
        return new InspectionResultIntegrationEvent(
            eventId,
            eventType,
            QualityIntegrationEventVersions.V1,
            occurredAtUtc,
            QualityIntegrationEventSources.BusinessQuality,
            $"corr-{eventId}",
            $"cause-{eventId}",
            "org-001",
            "env-dev",
            "quality",
            $"quality:inspection-result:org-001:env-dev:{inspectionRecordId}:{eventType}",
            new InspectionResultPayload(
                inspectionRecordId,
                inspectionPlanId,
                sourceType,
                sourceService,
                sourceDocumentId,
                skuCode,
                10m,
                result,
                dispositionReason ?? (eventType == QualityIntegrationEventTypes.InspectionRejected ? "critical-defect" : null),
                [],
                occurredAtUtc,
                WorkOrderId: workOrderId,
                OperationTaskId: operationTaskId));
    }
}
