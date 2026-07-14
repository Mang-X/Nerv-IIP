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
        // Select exactly one row per in-page plan at the database with GROUP BY plan + the newest row under a
        // strict total order (RecordedAtUtc, OccurredAtUtc, SourceEventType, SourceEventId). Within a plan group
        // (org/env/plan fixed) the (SourceEventType, SourceEventId) pair is unique — it is the rest of the
        // ux_schedule_plan_invalidations_source_event index — so the order is a true total order and even events
        // sharing an identical (RecordedAtUtc, OccurredAtUtc) collapse to a single deterministic DB row instead of
        // paging the whole tied history into memory (the candidate set stays strictly bounded to the plan count as
        // the append-only history grows). The tie-break columns are string/timestamp (translatable + IComparable on
        // every provider), avoiding the strongly-typed-id/uuid-vs-Guid ordering mismatch. EF translates the ordered
        // group-First to a window/lateral subquery on a relational provider; the InMemory handler test
        // client-evaluates it (SQLite cannot sort DateTimeOffset), and the conditional PostgreSQL profile test
        // verifies the real translation + the exact-timestamp-tie determinism.
        var planIds = plans.Select(x => x.PlanId).ToArray();
        var latest = await dbContext.SchedulePlanInvalidations.AsNoTracking()
            .Where(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                planIds.Contains(x.PlanId))
            .GroupBy(x => x.PlanId)
            .Select(group => group
                .OrderByDescending(x => x.RecordedAtUtc)
                .ThenByDescending(x => x.OccurredAtUtc)
                .ThenByDescending(x => x.SourceEventType)
                .ThenByDescending(x => x.SourceEventId)
                .Select(x => new LatestInvalidation(x.PlanId, x.ReasonCode, x.OccurredAtUtc))
                .First())
            .ToListAsync(cancellationToken);
        if (latest.Count == 0)
        {
            return plans;
        }

        var latestByPlan = latest.ToDictionary(x => x.PlanId, StringComparer.Ordinal);

        return plans
            .Select(plan => latestByPlan.TryGetValue(plan.PlanId, out var invalidation)
                ? plan with
                {
                    IsInvalidated = true,
                    LatestInvalidationReasonCode = invalidation.ReasonCode,
                    LatestInvalidatedAtUtc = invalidation.OccurredAtUtc,
                }
                : plan)
            .ToList();
    }

    private sealed record LatestInvalidation(
        string PlanId,
        string ReasonCode,
        DateTimeOffset OccurredAtUtc);
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
