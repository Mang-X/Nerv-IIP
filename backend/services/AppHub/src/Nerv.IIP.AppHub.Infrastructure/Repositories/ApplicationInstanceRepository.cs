using Microsoft.EntityFrameworkCore;
using Nerv.IIP.AppHub.Domain.AggregatesModel.ApplicationInstanceAggregate;
using NetCorePal.Extensions.Repository;
using NetCorePal.Extensions.Repository.EntityFrameworkCore;

namespace Nerv.IIP.AppHub.Infrastructure.Repositories;

public interface IApplicationInstanceRepository : IRepository<ApplicationInstance, ApplicationInstanceId>
{
    Task<ApplicationInstance?> GetByInstanceKeyAsync(string instanceKey, CancellationToken cancellationToken = default);
    Task<ApplicationInstance?> GetByContextAsync(string organizationId, string environmentId, string instanceKey, CancellationToken cancellationToken = default);
}

public class ApplicationInstanceRepository(ApplicationDbContext context)
    : RepositoryBase<ApplicationInstance, ApplicationInstanceId, ApplicationDbContext>(context), IApplicationInstanceRepository
{
    private readonly ApplicationDbContext _context = context;

    public async Task<ApplicationInstance?> GetByInstanceKeyAsync(string instanceKey, CancellationToken cancellationToken = default)
    {
        return await WithAggregate()
            .FirstOrDefaultAsync(x => x.InstanceKey == instanceKey, cancellationToken);
    }

    public async Task<ApplicationInstance?> GetByContextAsync(string organizationId, string environmentId, string instanceKey, CancellationToken cancellationToken = default)
    {
        return await WithAggregate()
            .FirstOrDefaultAsync(x =>
                x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.InstanceKey == instanceKey,
                cancellationToken);
    }

    private IQueryable<ApplicationInstance> WithAggregate()
    {
        return _context.ApplicationInstances
            .Include(x => x.Heartbeat)
            .Include(x => x.CollectionHealth)
            .Include(x => x.StateHistory)
            .Include(x => x.StatusChanges);
    }
}
