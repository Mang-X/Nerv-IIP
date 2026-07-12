using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Queries;

public sealed record ListSchedulePlansQuery(
    string OrganizationId,
    string EnvironmentId,
    int? PageIndex = null,
    int? PageSize = null)
    : IQuery<IReadOnlyCollection<SchedulePlanSummaryResponse>>;

public sealed record SchedulePlanSummaryResponse(
    string PlanId,
    string ProblemId,
    SchedulePlanStatusContract Status,
    DateTimeOffset GeneratedAtUtc,
    DateTimeOffset? ReleasedAtUtc,
    int AssignmentCount,
    int ConflictCount,
    int UnscheduledOperationCount,
    bool IsInvalidated = false,
    string? LatestInvalidationReasonCode = null,
    DateTimeOffset? LatestInvalidatedAtUtc = null);

public sealed class ListSchedulePlansQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListSchedulePlansQuery, IReadOnlyCollection<SchedulePlanSummaryResponse>>
{
    public const int DefaultPageSize = 100;
    public const int MaxPageSize = 100;

    public async Task<IReadOnlyCollection<SchedulePlanSummaryResponse>> Handle(ListSchedulePlansQuery request, CancellationToken cancellationToken)
    {
        var pageIndex = Math.Max(request.PageIndex ?? 0, 0);
        var pageSize = Math.Clamp(request.PageSize ?? DefaultPageSize, 1, MaxPageSize);

        var plans = await dbContext.SchedulePlans.AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .OrderByDescending(x => x.GeneratedAtUtc)
            .Skip(pageIndex * pageSize)
            .Take(pageSize)
            .Select(x => new SchedulePlanSummaryResponse(
                x.PlanId,
                x.ProblemId,
                SchedulePlanContractMapper.ToContractStatus(x.Status),
                x.GeneratedAtUtc,
                x.ReleasedAtUtc,
                x.Assignments.Count,
                x.Conflicts.Count,
                x.UnscheduledOperations.Count))
            .ToListAsync(cancellationToken);

        if (plans.Count == 0)
        {
            return plans;
        }

        // Invalidation is a separate append-only projection keyed by plan id (no navigation from SchedulePlan).
        // Resolve the latest invalidation per plan with a bounded second query + in-memory grouping so the
        // projection stays translatable on every provider (the query handler tests run on EF InMemory, which
        // does not translate correlated OrderBy + FirstOrDefault subqueries).
        var planIds = plans.Select(x => x.PlanId).ToArray();
        var invalidations = await dbContext.SchedulePlanInvalidations.AsNoTracking()
            .Where(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                planIds.Contains(x.PlanId))
            .Select(x => new { x.PlanId, x.ReasonCode, x.OccurredAtUtc, x.RecordedAtUtc })
            .ToListAsync(cancellationToken);
        if (invalidations.Count == 0)
        {
            return plans;
        }

        var latestByPlan = invalidations
            .GroupBy(x => x.PlanId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(x => x.RecordedAtUtc)
                    .ThenByDescending(x => x.OccurredAtUtc)
                    .First(),
                StringComparer.Ordinal);

        return plans
            .Select(plan => latestByPlan.TryGetValue(plan.PlanId, out var latest)
                ? plan with
                {
                    IsInvalidated = true,
                    LatestInvalidationReasonCode = latest.ReasonCode,
                    LatestInvalidatedAtUtc = latest.OccurredAtUtc,
                }
                : plan)
            .ToList();
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
            .AsSplitQuery()
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
            .AsSplitQuery()
            .SingleOrDefaultAsync(
                x => x.PlanId == request.PlanId &&
                    x.OrganizationId == request.OrganizationId &&
                    x.EnvironmentId == request.EnvironmentId,
                cancellationToken)
            ?? throw new KnownException($"Schedule plan was not found, PlanId = {request.PlanId}");

        return SchedulePlanContractMapper.ToContract(plan).GanttItems;
    }
}
