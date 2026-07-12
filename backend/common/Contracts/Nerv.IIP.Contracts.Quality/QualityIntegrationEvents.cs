using Nerv.IIP.Contracts.IntegrationEvents;

namespace Nerv.IIP.Contracts.Quality;

public static class QualityIntegrationEventTypes
{
    public const string InspectionPassed = "quality.InspectionPassed";
    public const string InspectionConditionalReleased = "quality.InspectionConditionalReleased";
    public const string InspectionRejected = "quality.InspectionRejected";
    public const string DefectRaised = "quality.DefectRaised";
    public const string NcrOpened = "quality.NcrOpened";
    public const string DispositionDecided = "quality.DispositionDecided";
    public const string NcrClosed = "quality.NcrClosed";
    public const string InspectionTaskOverdue = "quality.InspectionTaskOverdue";
    public const string CapaOpened = "quality.CapaOpened";
    public const string CapaEffectivenessVerified = "quality.CapaEffectivenessVerified";
    public const string CapaClosed = "quality.CapaClosed";
    public const string SpcAlertRaised = "quality.SpcAlertRaised";
    public const string MeasuringDeviceCalibrationDue = "quality.MeasuringDeviceCalibrationDue";
}

public static class QualityIntegrationEventVersions
{
    public const int V1 = 1;
}

public static class QualityIntegrationEventSources
{
    public const string BusinessQuality = "business-quality";
    public const string BusinessMes = "business-mes";
}

public static class QualityInspectionSourceTypes
{
    public const string Wms = "wms";
    public const string Receiving = "receiving";
}

public static class QualityInspectionDispositionStatuses
{
    public const string Passed = "passed";
    public const string ConditionalRelease = "conditional-release";
    public const string Rejected = "rejected";
}

public static class QualityStockReleaseTargetStatuses
{
    public const string Unrestricted = "unrestricted";
    public const string Restricted = "restricted";
    public const string Blocked = "blocked";
}

public static class QualityNcrDispositionTypes
{
    public const string Rework = "rework";
    public const string Scrap = "scrap";
    public const string ReturnToSupplier = "return-to-supplier";
    public const string ConditionalRelease = "conditional-release";
    public const string SortAndScreen = "sort-and-screen";
}

public static class QualitySpcRuleCodes
{
    public const string BeyondControlLimit = "beyond-control-limit";
    public const string ConsecutiveShiftAboveCenter = "consecutive-shift-above-center";
    public const string ConsecutiveShiftBelowCenter = "consecutive-shift-below-center";
    public const string TrendIncreasing = "trend-increasing";
    public const string TrendDecreasing = "trend-decreasing";
}

public sealed record DefectRaisedIntegrationEvent(
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
    DefectRaisedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record DefectRaisedPayload(
    string DefectNo,
    string WorkOrderId,
    string? OperationTaskId,
    string DefectCode,
    decimal Quantity,
    DateTimeOffset RecordedAtUtc);

public sealed record NcrOpenedIntegrationEvent(
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
    NcrOpenedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record NcrDispositionDecidedIntegrationEvent(
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
    NcrDispositionDecidedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record NcrClosedIntegrationEvent(
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
    NcrClosedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record CapaOpenedIntegrationEvent(
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
    CapaOpenedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record CapaEffectivenessVerifiedIntegrationEvent(
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
    CapaEffectivenessVerifiedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record CapaClosedIntegrationEvent(
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
    CapaClosedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record InspectionResultIntegrationEvent(
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
    InspectionResultPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record InspectionTaskOverdueIntegrationEvent(
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
    InspectionTaskOverduePayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record MeasuringDeviceCalibrationDueIntegrationEvent(
    string EventId, string EventType, int EventVersion, DateTimeOffset OccurredAtUtc,
    string SourceService, string CorrelationId, string CausationId, string OrganizationId,
    string EnvironmentId, string Actor, string IdempotencyKey, MeasuringDeviceCalibrationDuePayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record MeasuringDeviceCalibrationDuePayload(
    string MeasuringDeviceId, string DeviceCode, string DeviceType, string CalibrationState,
    DateTimeOffset CalibrationDueAtUtc, DateTimeOffset EvaluatedAtUtc);

public sealed record SpcAlertRaisedIntegrationEvent(
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
    SpcAlertRaisedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record InspectionTaskOverduePayload(
    string InspectionTaskId,
    string SourceType,
    string SourceService,
    string SourceDocumentId,
    string? SourceDocumentLineId,
    string SkuCode,
    DateTimeOffset DueAtUtc,
    DateTimeOffset RemindedAtUtc);

public sealed record SpcAlertRaisedPayload(
    string AlertKey,
    string ResourceType,
    string SkuCode,
    string CharacteristicCode,
    string WorkCenterId,
    IReadOnlyCollection<string> RuleCodes,
    string Severity,
    DateTimeOffset LatestMeasuredAtUtc,
    string Summary);

public sealed record InspectionResultPayload(
    string InspectionRecordId,
    string? InspectionPlanId,
    string SourceType,
    string SourceService,
    string SourceDocumentId,
    string SkuCode,
    decimal InspectedQuantity,
    string Result,
    string? DispositionReason,
    IReadOnlyCollection<string> DispositionAttachmentFileIds,
    DateTimeOffset RecordedAtUtc,
    StockReleaseDimensionPayload? StockRelease = null,
    IReadOnlyCollection<InspectionResultLinePayload>? ResultLines = null,
    string? LotNo = null,
    string? SerialNo = null,
    string? SiteCode = null,
    string? LocationCode = null,
    string? OwnerType = null,
    string? OwnerId = null,
    string? UomCode = null);

public sealed record StockReleaseDimensionPayload(
    string UomCode,
    string SiteCode,
    string LocationCode,
    string? LotNo,
    string? SerialNo,
    string SourceQualityStatus,
    string OwnerType,
    string? OwnerId,
    string? TargetQualityStatus = null);

public sealed record InspectionResultLinePayload(
    string CharacteristicCode,
    decimal? MeasuredValue,
    string? ObservedText,
    string? UnitCode,
    string Result,
    string? DefectReason,
    decimal? DefectQuantity);

public sealed record NcrOpenedPayload(
    string NcrId,
    string NcrCode,
    string SourceType,
    string SourceDocumentId,
    string SkuCode,
    decimal DefectQuantity,
    string DefectReason,
    string? BatchNo,
    string? SerialNo,
    string Status,
    DateTimeOffset OpenedAtUtc);

public sealed record NcrDispositionDecidedPayload(
    string NcrId,
    string NcrCode,
    string SkuCode,
    decimal DefectQuantity,
    string DispositionType,
    string? DispositionApprovalChainId,
    string? ReworkWorkOrderId,
    string? ScrapMovementId,
    string? ReturnDocumentId,
    DateTimeOffset ChangedAtUtc,
    IReadOnlyCollection<MrbReviewPayload>? MrbReviews = null)
{
    public string? SourceDocumentId { get; init; }
    public string? LotNo { get; init; }
    public string? SerialNo { get; init; }
    public string? UomCode { get; init; }
    public string? SiteCode { get; init; }
    public string? LocationCode { get; init; }
    public string? OwnerType { get; init; }
    public string? OwnerId { get; init; }
}

public sealed record MrbReviewPayload(
    string ReviewerId,
    string Decision,
    string? Comment,
    DateTimeOffset ReviewedAtUtc);

public sealed record NcrClosedPayload(
    string NcrId,
    string NcrCode,
    string SkuCode,
    decimal DefectQuantity,
    string DispositionType,
    string? ReworkWorkOrderId,
    string? ScrapMovementId,
    string? ReturnDocumentId,
    string Reason,
    DateTimeOffset ClosedAtUtc);

public sealed record CapaOpenedPayload(
    string CorrectiveActionId,
    string CapaCode,
    string? SourceNcrId,
    string OwnerUserId,
    string Status,
    DateTimeOffset DueAtUtc,
    DateTimeOffset OpenedAtUtc);

public sealed record CapaEffectivenessVerifiedPayload(
    string CorrectiveActionId,
    string CapaCode,
    string? SourceNcrId,
    string VerificationInspectionRecordId,
    string VerifiedByUserId,
    string Result,
    DateTimeOffset VerifiedAtUtc);

public sealed record CapaClosedPayload(
    string CorrectiveActionId,
    string CapaCode,
    string? SourceNcrId,
    string? CloseApprovalChainId,
    string ClosedByUserId,
    DateTimeOffset ClosedAtUtc);
