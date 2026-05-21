using Nerv.IIP.Contracts.Notification;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Notification.Domain.AggregatesModel.NotificationIntentAggregate;
using Nerv.IIP.Notification.Infrastructure;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Notification.Web.Application.Commands.Notifications;

public sealed record MarkNotificationMessagesReadRequest(IReadOnlyCollection<string> MessageIds);

public sealed record MarkNotificationMessagesReadCommand(
    string OrganizationId,
    string EnvironmentId,
    IReadOnlyCollection<string> MessageIds,
    DateTimeOffset Now)
    : ICommand<IReadOnlyCollection<MarkNotificationMessageReadResponse>>;

public sealed class MarkNotificationMessagesReadCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<MarkNotificationMessagesReadCommand, IReadOnlyCollection<MarkNotificationMessageReadResponse>>
{
    public async Task<IReadOnlyCollection<MarkNotificationMessageReadResponse>> Handle(
        MarkNotificationMessagesReadCommand command,
        CancellationToken cancellationToken)
    {
        var messageIds = command.MessageIds
            .Select(MarkNotificationMessageReadCommandHandler.ParseMessageId)
            .Distinct()
            .ToList();

        var intents = await dbContext.NotificationIntents
            .Include(x => x.Messages)
            .Where(x =>
                x.OrganizationId == command.OrganizationId
                && x.EnvironmentId == command.EnvironmentId
                && x.Messages.Any(message => messageIds.Contains(message.Id)))
            .ToListAsync(cancellationToken);
        var messages = intents
            .SelectMany(x => x.Messages.Select(message => new { Intent = x, Message = message }))
            .Where(x => messageIds.Contains(x.Message.Id))
            .ToList();
        var foundIds = messages.Select(x => x.Message.Id).ToHashSet();
        var missingId = messageIds.FirstOrDefault(id => !foundIds.Contains(id));
        if (missingId is not null)
        {
            throw new KnownException($"Notification message was not found: {missingId.Id}");
        }

        foreach (var item in messages)
        {
            item.Intent.MarkRead(item.Message.Id, command.Now);
        }

        return messages
            .Select(x => new MarkNotificationMessageReadResponse(
                x.Message.Id.Id.ToString(),
                x.Message.Status,
                x.Message.ReadAtUtc ?? command.Now))
            .ToList();
    }
}
