using NetCorePal.Extensions.Domain;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Notification.Domain.AggregatesModel.NotificationIntentAggregate;

namespace Nerv.IIP.Notification.Infrastructure;

public partial record DeliveryAttemptId : IGuidStronglyTypedId;

public sealed class DeliveryAttempt : Entity<DeliveryAttemptId>
{
    private DeliveryAttempt()
    {
    }

    public DeliveryAttempt(
        NotificationMessageId notificationMessageId,
        string channel,
        string status,
        DateTimeOffset attemptedAtUtc,
        string? failureReason)
    {
        NotificationMessageId = notificationMessageId;
        Channel = channel;
        Status = status;
        AttemptedAtUtc = attemptedAtUtc;
        FailureReason = failureReason;
    }

    public NotificationMessageId NotificationMessageId { get; private set; } = null!;
    public string Channel { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public DateTimeOffset AttemptedAtUtc { get; private set; }
    public string? FailureReason { get; private set; }
}
