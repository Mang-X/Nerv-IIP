using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionPlanAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;

namespace Nerv.IIP.Business.Quality.Infrastructure.Repositories;

public interface IInspectionPlanRepository : IRepository<InspectionPlan, InspectionPlanId>
{
    Task<bool> CodeExistsAsync(string organizationId, string environmentId, string planCode, CancellationToken cancellationToken = default);
}

public sealed class InspectionPlanRepository(ApplicationDbContext context)
    : RepositoryBase<InspectionPlan, InspectionPlanId, ApplicationDbContext>(context), IInspectionPlanRepository
{
    public async Task<bool> CodeExistsAsync(string organizationId, string environmentId, string planCode, CancellationToken cancellationToken = default)
    {
        return await DbContext.InspectionPlans.AnyAsync(
            x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.PlanCode == planCode,
            cancellationToken);
    }
}

public interface IInspectionRecordRepository : IRepository<InspectionRecord, InspectionRecordId>
{
}

public sealed class InspectionRecordRepository(ApplicationDbContext context)
    : RepositoryBase<InspectionRecord, InspectionRecordId, ApplicationDbContext>(context), IInspectionRecordRepository;
