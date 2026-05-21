using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.Notification.Infrastructure;
using Nerv.IIP.Notification.Web.Application.Notifications;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Notification.Web.Application.Queries.Notifications;

public sealed record ListNotificationTasksQuery(string OrganizationId, string EnvironmentId, string? RecipientRef, string? Status)
    : IQuery<NotificationTaskListResponse>;

public sealed class ListNotificationTasksQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListNotificationTasksQuery, NotificationTaskListResponse>
{
    public async Task<NotificationTaskListResponse> Handle(ListNotificationTasksQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.NotificationTasks
            .AsNoTracking()
            .Where(task => dbContext.NotificationIntents.Any(intent =>
                intent.Id == task.NotificationIntentId
                && intent.OrganizationId == request.OrganizationId
                && intent.EnvironmentId == request.EnvironmentId));
        if (!string.IsNullOrWhiteSpace(request.RecipientRef))
        {
            query = query.Where(x => x.RecipientRef == request.RecipientRef);
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            query = query.Where(x => x.Status == request.Status);
        }

        var tasks = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return new NotificationTaskListResponse(tasks.Select(x => x.ToResponse()).ToList());
    }
}
