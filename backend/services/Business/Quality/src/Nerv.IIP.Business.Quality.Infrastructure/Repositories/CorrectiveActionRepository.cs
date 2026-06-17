using Nerv.IIP.Business.Quality.Domain.AggregatesModel.CorrectiveActionAggregate;

namespace Nerv.IIP.Business.Quality.Infrastructure.Repositories;

public interface ICorrectiveActionRepository : IRepository<CorrectiveAction, CorrectiveActionId>
{
    Task<CorrectiveAction?> GetWithActionsAsync(CorrectiveActionId id, CancellationToken cancellationToken);
}

public sealed class CorrectiveActionRepository(ApplicationDbContext dbContext)
    : RepositoryBase<CorrectiveAction, CorrectiveActionId, ApplicationDbContext>(dbContext), ICorrectiveActionRepository
{
    public Task<CorrectiveAction?> GetWithActionsAsync(CorrectiveActionId id, CancellationToken cancellationToken)
    {
        return DbContext.CorrectiveActions
            .Include(x => x.Actions)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    }
}
