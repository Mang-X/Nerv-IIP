using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;

namespace Nerv.IIP.Business.Quality.Infrastructure.Repositories;

public interface INonconformanceReportRepository : IRepository<NonconformanceReport, NonconformanceReportId>
{
    Task<bool> CodeExistsAsync(string organizationId, string environmentId, string ncrCode, CancellationToken cancellationToken = default);

    Task<NonconformanceReport?> GetScopedAsync(
        NonconformanceReportId id,
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken = default);
}

public sealed class NonconformanceReportRepository(ApplicationDbContext context)
    : RepositoryBase<NonconformanceReport, NonconformanceReportId, ApplicationDbContext>(context), INonconformanceReportRepository
{
    public async Task<bool> CodeExistsAsync(string organizationId, string environmentId, string ncrCode, CancellationToken cancellationToken = default)
    {
        return await DbContext.NonconformanceReports.AnyAsync(
            x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.NcrCode == ncrCode,
            cancellationToken);
    }

    public Task<NonconformanceReport?> GetScopedAsync(
        NonconformanceReportId id,
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken = default) =>
        DbContext.NonconformanceReports.SingleOrDefaultAsync(
            x => x.Id == id &&
                x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId,
            cancellationToken);
}
