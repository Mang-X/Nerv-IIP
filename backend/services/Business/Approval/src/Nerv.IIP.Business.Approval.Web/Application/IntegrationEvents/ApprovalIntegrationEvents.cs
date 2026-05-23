namespace Nerv.IIP.Business.Approval.Web.Application.IntegrationEvents;

public static class ApprovalIntegrationEventTypes
{
    public const string ApprovalStarted = "businessApproval.ApprovalStarted";
    public const string StepResolved = "businessApproval.StepResolved";
    public const string ApprovalApproved = "businessApproval.ApprovalApproved";
    public const string ApprovalRejected = "businessApproval.ApprovalRejected";
    public const string ApprovalReturned = "businessApproval.ApprovalReturned";
}

public static class ApprovalIntegrationEventSources
{
    public const string BusinessApproval = "business-approval";
}

public sealed record ApprovalDocumentReferencePayload(
    string SourceService,
    string DocumentType,
    string DocumentId,
    string? DocumentLineId);

public sealed record ApprovalStartedIntegrationEvent(
    string EventId,
    string EventType,
    int EventVersion,
    DateTimeOffset OccurredAtUtc,
    string SourceService,
    string OrganizationId,
    string EnvironmentId,
    string IdempotencyKey,
    ApprovalStartedPayload Payload);

public sealed record ApprovalStartedPayload(
    string ChainId,
    string TemplateCode,
    int TemplateVersion,
    ApprovalDocumentReferencePayload DocumentReference,
    string StartedBy);

public sealed record ApprovalStepResolvedIntegrationEvent(
    string EventId,
    string EventType,
    int EventVersion,
    DateTimeOffset OccurredAtUtc,
    string SourceService,
    string OrganizationId,
    string EnvironmentId,
    string IdempotencyKey,
    ApprovalStepResolvedPayload Payload);

public sealed record ApprovalStepResolvedPayload(
    string ChainId,
    int StepNo,
    string ActorType,
    string ActorRef,
    string Decision,
    string? Comment,
    ApprovalDocumentReferencePayload DocumentReference);

public sealed record ApprovalCompletedIntegrationEvent(
    string EventId,
    string EventType,
    int EventVersion,
    DateTimeOffset OccurredAtUtc,
    string SourceService,
    string OrganizationId,
    string EnvironmentId,
    string IdempotencyKey,
    ApprovalCompletedPayload Payload);

public sealed record ApprovalCompletedPayload(
    string ChainId,
    string Result,
    string ActorType,
    string ActorRef,
    ApprovalDocumentReferencePayload DocumentReference);
