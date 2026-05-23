namespace Nerv.IIP.Contracts.Quality;

public static class QualityIntegrationEventTypes
{
    public const string InspectionPassed = "quality.InspectionPassed";
    public const string InspectionRejected = "quality.InspectionRejected";
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
}

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
    NcrOpenedPayload Payload);

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
    NcrDispositionDecidedPayload Payload);

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
    NcrClosedPayload Payload);

public sealed record InspectionPassedIntegrationEvent(
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
    InspectionResultPayload Payload);

public sealed record InspectionRejectedIntegrationEvent(
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
    InspectionResultPayload Payload);

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
    DateTimeOffset RecordedAtUtc);

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
    DateTimeOffset ChangedAtUtc);

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
