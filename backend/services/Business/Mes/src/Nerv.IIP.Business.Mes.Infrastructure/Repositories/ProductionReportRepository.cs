using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;

namespace Nerv.IIP.Business.Mes.Infrastructure.Repositories;

public interface IProductionReportRepository : IRepository<ProductionReport, ProductionReportId>
{
    Task<IReadOnlyCollection<ProductionReport>> GetByWorkOrderAsync(
        string organizationId,
        string environmentId,
        string workOrderId,
        CancellationToken cancellationToken = default);
}

public sealed class ProductionReportRepository(ApplicationDbContext context)
    : RepositoryBase<ProductionReport, ProductionReportId, ApplicationDbContext>(context), IProductionReportRepository
{
    public async Task<IReadOnlyCollection<ProductionReport>> GetByWorkOrderAsync(
        string organizationId,
        string environmentId,
        string workOrderId,
        CancellationToken cancellationToken = default)
    {
        return await DbContext.ProductionReports
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                x.WorkOrderId == workOrderId)
            .OrderBy(x => x.ReportedAtUtc)
            .ToListAsync(cancellationToken);
    }
}
