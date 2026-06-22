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
    IReadOnlyCollection<InspectionResultLinePayload>? ResultLines = null);

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
    DateTimeOffset ClosedAtUtc);
