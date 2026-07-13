using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
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
            .SingleOrDefaultAsync(
                x => x.PlanId == request.PlanId &&
                    x.OrganizationId == request.OrganizationId &&
                    x.EnvironmentId == request.EnvironmentId,
                cancellationToken)
            ?? throw new KnownException($"Schedule plan was not found, PlanId = {request.PlanId}");

        var hasErrorConflict = await dbContext.Set<SchedulePlanConflict>()
            .AnyAsync(
                x => x.SchedulePlanId == plan.Id &&
                    x.Severity == ScheduleConflictSeverity.Error,
                cancellationToken);
        var hasUnscheduledOperation = await dbContext.Set<SchedulePlanUnscheduledOperation>()
            .AnyAsync(x => x.SchedulePlanId == plan.Id, cancellationToken);
        if (hasErrorConflict || hasUnscheduledOperation)
        {
            throw new KnownException("Schedule plan cannot be released because it contains error conflicts or unscheduled operations.");
        }

        // A plan whose scheduling inputs have since changed (recorded as an invalidation) is stale and
        // must be regenerated before release — releasing it would dispatch an out-of-date schedule.
        var isInvalidated = await dbContext.SchedulePlanInvalidations
            .AnyAsync(
                x => x.OrganizationId == request.OrganizationId &&
                    x.EnvironmentId == request.EnvironmentId &&
                    x.PlanId == request.PlanId,
                cancellationToken);
        if (isInvalidated)
        {
            throw new KnownException("Schedule plan cannot be released because it has been invalidated by a scheduling input change; regenerate the plan first.");
        }

        plan.Release(timeProvider.GetUtcNow());
        return new ReleaseSchedulePlanResponse(
            plan.PlanId,
            SchedulePlanStatusContract.Released,
            plan.ReleasedAtUtc);
    }
}
