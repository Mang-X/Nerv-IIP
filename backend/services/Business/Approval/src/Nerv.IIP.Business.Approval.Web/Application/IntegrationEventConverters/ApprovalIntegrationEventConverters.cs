using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalChainAggregate;
using Nerv.IIP.Business.Approval.Domain.DomainEvents;
using Nerv.IIP.Contracts.Approval;

namespace Nerv.IIP.Business.Approval.Web.Application.IntegrationEventConverters;

public sealed class ApprovalStartedIntegrationEventConverter
    : IIntegrationEventConverter<ApprovalStartedDomainEvent, ApprovalStartedIntegrationEvent>
{
    public ApprovalStartedIntegrationEvent Convert(ApprovalStartedDomainEvent domainEvent)
    {
        var chain = domainEvent.Chain;
        var occurredAtUtc = DateTimeOffset.UtcNow;
        return new ApprovalStartedIntegrationEvent(
            EventIds.New(),
            ApprovalIntegrationEventTypes.ApprovalStarted,
            ApprovalIntegrationEventVersions.V1,
            occurredAtUtc,
            ApprovalIntegrationEventSources.BusinessApproval,
            chain.Id.ToString(),
            chain.Id.ToString(),
            chain.OrganizationId,
            chain.EnvironmentId,
            chain.StartedBy,
            EventIds.Idempotency("approval-started", chain.OrganizationId, chain.EnvironmentId, chain.Id.ToString()),
            new ApprovalStartedPayload(
                chain.Id.ToString(),
                chain.TemplateCode,
                chain.TemplateVersion,
                ApprovalIntegrationEventConverterHelpers.ToPayload(chain.DocumentReference),
                chain.StartedBy));
    }
}

public sealed class ApprovalStepResolvedIntegrationEventConverter
    : IIntegrationEventConverter<ApprovalStepResolvedDomainEvent, ApprovalStepResolvedIntegrationEvent>
{
    public ApprovalStepResolvedIntegrationEvent Convert(ApprovalStepResolvedDomainEvent domainEvent)
    {
        var chain = domainEvent.Chain;
        var decision = domainEvent.Decision;
        var occurredAtUtc = DateTimeOffset.UtcNow;
        return new ApprovalStepResolvedIntegrationEvent(
            EventIds.New(),
            ApprovalIntegrationEventTypes.StepResolved,
            ApprovalIntegrationEventVersions.V1,
            occurredAtUtc,
            ApprovalIntegrationEventSources.BusinessApproval,
            chain.Id.ToString(),
            decision.Id.ToString(),
            chain.OrganizationId,
            chain.EnvironmentId,
            $"{decision.ActorType}:{decision.ActorRef}",
            EventIds.Idempotency(
                "step-resolved",
                chain.OrganizationId,
                chain.EnvironmentId,
                chain.Id.ToString(),
                decision.RoundNo.ToString(),
                decision.StepNo.ToString(),
                decision.ActorType,
                decision.ActorRef,
                decision.OnBehalfOfActorType ?? "_",
                decision.OnBehalfOfActorRef ?? "_"),
            new ApprovalStepResolvedPayload(
                chain.Id.ToString(),
                decision.StepNo,
                decision.ActorType,
                decision.ActorRef,
                decision.OnBehalfOfActorType,
                decision.OnBehalfOfActorRef,
                decision.Decision,
                decision.Comment,
                ApprovalIntegrationEventConverterHelpers.ToPayload(chain.DocumentReference)));
    }
}

public sealed class ApprovalStepOverdueIntegrationEventConverter
    : IIntegrationEventConverter<ApprovalStepOverdueDomainEvent, ApprovalStepOverdueIntegrationEvent>
{
    public ApprovalStepOverdueIntegrationEvent Convert(ApprovalStepOverdueDomainEvent domainEvent)
    {
        var chain = domainEvent.Chain;
        var step = domainEvent.Step;
        return new ApprovalStepOverdueIntegrationEvent(
            EventIds.New(),
            ApprovalIntegrationEventTypes.StepOverdue,
            ApprovalIntegrationEventVersions.V1,
            domainEvent.MarkedAtUtc,
            ApprovalIntegrationEventSources.BusinessApproval,
            chain.Id.ToString(),
            step.Id.ToString(),
            chain.OrganizationId,
            chain.EnvironmentId,
            $"system:{ApprovalIntegrationEventSources.BusinessApproval}",
            EventIds.Idempotency("step-overdue", chain.OrganizationId, chain.EnvironmentId, chain.Id.ToString(), step.StepNo.ToString(), step.ApproverType, step.ApproverRef),
            new ApprovalStepOverduePayload(
                chain.Id.ToString(),
                step.Id.ToString(),
                step.StepNo,
                step.StepName,
                step.ApproverType,
                step.ApproverRef,
                step.DueAtUtc!.Value,
                domainEvent.MarkedAtUtc,
                ApprovalIntegrationEventConverterHelpers.ToPayload(chain.DocumentReference)));
    }
}

public sealed class ApprovalChainActionRecordedIntegrationEventConverter
    : IIntegrationEventConverter<ApprovalChainActionRecordedDomainEvent, ApprovalActionRecordedIntegrationEvent>
{
    public ApprovalActionRecordedIntegrationEvent Convert(ApprovalChainActionRecordedDomainEvent domainEvent)
    {
        var chain = domainEvent.Chain;
        var decision = domainEvent.Decision;
        return new ApprovalActionRecordedIntegrationEvent(
            EventIds.New(),
            ApprovalIntegrationEventTypes.ActionRecorded,
            ApprovalIntegrationEventVersions.V1,
            decision.DecidedAtUtc,
            ApprovalIntegrationEventSources.BusinessApproval,
            chain.Id.ToString(),
            decision.Id.ToString(),
            chain.OrganizationId,
            chain.EnvironmentId,
            $"{decision.ActorType}:{decision.ActorRef}",
            EventIds.Idempotency(
                "action-recorded",
                chain.OrganizationId,
                chain.EnvironmentId,
                chain.Id.ToString(),
                decision.RoundNo.ToString(),
                decision.StepNo.ToString(),
                decision.Decision,
                decision.ActorType,
                decision.ActorRef),
            new ApprovalActionRecordedPayload(
                chain.Id.ToString(),
                decision.StepId.ToString(),
                decision.StepNo,
                decision.Decision,
                decision.ActorType,
                decision.ActorRef,
                decision.Comment,
                domainEvent.SuggestedRecipientRefs,
                ApprovalIntegrationEventConverterHelpers.ToPayload(chain.DocumentReference)));
    }
}

public sealed class ApprovalApprovedIntegrationEventConverter
    : IIntegrationEventConverter<ApprovalApprovedDomainEvent, ApprovalCompletedIntegrationEvent>
{
    public ApprovalCompletedIntegrationEvent Convert(ApprovalApprovedDomainEvent domainEvent)
    {
        return ApprovalIntegrationEventConverterHelpers.ToCompletedEvent(domainEvent.Chain, domainEvent.Decision, ApprovalIntegrationEventTypes.ApprovalApproved, ApprovalChainStatuses.Approved);
    }
}

public sealed class ApprovalRejectedIntegrationEventConverter
    : IIntegrationEventConverter<ApprovalRejectedDomainEvent, ApprovalCompletedIntegrationEvent>
{
    public ApprovalCompletedIntegrationEvent Convert(ApprovalRejectedDomainEvent domainEvent)
    {
        return ApprovalIntegrationEventConverterHelpers.ToCompletedEvent(domainEvent.Chain, domainEvent.Decision, ApprovalIntegrationEventTypes.ApprovalRejected, ApprovalChainStatuses.Rejected);
    }
}

public sealed class ApprovalReturnedIntegrationEventConverter
    : IIntegrationEventConverter<ApprovalReturnedDomainEvent, ApprovalCompletedIntegrationEvent>
{
    public ApprovalCompletedIntegrationEvent Convert(ApprovalReturnedDomainEvent domainEvent)
    {
        return ApprovalIntegrationEventConverterHelpers.ToCompletedEvent(domainEvent.Chain, domainEvent.Decision, ApprovalIntegrationEventTypes.ApprovalReturned, ApprovalChainStatuses.Returned);
    }
}

internal static class ApprovalIntegrationEventConverterHelpers
{
    public static ApprovalDocumentReferencePayload ToPayload(ApprovalDocumentReference documentReference)
    {
        return new ApprovalDocumentReferencePayload(
            documentReference.SourceService,
            documentReference.DocumentType,
            documentReference.DocumentId,
            documentReference.DocumentLineId);
    }

    public static ApprovalCompletedIntegrationEvent ToCompletedEvent(
        ApprovalChain chain,
        ApprovalDecision decision,
        string eventType,
        string result)
    {
        var occurredAtUtc = DateTimeOffset.UtcNow;
        return new ApprovalCompletedIntegrationEvent(
            EventIds.New(),
            eventType,
            ApprovalIntegrationEventVersions.V1,
            occurredAtUtc,
            ApprovalIntegrationEventSources.BusinessApproval,
            chain.Id.ToString(),
            decision.Id.ToString(),
            chain.OrganizationId,
            chain.EnvironmentId,
            $"{decision.ActorType}:{decision.ActorRef}",
            EventIds.Idempotency(result, chain.OrganizationId, chain.EnvironmentId, chain.Id.ToString(), decision.RoundNo.ToString()),
            new ApprovalCompletedPayload(
                chain.Id.ToString(),
                result,
                decision.ActorType,
                decision.ActorRef,
                decision.OnBehalfOfActorType,
                decision.OnBehalfOfActorRef,
                ToPayload(chain.DocumentReference)));
    }
}

internal static class EventIds
{
    public static string New() => $"evt-{Guid.CreateVersion7():N}";

    public static string Idempotency(params string[] parts) => $"business-approval:{string.Join(':', parts)}";
}
