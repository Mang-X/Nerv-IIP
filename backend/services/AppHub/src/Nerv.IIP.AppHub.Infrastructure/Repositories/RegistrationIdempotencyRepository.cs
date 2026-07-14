using Microsoft.EntityFrameworkCore;
using Nerv.IIP.AppHub.Domain.AggregatesModel.ApplicationInstanceAggregate;
using NetCorePal.Extensions.Repository;
using NetCorePal.Extensions.Repository.EntityFrameworkCore;

namespace Nerv.IIP.AppHub.Infrastructure.Repositories;

public interface IRegistrationIdempotencyRepository : IRepository<RegistrationIdempotency, RegistrationIdempotencyId>
{
    Task<RegistrationIdempotency?> GetByKeyAsync(string organizationId, string environmentId, string idempotencyKey, CancellationToken cancellationToken = default);
}

public class RegistrationIdempotencyRepository(ApplicationDbContext context)
    : RepositoryBase<RegistrationIdempotency, RegistrationIdempotencyId, ApplicationDbContext>(context), IRegistrationIdempotencyRepository
{
    private readonly ApplicationDbContext _context = context;

    public async Task<RegistrationIdempotency?> GetByKeyAsync(string organizationId, string environmentId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        return await _context.RegistrationIdempotency.FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.IdempotencyKey == idempotencyKey, cancellationToken);
    }
}
