using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.Notification.Domain.AggregatesModel.NotificationIntentAggregate;
using Nerv.IIP.Notification.Infrastructure;
using Nerv.IIP.Notification.Web.Application.Notifications;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Notification.Web.Application.Commands.Notifications;

public sealed record SubmitNotificationIntentCommand(
    string OrganizationId,
    string EnvironmentId,
    SubmitNotificationIntentRequest Request,
    DateTimeOffset Now) : ICommand<NotificationIntentResponse>;

public sealed class SubmitNotificationIntentCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<SubmitNotificationIntentCommand, NotificationIntentResponse>
{
    public async Task<NotificationIntentResponse> Handle(SubmitNotificationIntentCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var existing = await dbContext.NotificationIntents
            .Include(x => x.Messages)
            .Include(x => x.Tasks)
            .FirstOrDefaultAsync(x =>
                x.OrganizationId == command.OrganizationId
                && x.EnvironmentId == command.EnvironmentId
                && x.SourceService == request.SourceService
                && x.SourceEventType == request.SourceEventType
                && x.DedupeKey == request.DedupeKey,
                cancellationToken);
        if (existing is not null)
        {
            return existing.ToResponse(duplicate: true);
        }

        var intent = new NotificationIntent(
            command.OrganizationId,
            command.EnvironmentId,
            request.SourceService,
            request.SourceEventType,
            request.SourceEventId,
            request.IntentType,
            request.Severity,
            request.DedupeKey,
            request.Resource?.ResourceType,
            request.Resource?.ResourceId,
            request.Resource?.FileId,
            request.Title,
            request.Summary,
            request.SuggestedRecipientRefs,
            command.Now);

        dbContext.NotificationIntents.Add(intent);
        return intent.ToResponse(duplicate: false);
    }
}
