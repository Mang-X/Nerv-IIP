using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.Notification.Domain.AggregatesModel.NotificationIntentAggregate;

namespace Nerv.IIP.Notification.Web.Application.Notifications;

internal static class NotificationMappings
{
    public static NotificationIntentResponse ToResponse(this NotificationIntent intent, bool duplicate)
    {
        return new NotificationIntentResponse(
            intent.Id.Id.ToString(),
            duplicate,
            intent.Messages.Select(message => message.ToResponse(intent.Id)).ToList());
    }

    public static NotificationMessageResponse ToResponse(this NotificationMessage message, NotificationIntentId intentId)
    {
        return new NotificationMessageResponse(
            message.Id.Id.ToString(),
            intentId.Id.ToString(),
            message.RecipientRef,
            message.Status,
            message.Severity,
            message.Title,
            message.Summary,
            ToResource(message.ResourceType, message.ResourceId, message.FileId),
            message.CreatedAtUtc,
            message.ReadAtUtc);
    }

    public static NotificationTaskResponse ToResponse(this NotificationTask task)
    {
        return new NotificationTaskResponse(
            task.Id.Id.ToString(),
            task.MessageId.Id.ToString(),
            task.RecipientRef,
            task.TaskType,
            task.Status,
            task.ActionRef,
            task.CreatedAtUtc);
    }

    private static NotificationResourceRef? ToResource(string? resourceType, string? resourceId, string? fileId)
    {
        return string.IsNullOrWhiteSpace(resourceType) || string.IsNullOrWhiteSpace(resourceId)
            ? null
            : new NotificationResourceRef(resourceType, resourceId, fileId);
    }
}
