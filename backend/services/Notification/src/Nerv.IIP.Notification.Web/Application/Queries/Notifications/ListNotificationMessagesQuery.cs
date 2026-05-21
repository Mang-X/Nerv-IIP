using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.Notification.Infrastructure;
using Nerv.IIP.Notification.Web.Application.Notifications;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Notification.Web.Application.Queries.Notifications;

public sealed record ListNotificationMessagesQuery(string OrganizationId, string EnvironmentId, string? RecipientRef, string? Status)
    : IQuery<NotificationMessageListResponse>;

public sealed class ListNotificationMessagesQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListNotificationMessagesQuery, NotificationMessageListResponse>
{
    public async Task<NotificationMessageListResponse> Handle(ListNotificationMessagesQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.NotificationMessages
            .AsNoTracking()
            .Where(message => dbContext.NotificationIntents.Any(intent =>
                intent.Id == message.NotificationIntentId
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

        var messages = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return new NotificationMessageListResponse(messages.Select(x => x.ToResponse(x.NotificationIntentId)).ToList());
    }
}
