using Nerv.IIP.Contracts.IntegrationEvents;

namespace Nerv.IIP.Contracts.Approval;

public static class ApprovalIntegrationEventTypes
{
    public const string ApprovalStarted = "businessApproval.ApprovalStarted";
    public const string StepResolved = "businessApproval.StepResolved";
    public const string StepOverdue = "businessApproval.StepOverdue";
    public const string ActionRecorded = "businessApproval.ActionRecorded";
    public const string ApprovalApproved = "businessApproval.ApprovalApproved";
    public const string ApprovalRejected = "businessApproval.ApprovalRejected";
    public const string ApprovalReturned = "businessApproval.ApprovalReturned";
}

public static class ApprovalIntegrationEventVersions
{
    public const int V1 = 1;
}

public static class ApprovalIntegrationEventSources
{
    public const string BusinessApproval = "business-approval";
}

public static class ApprovalResults
{
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Returned = "returned";
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
    string CorrelationId,
    string CausationId,
    string OrganizationId,
    string EnvironmentId,
    string Actor,
    string IdempotencyKey,
    ApprovalStartedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

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
    string CorrelationId,
    string CausationId,
    string OrganizationId,
    string EnvironmentId,
    string Actor,
    string IdempotencyKey,
    ApprovalStepResolvedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record ApprovalStepResolvedPayload(
    string ChainId,
    int StepNo,
    string ActorType,
    string ActorRef,
    string? OnBehalfOfActorType,
    string? OnBehalfOfActorRef,
    string Decision,
    string? Comment,
    ApprovalDocumentReferencePayload DocumentReference);

public sealed record ApprovalStepOverdueIntegrationEvent(
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
    ApprovalStepOverduePayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record ApprovalStepOverduePayload(
    string ChainId,
    string StepId,
    int StepNo,
    string StepName,
    string ApproverType,
    string ApproverRef,
    DateTimeOffset DueAtUtc,
    DateTimeOffset MarkedAtUtc,
    ApprovalDocumentReferencePayload DocumentReference);

public sealed record ApprovalActionRecordedIntegrationEvent(
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
    ApprovalActionRecordedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record ApprovalActionRecordedPayload(
    string ChainId,
    string StepId,
    int StepNo,
    string Action,
    string ActorType,
    string ActorRef,
    string? Reason,
    IReadOnlyCollection<string> SuggestedRecipientRefs,
    ApprovalDocumentReferencePayload DocumentReference);

public sealed record ApprovalCompletedIntegrationEvent(
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
    ApprovalCompletedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record ApprovalCompletedPayload(
    string ChainId,
    string Result,
    string ActorType,
    string ActorRef,
    string? OnBehalfOfActorType,
    string? OnBehalfOfActorRef,
    ApprovalDocumentReferencePayload DocumentReference);
