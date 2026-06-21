using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalChainAggregate;

namespace Nerv.IIP.Business.Approval.Domain.DomainEvents;

public sealed record ApprovalStartedDomainEvent(ApprovalChain Chain) : IDomainEvent;

public sealed record ApprovalStepResolvedDomainEvent(
    ApprovalChain Chain,
    ApprovalStep Step,
    ApprovalDecision Decision) : IDomainEvent;

public sealed record ApprovalApprovedDomainEvent(
    ApprovalChain Chain,
    ApprovalDecision Decision) : IDomainEvent;

public sealed record ApprovalRejectedDomainEvent(
    ApprovalChain Chain,
    ApprovalDecision Decision) : IDomainEvent;

public sealed record ApprovalReturnedDomainEvent(
    ApprovalChain Chain,
    ApprovalDecision Decision) : IDomainEvent;

public sealed record ApprovalStepOverdueDomainEvent(
    ApprovalChain Chain,
    ApprovalStep Step,
    DateTimeOffset MarkedAtUtc) : IDomainEvent;

public sealed record ApprovalChainActionRecordedDomainEvent(
    ApprovalChain Chain,
    ApprovalDecision Decision,
    IReadOnlyCollection<string> SuggestedRecipientRefs) : IDomainEvent;
