using Nerv.IIP.Business.Quality.Domain.AggregatesModel.CorrectiveActionAggregate;

namespace Nerv.IIP.Business.Quality.Infrastructure.Repositories;

public interface ICorrectiveActionRepository : IRepository<CorrectiveAction, CorrectiveActionId>
{
    Task<CorrectiveAction?> GetWithActionsAsync(CorrectiveActionId id, CancellationToken cancellationToken);

    Task<bool> HasCapaForNcrAsync(string sourceNcrId, CancellationToken cancellationToken = default);

    Task<bool> HasEffectiveCapaForNcrAsync(string sourceNcrId, CancellationToken cancellationToken = default);
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

    public Task<bool> HasEffectiveCapaForNcrAsync(string sourceNcrId, CancellationToken cancellationToken = default)
    {
        return DbContext.CorrectiveActions.AnyAsync(
            x => x.SourceNcrId == sourceNcrId
                && (x.Status == "effectiveness-verified" || x.Status == "closed"),
            cancellationToken);
    }

    public Task<bool> HasCapaForNcrAsync(string sourceNcrId, CancellationToken cancellationToken = default)
    {
        return DbContext.CorrectiveActions.AnyAsync(x => x.SourceNcrId == sourceNcrId, cancellationToken);
    }
}
