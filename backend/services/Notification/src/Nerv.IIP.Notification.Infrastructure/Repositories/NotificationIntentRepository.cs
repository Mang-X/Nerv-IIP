using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Notification.Domain.AggregatesModel.NotificationIntentAggregate;
using NetCorePal.Extensions.Repository;
using NetCorePal.Extensions.Repository.EntityFrameworkCore;

namespace Nerv.IIP.Notification.Infrastructure.Repositories;

public interface INotificationIntentRepository : IRepository<NotificationIntent, NotificationIntentId>
{
    Task<NotificationIntent?> GetByDedupeKeyAsync(string organizationId, string environmentId, string dedupeKey, CancellationToken cancellationToken = default);
}

public sealed class NotificationIntentRepository(ApplicationDbContext context)
    : RepositoryBase<NotificationIntent, NotificationIntentId, ApplicationDbContext>(context), INotificationIntentRepository
{
    private readonly ApplicationDbContext context = context;

    public async Task<NotificationIntent?> GetByDedupeKeyAsync(string organizationId, string environmentId, string dedupeKey, CancellationToken cancellationToken = default)
    {
        return await context.NotificationIntents
            .Include(x => x.Messages)
            .Include(x => x.Tasks)
            .FirstOrDefaultAsync(x =>
                x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.DedupeKey == dedupeKey,
                cancellationToken);
    }
}
