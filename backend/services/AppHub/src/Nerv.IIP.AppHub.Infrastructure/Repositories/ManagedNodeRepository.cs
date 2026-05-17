using Microsoft.EntityFrameworkCore;
using Nerv.IIP.AppHub.Domain.AggregatesModel.ManagedNodeAggregate;
using NetCorePal.Extensions.Repository;
using NetCorePal.Extensions.Repository.EntityFrameworkCore;

namespace Nerv.IIP.AppHub.Infrastructure.Repositories;

public interface IManagedNodeRepository : IRepository<ManagedNode, ManagedNodeId>
{
    Task<ManagedNode?> GetByKeyAsync(string organizationId, string environmentId, string nodeKey, CancellationToken cancellationToken = default);
}

public class ManagedNodeRepository(ApplicationDbContext context)
    : RepositoryBase<ManagedNode, ManagedNodeId, ApplicationDbContext>(context), IManagedNodeRepository
{
    private readonly ApplicationDbContext _context = context;

    public async Task<ManagedNode?> GetByKeyAsync(string organizationId, string environmentId, string nodeKey, CancellationToken cancellationToken = default)
    {
        return await _context.ManagedNodes.FirstOrDefaultAsync(x =>
            x.OrganizationId == organizationId
            && x.EnvironmentId == environmentId
            && x.NodeKey == nodeKey,
            cancellationToken);
    }
}
