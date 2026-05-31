using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Queries;

public sealed record ListSchedulePlansQuery(string OrganizationId, string EnvironmentId)
    : IQuery<IReadOnlyCollection<SchedulePlanSummaryResponse>>;

public sealed record SchedulePlanSummaryResponse(
    string PlanId,
    string ProblemId,
    SchedulePlanStatusContract Status,
    DateTimeOffset GeneratedAtUtc,
    DateTimeOffset? ReleasedAtUtc,
    int AssignmentCount,
    int ConflictCount,
    int UnscheduledOperationCount);

public sealed class ListSchedulePlansQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListSchedulePlansQuery, IReadOnlyCollection<SchedulePlanSummaryResponse>>
{
    public async Task<IReadOnlyCollection<SchedulePlanSummaryResponse>> Handle(ListSchedulePlansQuery request, CancellationToken cancellationToken)
    {
        return await dbContext.SchedulePlans.AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .OrderByDescending(x => x.GeneratedAtUtc)
            .Select(x => new SchedulePlanSummaryResponse(
                x.PlanId,
                x.ProblemId,
                x.Status == Domain.AggregatesModel.SchedulePlanAggregate.SchedulePlanLifecycleStatus.Released
                    ? SchedulePlanStatusContract.Released
                    : SchedulePlanStatusContract.Generated,
                x.GeneratedAtUtc,
                x.ReleasedAtUtc,
                x.Assignments.Count,
                x.Conflicts.Count,
                x.UnscheduledOperations.Count))
            .ToListAsync(cancellationToken);
    }
}

public sealed record GetSchedulePlanDetailQuery(string PlanId, string OrganizationId, string EnvironmentId) : IQuery<SchedulePlanContract>;

public sealed class GetSchedulePlanDetailQueryValidator : AbstractValidator<GetSchedulePlanDetailQuery>
{
    public GetSchedulePlanDetailQueryValidator()
    {
        RuleFor(x => x.PlanId).NotEmpty().MaximumLength(128);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
    }
}

public sealed class GetSchedulePlanDetailQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetSchedulePlanDetailQuery, SchedulePlanContract>
{
    public async Task<SchedulePlanContract> Handle(GetSchedulePlanDetailQuery request, CancellationToken cancellationToken)
    {
        var plan = await dbContext.SchedulePlans.AsNoTracking()
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

        return SchedulePlanContractMapper.ToContract(plan);
    }
}

public sealed record GetSchedulePlanGanttQuery(string PlanId, string OrganizationId, string EnvironmentId) : IQuery<IReadOnlyCollection<GanttScheduleItemContract>>;

public sealed class GetSchedulePlanGanttQueryValidator : AbstractValidator<GetSchedulePlanGanttQuery>
{
    public GetSchedulePlanGanttQueryValidator()
    {
        RuleFor(x => x.PlanId).NotEmpty().MaximumLength(128);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
    }
}

public sealed class GetSchedulePlanGanttQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetSchedulePlanGanttQuery, IReadOnlyCollection<GanttScheduleItemContract>>
{
    public async Task<IReadOnlyCollection<GanttScheduleItemContract>> Handle(GetSchedulePlanGanttQuery request, CancellationToken cancellationToken)
    {
        var plan = await dbContext.SchedulePlans.AsNoTracking()
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

        return SchedulePlanContractMapper.ToContract(plan).GanttItems;
    }
}
