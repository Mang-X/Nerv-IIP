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
