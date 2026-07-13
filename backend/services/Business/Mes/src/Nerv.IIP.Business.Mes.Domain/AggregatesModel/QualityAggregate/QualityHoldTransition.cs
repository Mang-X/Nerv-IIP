namespace Nerv.IIP.Business.Mes.Domain.AggregatesModel.QualityAggregate;

public partial record QualityHoldTransitionId : IGuidStronglyTypedId;

public sealed class QualityHoldTransition : Entity<QualityHoldTransitionId>, IAggregateRoot
{
    private QualityHoldTransition()
    {
    }

    private QualityHoldTransition(
        string organizationId,
        string environmentId,
        string sourceService,
        string sourceDocumentId,
        string holdCycleId,
        string correlationId,
        string eventKind,
        string actor,
        DateTimeOffset occurredAtUtc,
        string? reason,
        string? sourceInspectionRecordId,
        string? sourceInspectionDocumentId,
        string origin,
        string? idempotencyKey)
    {
        OrganizationId = DomainGuard.Required(organizationId, nameof(organizationId));
        EnvironmentId = DomainGuard.Required(environmentId, nameof(environmentId));
        SourceService = DomainGuard.Required(sourceService, nameof(sourceService));
        SourceDocumentId = DomainGuard.Required(sourceDocumentId, nameof(sourceDocumentId));
        HoldCycleId = DomainGuard.Required(holdCycleId, nameof(holdCycleId));
        CorrelationId = DomainGuard.Required(correlationId, nameof(correlationId));
        EventKind = DomainGuard.Required(eventKind, nameof(eventKind));
        Actor = DomainGuard.Required(actor, nameof(actor));
        OccurredAtUtc = occurredAtUtc;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        SourceInspectionRecordId = string.IsNullOrWhiteSpace(sourceInspectionRecordId) ? null : sourceInspectionRecordId.Trim();
        SourceInspectionDocumentId = string.IsNullOrWhiteSpace(sourceInspectionDocumentId) ? null : sourceInspectionDocumentId.Trim();
        Origin = DomainGuard.Required(origin, nameof(origin));
        IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string SourceService { get; private set; } = string.Empty;
    public string SourceDocumentId { get; private set; } = string.Empty;
    public string HoldCycleId { get; private set; } = string.Empty;
    public string CorrelationId { get; private set; } = string.Empty;
    public string EventKind { get; private set; } = string.Empty;
    public string Actor { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public string? Reason { get; private set; }
    public string? SourceInspectionRecordId { get; private set; }
    public string? SourceInspectionDocumentId { get; private set; }
    public string Origin { get; private set; } = string.Empty;
    public string? IdempotencyKey { get; private set; }

    public static QualityHoldTransition Record(
        string organizationId, string environmentId, string sourceService, string sourceDocumentId,
        string holdCycleId, string correlationId, string eventKind, string actor, DateTimeOffset occurredAtUtc,
        string? reason, string? sourceInspectionRecordId, string? sourceInspectionDocumentId, string origin, string? idempotencyKey = null) =>
        new(organizationId, environmentId, sourceService, sourceDocumentId, holdCycleId, correlationId,
            eventKind, actor, occurredAtUtc, reason, sourceInspectionRecordId, sourceInspectionDocumentId, origin, idempotencyKey);
}
