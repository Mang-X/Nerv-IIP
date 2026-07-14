using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.Notification.Domain.AggregatesModel.NotificationIntentAggregate;
using Nerv.IIP.Notification.Infrastructure;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Notification.Web.Application.Commands.Notifications;

public sealed record MarkNotificationMessageReadCommand(string OrganizationId, string EnvironmentId, string MessageId, string RecipientRef, DateTimeOffset Now)
    : ICommand<MarkNotificationMessageReadResponse>;

public sealed class MarkNotificationMessageReadCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<MarkNotificationMessageReadCommand, MarkNotificationMessageReadResponse>
{
    public async Task<MarkNotificationMessageReadResponse> Handle(MarkNotificationMessageReadCommand command, CancellationToken cancellationToken)
    {
        var messageId = ParseMessageId(command.MessageId);
        var intent = await dbContext.NotificationIntents
            .Include(x => x.Messages)
            .FirstOrDefaultAsync(x =>
                x.OrganizationId == command.OrganizationId
                && x.EnvironmentId == command.EnvironmentId
                && x.Messages.Any(message => message.Id == messageId && message.RecipientRef == command.RecipientRef),
                cancellationToken)
            ?? throw new KnownException($"Notification message was not found: {command.MessageId}");

        var message = intent.MarkRead(messageId, command.Now);
        return new MarkNotificationMessageReadResponse(
            message.Id.Id.ToString(),
            message.Status,
            message.ReadAtUtc ?? command.Now);
    }

    internal static NotificationMessageId ParseMessageId(string messageId)
    {
        if (!Guid.TryParse(messageId, out var value))
        {
            throw new KnownException($"Notification message id is invalid: {messageId}");
        }

        return new NotificationMessageId(value);
    }
}
