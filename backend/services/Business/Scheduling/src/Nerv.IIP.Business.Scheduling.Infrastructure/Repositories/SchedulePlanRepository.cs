using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;

namespace Nerv.IIP.Business.Scheduling.Infrastructure.Repositories;

public interface ISchedulePlanRepository : IRepository<SchedulePlan, SchedulePlanId>
{
    Task<SchedulePlan?> GetByPlanIdWithDetailsAsync(
        string planId,
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken);
}

public sealed class SchedulePlanRepository(ApplicationDbContext context)
    : RepositoryBase<SchedulePlan, SchedulePlanId, ApplicationDbContext>(context), ISchedulePlanRepository
{
    public async Task<SchedulePlan?> GetByPlanIdWithDetailsAsync(
        string planId,
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken)
    {
        return await DbContext.SchedulePlans
            .Include(x => x.Assignments)
            .Include(x => x.ResourceLoads)
            .Include(x => x.Conflicts)
            .Include(x => x.UnscheduledOperations)
            .AsSplitQuery()
            .SingleOrDefaultAsync(
                x => x.PlanId == planId &&
                    x.OrganizationId == organizationId &&
                    x.EnvironmentId == environmentId,
                cancellationToken);
    }
}
