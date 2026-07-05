using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionPlanAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionTaskAggregate;
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
    Task<InspectionRecord?> FindBySourceDocumentAsync(
        string organizationId,
        string environmentId,
        string sourceType,
        string sourceService,
        string skuCode,
        string sourceDocumentId,
        CancellationToken cancellationToken = default);
}

public sealed class InspectionRecordRepository(ApplicationDbContext context)
    : RepositoryBase<InspectionRecord, InspectionRecordId, ApplicationDbContext>(context), IInspectionRecordRepository
{
    public Task<InspectionRecord?> FindBySourceDocumentAsync(
        string organizationId,
        string environmentId,
        string sourceType,
        string sourceService,
        string skuCode,
        string sourceDocumentId,
        CancellationToken cancellationToken = default)
    {
        var normalizedSourceType = sourceType.Trim().ToLowerInvariant();
        var normalizedSourceService = sourceService.Trim().ToLowerInvariant();
        var normalizedSkuCode = skuCode.Trim();
        var normalizedSourceDocumentId = sourceDocumentId.Trim();
        return DbContext.InspectionRecords.SingleOrDefaultAsync(
            x => x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.SourceType == normalizedSourceType
                && x.SourceService == normalizedSourceService
                && x.SkuCode == normalizedSkuCode
                && x.SourceDocumentId == normalizedSourceDocumentId,
            cancellationToken);
    }
}

public interface IInspectionTaskRepository : IRepository<InspectionTask, InspectionTaskId>
{
    new Task<InspectionTask?> GetAsync(
        InspectionTaskId id,
        CancellationToken cancellationToken = default);

    Task<InspectionTask?> FindOpenBySourceAsync(
        string organizationId,
        string environmentId,
        string sourceType,
        string sourceService,
        string sourceDocumentId,
        string skuCode,
        CancellationToken cancellationToken = default);
}

public sealed class InspectionTaskRepository(ApplicationDbContext context)
    : RepositoryBase<InspectionTask, InspectionTaskId, ApplicationDbContext>(context), IInspectionTaskRepository
{
    public new Task<InspectionTask?> GetAsync(InspectionTaskId id, CancellationToken cancellationToken = default)
    {
        return DbContext.InspectionTasks.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public Task<InspectionTask?> FindOpenBySourceAsync(
        string organizationId,
        string environmentId,
        string sourceType,
        string sourceService,
        string sourceDocumentId,
        string skuCode,
        CancellationToken cancellationToken = default)
    {
        var normalizedSourceType = sourceType.Trim().ToLowerInvariant();
        var normalizedSourceService = sourceService.Trim().ToLowerInvariant();
        var normalizedDocumentId = sourceDocumentId.Trim();
        var normalizedSkuCode = skuCode.Trim();
        return DbContext.InspectionTasks
            .Where(
            x => x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                x.SourceType == normalizedSourceType &&
                x.SourceService == normalizedSourceService &&
                x.SourceDocumentId == normalizedDocumentId &&
                x.SkuCode == normalizedSkuCode &&
                x.Status != InspectionTaskStatuses.Completed)
            .OrderBy(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }
}

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
