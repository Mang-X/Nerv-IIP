using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalChainAggregate;

namespace Nerv.IIP.Business.Approval.Web.Application.Commands.Chains;

public sealed record CheckOverdueApprovalStepsCommand(
    string OrganizationId,
    string EnvironmentId) : ICommand<int>;

public sealed class CheckOverdueApprovalStepsCommandValidator : AbstractValidator<CheckOverdueApprovalStepsCommand>
{
    public CheckOverdueApprovalStepsCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
    }
}

public interface IApprovalClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemApprovalClock : IApprovalClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed class CheckOverdueApprovalStepsCommandHandler(ApplicationDbContext dbContext, IApprovalClock clock)
    : ICommandHandler<CheckOverdueApprovalStepsCommand, int>
{
    public async Task<int> Handle(CheckOverdueApprovalStepsCommand request, CancellationToken cancellationToken)
    {
        var nowUtc = clock.UtcNow;
        var chains = await dbContext.ApprovalChains
            .Include(x => x.Steps)
            .Where(x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.Status == ApprovalChainStatuses.Pending
                && x.Steps.Any(step =>
                    step.Status == ApprovalStepStatuses.Pending
                    && step.OverdueNotifiedAtUtc == null
                    && step.DueAtUtc != null
                    && step.DueAtUtc <= nowUtc))
            .ToListAsync(cancellationToken);

        return chains.Sum(chain => chain.MarkOverdueSteps(nowUtc));
    }
}
