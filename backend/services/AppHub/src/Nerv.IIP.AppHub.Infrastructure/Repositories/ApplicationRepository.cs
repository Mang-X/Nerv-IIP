using Microsoft.EntityFrameworkCore;
using NetCorePal.Extensions.Repository;
using NetCorePal.Extensions.Repository.EntityFrameworkCore;
using AppHubApplication = Nerv.IIP.AppHub.Domain.AggregatesModel.ApplicationAggregate.Application;
using AppHubApplicationId = Nerv.IIP.AppHub.Domain.AggregatesModel.ApplicationAggregate.ApplicationId;

namespace Nerv.IIP.AppHub.Infrastructure.Repositories;

public interface IApplicationRepository : IRepository<AppHubApplication, AppHubApplicationId>
{
    Task<AppHubApplication?> GetByKeyAsync(string organizationId, string environmentId, string applicationKey, CancellationToken cancellationToken = default);
}

public class ApplicationRepository(ApplicationDbContext context)
    : RepositoryBase<AppHubApplication, AppHubApplicationId, ApplicationDbContext>(context), IApplicationRepository
{
    private readonly ApplicationDbContext _context = context;

    public async Task<AppHubApplication?> GetByKeyAsync(string organizationId, string environmentId, string applicationKey, CancellationToken cancellationToken = default)
    {
        return await _context.Applications
            .Include(x => x.Versions)
            .FirstOrDefaultAsync(x =>
                x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.ApplicationKey == applicationKey,
                cancellationToken);
    }
}
