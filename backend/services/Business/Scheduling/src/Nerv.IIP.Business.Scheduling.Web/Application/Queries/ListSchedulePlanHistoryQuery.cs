using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Queries;

// ReleasedOn is a UTC calendar date. Unreleased plans follow released plans, ordered by generation time.
public sealed record ListSchedulePlanHistoryQuery(
    string OrganizationId,
    string EnvironmentId,
    int PageIndex = 0,
    int PageSize = 100,
    SchedulePlanStatusContract? Status = null,
    DateOnly? ReleasedOn = null,
    bool? IsInvalidated = null) : IQuery<SchedulePlanHistoryResponse>;

public sealed record SchedulePlanHistoryResponse(IReadOnlyCollection<SchedulePlanSummaryResponse> Items, int Total);

public sealed class ListSchedulePlanHistoryQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListSchedulePlanHistoryQuery, SchedulePlanHistoryResponse>
{
    public async Task<SchedulePlanHistoryResponse> Handle(ListSchedulePlanHistoryQuery request, CancellationToken cancellationToken)
    {
        var candidates = dbContext.SchedulePlans.AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId);
        if (request.Status is { } status)
        {
            var lifecycleStatus = status switch
            {
                SchedulePlanStatusContract.Generated => SchedulePlanLifecycleStatus.Generated,
                SchedulePlanStatusContract.Released => SchedulePlanLifecycleStatus.Released,
                SchedulePlanStatusContract.Superseded => SchedulePlanLifecycleStatus.Superseded,
                SchedulePlanStatusContract.Revoked => SchedulePlanLifecycleStatus.Revoked,
                _ => throw new ArgumentOutOfRangeException(nameof(request.Status))
            };
            candidates = candidates.Where(x => x.Status == lifecycleStatus);
        }
        if (request.ReleasedOn is { } releasedOn)
        {
            var start = new DateTimeOffset(releasedOn.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var end = start.AddDays(1);
            candidates = candidates.Where(x => x.ReleasedAtUtc >= start && x.ReleasedAtUtc < end);
        }
        if (request.IsInvalidated is { } isInvalidated)
        {
            candidates = candidates.Where(plan => dbContext.SchedulePlanInvalidations.Any(invalidation =>
                invalidation.OrganizationId == request.OrganizationId &&
                invalidation.EnvironmentId == request.EnvironmentId &&
                invalidation.PlanId == plan.PlanId) == isInvalidated);
        }

        var total = await candidates.CountAsync(cancellationToken);
        var plans = await candidates
            .OrderByDescending(x => x.ReleasedAtUtc.HasValue)
            .ThenByDescending(x => x.ReleasedAtUtc)
            .ThenByDescending(x => x.GeneratedAtUtc)
            .ThenBy(x => x.PlanId)
            .Skip(request.PageIndex * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new SchedulePlanSummaryResponse(
                x.PlanId, x.ProblemId, SchedulePlanContractMapper.ToContractStatus(x.Status),
                x.GeneratedAtUtc, x.ReleasedAtUtc, x.Assignments.Count, x.Conflicts.Count, x.UnscheduledOperations.Count,
                false, null, null,
                dbContext.ScheduleProblems.Where(problem => problem.OrganizationId == request.OrganizationId &&
                    problem.EnvironmentId == request.EnvironmentId && problem.ProblemId == x.ProblemId)
                    .Select(problem => (DateTimeOffset?)problem.HorizonStartUtc).FirstOrDefault(),
                dbContext.ScheduleProblems.Where(problem => problem.OrganizationId == request.OrganizationId &&
                    problem.EnvironmentId == request.EnvironmentId && problem.ProblemId == x.ProblemId)
                    .Select(problem => (DateTimeOffset?)problem.HorizonEndUtc).FirstOrDefault()))
            .ToListAsync(cancellationToken);
        var items = await ListSchedulePlansQueryHandler.EnrichInvalidationsAsync(
            dbContext, request.OrganizationId, request.EnvironmentId, plans, cancellationToken);
        return new SchedulePlanHistoryResponse(items, total);
    }
}
