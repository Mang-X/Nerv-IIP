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
        // "in-process" 是 NCR 的来源环节取值，在 Contracts 里零命中，payload.SourceType 面上
        // 真实生产者发不出来——与 #3191 裁定点名的夹具缺陷同族，故默认值取工序检（#3191）。
        string sourceType = QualityInspectionSourceTypes.Operation,
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
