using Nerv.IIP.Contracts.IntegrationEvents;

namespace Nerv.IIP.Contracts.Mes;

public static class MesIntegrationEventTypes
{
    public const string WorkOrderReleased = "mes.WorkOrderReleased";
    public const string WorkOrderCompleted = "mes.WorkOrderCompleted";
    public const string WorkOrderClosed = "mes.WorkOrderClosed";
    public const string WorkOrderEngineeringChangeImpactDetected = "mes.WorkOrderEngineeringChangeImpactDetected";
    public const string OperationTaskCompleted = "mes.OperationTaskCompleted";
    public const string FinishedGoodsReceiptRequested = "mes.FinishedGoodsReceiptRequested";
    public const string ProductionReportRecorded = "mes.ProductionReportRecorded";
}

public static class MesIntegrationEventVersions
{
    public const int V1 = 1;
}

public static class MesIntegrationEventSources
{
    public const string BusinessMes = "business-mes";
}

public sealed record WorkOrderReleasedIntegrationEvent(
    string EventId,
    string EventType,
    int EventVersion,
    DateTimeOffset OccurredAtUtc,
    string SourceService,
    string CorrelationId,
    string CausationId,
    string OrganizationId,
    string EnvironmentId,
    string Actor,
    string IdempotencyKey,
    WorkOrderReleasedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record WorkOrderReleasedPayload(
    string WorkOrderId,
    string SkuCode,
    decimal PlannedQuantity,
    DateTimeOffset ReleasedAtUtc,
    IReadOnlyCollection<ReleasedOperationPayload> Operations);

public sealed record ReleasedOperationPayload(
    string OperationId,
    int OperationSequence,
    string WorkCenterId);

public sealed record WorkOrderCompletedIntegrationEvent(
    string EventId,
    string EventType,
    int EventVersion,
    DateTimeOffset OccurredAtUtc,
    string SourceService,
    string CorrelationId,
    string CausationId,
    string OrganizationId,
    string EnvironmentId,
    string Actor,
    string IdempotencyKey,
    WorkOrderCompletedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record WorkOrderCompletedPayload(
    string WorkOrderId,
    string SkuCode,
    decimal PlannedQuantity,
    decimal GoodQuantity,
    decimal ScrapQuantity,
    DateTimeOffset CompletedAtUtc,
    int ExpectedCostReportCount = 0,
    int ExpectedMaterialMovementCount = 0);

public sealed record WorkOrderClosedIntegrationEvent(
    string EventId,
    string EventType,
    int EventVersion,
    DateTimeOffset OccurredAtUtc,
    string SourceService,
    string CorrelationId,
    string CausationId,
    string OrganizationId,
    string EnvironmentId,
    string Actor,
    string IdempotencyKey,
    WorkOrderClosedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record WorkOrderClosedPayload(
    string WorkOrderId,
    string SkuCode,
    decimal PlannedQuantity,
    decimal GoodQuantity,
    decimal ScrapQuantity,
    DateTimeOffset ClosedAtUtc);

public static class MesEngineeringChangeImpactContractStatuses
{
    public const string PendingDecision = "pending-decision";
    public const string AutoRebound = "auto-rebound";
    public const string BlockedForManualConfirmation = "blocked-for-manual-confirmation";
    public const string Decided = "decided";
}

public sealed record WorkOrderEngineeringChangeImpactDetectedIntegrationEvent(
    string EventId,
    string EventType,
    int EventVersion,
    DateTimeOffset OccurredAtUtc,
    string SourceService,
    string CorrelationId,
    string CausationId,
    string OrganizationId,
    string EnvironmentId,
    string Actor,
    string IdempotencyKey,
    WorkOrderEngineeringChangeImpactDetectedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record WorkOrderEngineeringChangeImpactDetectedPayload(
    string WorkOrderId,
    string SkuCode,
    string ChangeNumber,
    string ArchivedProductionVersionId,
    string? SupersededByProductionVersionId,
    string ImpactStatus,
    DateOnly EffectiveDate);

public sealed record OperationTaskCompletedIntegrationEvent(
    string EventId,
    string EventType,
    int EventVersion,
    DateTimeOffset OccurredAtUtc,
    string SourceService,
    string CorrelationId,
    string CausationId,
    string OrganizationId,
    string EnvironmentId,
    string Actor,
    string IdempotencyKey,
    OperationTaskCompletedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record OperationTaskCompletedPayload(
    string WorkOrderId,
    string OperationTaskId,
    string SkuCode,
    int OperationSequence,
    string WorkCenterId,
    decimal PlannedQuantity,
    string UomCode,
    bool RequiresQualityInspection,
    DateTimeOffset CompletedAtUtc);

public sealed record ProductionReportRecordedIntegrationEvent(
    string EventId,
    string EventType,
    int EventVersion,
    DateTimeOffset OccurredAtUtc,
    string SourceService,
    string CorrelationId,
    string CausationId,
    string OrganizationId,
    string EnvironmentId,
    string Actor,
    string IdempotencyKey,
    ProductionReportRecordedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record ProductionReportRecordedPayload(
    string ReportNo,
    string WorkOrderId,
    string OperationTaskId,
    string WorkCenterId,
    string? DeviceAssetId,
    decimal GoodQuantity,
    decimal ScrapQuantity,
    decimal ReworkQuantity,
    string UomCode,
    decimal? TheoreticalRatePerHour,
    DateTimeOffset ReportedAtUtc,
    bool IsReversal,
    string? ReversedReportNo = null,
    int MaterialMovementCount = 0);

public sealed record FinishedGoodsReceiptRequestedIntegrationEvent(
    string EventId,
    string EventType,
    int EventVersion,
    DateTimeOffset OccurredAtUtc,
    string SourceService,
    string CorrelationId,
    string CausationId,
    string OrganizationId,
    string EnvironmentId,
    string Actor,
    string IdempotencyKey,
    FinishedGoodsReceiptRequestedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record FinishedGoodsReceiptRequestedPayload(
    string RequestNo,
    string WorkOrderId,
    string SkuCode,
    decimal Quantity,
    string UomCode,
    string? ProducedLotNo,
    string? SerialNo,
    DateTimeOffset RequestedAtUtc);
