using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionPlanAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.QualityReasonAggregate;

namespace Nerv.IIP.Business.Quality.Infrastructure.Repositories;

public interface IInspectionPlanRepository : IRepository<InspectionPlan, InspectionPlanId>
{
    Task<bool> CodeExistsAsync(string organizationId, string environmentId, string planCode, CancellationToken cancellationToken = default);
    Task<InspectionPlan?> GetWithCharacteristicsAsync(
        string organizationId,
        string environmentId,
        InspectionPlanId id,
        CancellationToken cancellationToken = default);
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

    public Task<InspectionPlan?> GetWithCharacteristicsAsync(
        string organizationId,
        string environmentId,
        InspectionPlanId id,
        CancellationToken cancellationToken = default)
    {
        return DbContext.InspectionPlans
            .Include(x => x.Characteristics)
            .SingleOrDefaultAsync(
                x => x.OrganizationId == organizationId
                    && x.EnvironmentId == environmentId
                    && x.Id == id,
                cancellationToken);
    }
}

public interface IInspectionRecordRepository : IRepository<InspectionRecord, InspectionRecordId>
{
}

public sealed class InspectionRecordRepository(ApplicationDbContext context)
    : RepositoryBase<InspectionRecord, InspectionRecordId, ApplicationDbContext>(context), IInspectionRecordRepository;

public interface IQualityReasonRepository : IRepository<QualityReason, QualityReasonId>
{
    Task<bool> ExistsAsync(string organizationId, string environmentId, string reasonCode, CancellationToken cancellationToken = default);

    Task<QualityReason?> GetByCodeAsync(string organizationId, string environmentId, string reasonCode, CancellationToken cancellationToken = default);
}

public sealed class QualityReasonRepository(ApplicationDbContext context)
    : RepositoryBase<QualityReason, QualityReasonId, ApplicationDbContext>(context), IQualityReasonRepository
{
    public async Task<bool> ExistsAsync(string organizationId, string environmentId, string reasonCode, CancellationToken cancellationToken = default)
    {
        return await DbContext.QualityReasons.AnyAsync(
            x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.ReasonCode == reasonCode,
            cancellationToken);
    }

    public async Task<QualityReason?> GetByCodeAsync(string organizationId, string environmentId, string reasonCode, CancellationToken cancellationToken = default)
    {
        return await DbContext.QualityReasons.SingleOrDefaultAsync(
            x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.ReasonCode == reasonCode,
            cancellationToken);
    }
}
