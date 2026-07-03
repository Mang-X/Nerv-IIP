using NetCorePal.Extensions.Domain;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Notification.Domain.AggregatesModel.NotificationIntentAggregate;

namespace Nerv.IIP.Notification.Infrastructure;

public partial record DeliveryAttemptId : IGuidStronglyTypedId;

public static class NotificationDeliveryChannels
{
    public const string InApp = "in-app";
    public const string WeCom = "wecom";
    public const string DingTalk = "dingtalk";
    public const string Email = "email";
    public const string Webhook = "webhook";
}

public static class NotificationDeliveryAttemptStatuses
{
    public const string Started = "started";
    public const string Succeeded = "succeeded";
    public const string PendingRetry = "pending-retry";
    public const string DeadLettered = "dead-lettered";
}

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
        AttemptNo = 1;
    }

    private DeliveryAttempt(
        NotificationMessageId notificationMessageId,
        string channel,
        DateTimeOffset attemptedAtUtc,
        string? recipientAddress = null,
        string? providerName = null)
    {
        NotificationMessageId = notificationMessageId;
        Channel = Required(channel, "Delivery channel is required.");
        Status = NotificationDeliveryAttemptStatuses.Started;
        AttemptedAtUtc = attemptedAtUtc;
        AttemptNo = 1;
        RecipientAddress = string.IsNullOrWhiteSpace(recipientAddress) ? null : recipientAddress;
        ProviderName = string.IsNullOrWhiteSpace(providerName) ? null : providerName;
    }

    public NotificationMessageId NotificationMessageId { get; private set; } = null!;
    public string Channel { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public DateTimeOffset AttemptedAtUtc { get; private set; }
    public int AttemptNo { get; private set; }
    public DateTimeOffset? NextRetryAtUtc { get; private set; }
    public string? FailureReason { get; private set; }
    public string? RecipientAddress { get; private set; }
    public string? ProviderName { get; private set; }
    public string? ProviderMessageId { get; private set; }

    public static DeliveryAttempt Start(NotificationMessageId notificationMessageId, string channel, DateTimeOffset now)
    {
        return new DeliveryAttempt(notificationMessageId, channel, now);
    }

    public static DeliveryAttempt StartExternal(
        NotificationMessageId notificationMessageId,
        string channel,
        string recipientAddress,
        string providerName,
        DateTimeOffset now)
    {
        return new DeliveryAttempt(notificationMessageId, channel, now, recipientAddress, providerName);
    }

    public static DeliveryAttempt Succeeded(NotificationMessageId notificationMessageId, string channel, DateTimeOffset now)
    {
        var attempt = Start(notificationMessageId, channel, now);
        attempt.MarkSucceeded(now);
        return attempt;
    }

    public void MarkSucceeded(DateTimeOffset now)
    {
        MarkSucceeded(now, providerMessageId: null);
    }

    public void MarkSucceeded(DateTimeOffset now, string? providerMessageId)
    {
        EnsureStarted();
        Status = NotificationDeliveryAttemptStatuses.Succeeded;
        AttemptedAtUtc = now;
        NextRetryAtUtc = null;
        FailureReason = null;
        ProviderMessageId = string.IsNullOrWhiteSpace(providerMessageId) ? null : providerMessageId;
    }

    public void MarkFailed(string failureReason, DateTimeOffset now, int maxAttempts, TimeSpan retryDelay)
    {
        if (maxAttempts <= 0)
        {
            throw new KnownException("Delivery max attempts must be positive.");
        }

        EnsureStarted();
        FailureReason = Required(failureReason, "Delivery failure reason is required.");
        AttemptedAtUtc = now;
        if (AttemptNo >= maxAttempts)
        {
            Status = NotificationDeliveryAttemptStatuses.DeadLettered;
            NextRetryAtUtc = null;
            return;
        }

        Status = NotificationDeliveryAttemptStatuses.PendingRetry;
        NextRetryAtUtc = now.Add(retryDelay);
    }

    public void StartRetry(DateTimeOffset now)
    {
        if (!string.Equals(Status, NotificationDeliveryAttemptStatuses.PendingRetry, StringComparison.Ordinal))
        {
            throw new KnownException("Delivery attempt is not pending retry.");
        }

        if (NextRetryAtUtc.HasValue && NextRetryAtUtc > now)
        {
            throw new KnownException("Delivery retry is not due yet.");
        }

        Status = NotificationDeliveryAttemptStatuses.Started;
        AttemptNo++;
        AttemptedAtUtc = now;
        NextRetryAtUtc = null;
    }

    private void EnsureStarted()
    {
        if (!string.Equals(Status, NotificationDeliveryAttemptStatuses.Started, StringComparison.Ordinal))
        {
            throw new KnownException("Delivery attempt is not started.");
        }
    }

    private static string Required(string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new KnownException(message);
        }

        return value;
    }
}
