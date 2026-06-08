using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalDelegationAggregate;
using Nerv.IIP.Business.Approval.Web.Application.Validation;

namespace Nerv.IIP.Business.Approval.Web.Application.Commands.Delegations;

public sealed record CreateApprovalDelegationCommand(
    string OrganizationId,
    string EnvironmentId,
    string DelegatorActorType,
    string DelegatorActorRef,
    string DelegateActorType,
    string DelegateActorRef,
    string? DocumentType,
    DateTimeOffset EffectiveFromUtc,
    DateTimeOffset EffectiveToUtc,
    string? Reason,
    string CreatedBy) : ICommand<ApprovalDelegationId>;

public sealed class CreateApprovalDelegationCommandValidator : AbstractValidator<CreateApprovalDelegationCommand>
{
    public CreateApprovalDelegationCommandValidator()
    {
        RuleFor(x => x.OrganizationId).RequiredApprovalCode(100);
        RuleFor(x => x.EnvironmentId).RequiredApprovalCode(100);
        RuleFor(x => x.DelegatorActorType).RequiredApprovalCode(50);
        RuleFor(x => x.DelegatorActorRef).RequiredApprovalCode(150);
        RuleFor(x => x.DelegateActorType).RequiredApprovalCode(50);
        RuleFor(x => x.DelegateActorRef).RequiredApprovalCode(150);
        RuleFor(x => x.DocumentType).OptionalApprovalCode(100);
        RuleFor(x => x.EffectiveToUtc).GreaterThan(x => x.EffectiveFromUtc);
        RuleFor(x => x.Reason).MaximumLength(500);
        RuleFor(x => x.CreatedBy).RequiredApprovalCode(150);
    }
}

public sealed class CreateApprovalDelegationCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<CreateApprovalDelegationCommand, ApprovalDelegationId>
{
    public Task<ApprovalDelegationId> Handle(CreateApprovalDelegationCommand request, CancellationToken cancellationToken)
    {
        var delegation = ApprovalDelegation.Create(
            request.OrganizationId,
            request.EnvironmentId,
            request.DelegatorActorType,
            request.DelegatorActorRef,
            request.DelegateActorType,
            request.DelegateActorRef,
            request.DocumentType,
            request.EffectiveFromUtc,
            request.EffectiveToUtc,
            request.Reason,
            request.CreatedBy);
        dbContext.ApprovalDelegations.Add(delegation);
        return Task.FromResult(delegation.Id);
    }
}
