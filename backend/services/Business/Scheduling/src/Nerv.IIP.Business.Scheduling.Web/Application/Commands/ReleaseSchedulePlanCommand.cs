using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Scheduling.Web.Application.Queries;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Commands;

public sealed record ReleaseSchedulePlanCommand(string PlanId, string OrganizationId, string EnvironmentId) : ICommand<ReleaseSchedulePlanResponse>;

public sealed record ReleaseSchedulePlanResponse(
    string PlanId,
    SchedulePlanStatusContract Status,
    DateTimeOffset? ReleasedAtUtc);

public sealed class ReleaseSchedulePlanCommandValidator : AbstractValidator<ReleaseSchedulePlanCommand>
{
    public ReleaseSchedulePlanCommandValidator()
    {
        RuleFor(x => x.PlanId).NotEmpty().MaximumLength(128);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
    }
}

public sealed class ReleaseSchedulePlanCommandHandler(ApplicationDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<ReleaseSchedulePlanCommand, ReleaseSchedulePlanResponse>
{
    public async Task<ReleaseSchedulePlanResponse> Handle(ReleaseSchedulePlanCommand request, CancellationToken cancellationToken)
    {
        var plan = await dbContext.SchedulePlans
            .Include(x => x.Assignments)
            .Include(x => x.ResourceLoads)
            .Include(x => x.Conflicts)
            .Include(x => x.UnscheduledOperations)
            .SingleOrDefaultAsync(
                x => x.PlanId == request.PlanId &&
                    x.OrganizationId == request.OrganizationId &&
                    x.EnvironmentId == request.EnvironmentId,
                cancellationToken)
            ?? throw new KnownException($"Schedule plan was not found, PlanId = {request.PlanId}");

        plan.Release(timeProvider.GetUtcNow());
        return new ReleaseSchedulePlanResponse(
            plan.PlanId,
            SchedulePlanContractMapper.ToContract(plan).Status,
            plan.ReleasedAtUtc);
    }
}
