using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalDelegationAggregate;
using Nerv.IIP.Business.Approval.Web.Application.Validation;

namespace Nerv.IIP.Business.Approval.Web.Application.Commands.Delegations;

public sealed record RevokeApprovalDelegationCommand(
    ApprovalDelegationId DelegationId,
    string OrganizationId,
    string EnvironmentId,
    string RevokedBy) : ICommand;

public sealed class RevokeApprovalDelegationCommandValidator : AbstractValidator<RevokeApprovalDelegationCommand>
{
    public RevokeApprovalDelegationCommandValidator()
    {
        RuleFor(x => x.DelegationId).NotEmpty();
        RuleFor(x => x.OrganizationId).RequiredApprovalCode(100);
        RuleFor(x => x.EnvironmentId).RequiredApprovalCode(100);
        RuleFor(x => x.RevokedBy).RequiredApprovalCode(150);
    }
}

public sealed class RevokeApprovalDelegationCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<RevokeApprovalDelegationCommand>
{
    public async Task Handle(RevokeApprovalDelegationCommand request, CancellationToken cancellationToken)
    {
        var delegation = await dbContext.ApprovalDelegations
            .SingleOrDefaultAsync(x =>
                x.Id == request.DelegationId
                && x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId,
                cancellationToken)
            ?? throw new KnownException("Approval delegation was not found.");
        delegation.Revoke(request.RevokedBy);
    }
}
