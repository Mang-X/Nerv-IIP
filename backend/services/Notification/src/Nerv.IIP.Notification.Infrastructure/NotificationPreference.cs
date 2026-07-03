using NetCorePal.Extensions.Domain;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Notification.Infrastructure;

public partial record NotificationPreferenceId : IGuidStronglyTypedId;

public sealed class NotificationPreference : Entity<NotificationPreferenceId>
{
    private NotificationPreference()
    {
    }

    private NotificationPreference(
        string organizationId,
        string environmentId,
        string recipientRef,
        string notificationType,
        string channel,
        bool enabled,
        DateTimeOffset now)
    {
        OrganizationId = Required(organizationId, "Organization is required.");
        EnvironmentId = Required(environmentId, "Environment is required.");
        RecipientRef = Required(recipientRef, "Recipient ref is required.");
        NotificationType = Required(notificationType, "Notification type is required.");
        Channel = Required(channel, "Delivery channel is required.");
        Enabled = enabled;
        CreatedAtUtc = now;
        UpdatedAtUtc = now;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string RecipientRef { get; private set; } = string.Empty;
    public string NotificationType { get; private set; } = string.Empty;
    public string Channel { get; private set; } = string.Empty;
    public bool Enabled { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public Deleted Deleted { get; private set; } = new();
    public RowVersion RowVersion { get; private set; } = new(0);

    public static NotificationPreference Create(
        string organizationId,
        string environmentId,
        string recipientRef,
        string notificationType,
        string channel,
        bool enabled,
        DateTimeOffset now)
    {
        return new NotificationPreference(organizationId, environmentId, recipientRef, notificationType, channel, enabled, now);
    }

    public void Update(bool enabled, DateTimeOffset now)
    {
        Enabled = enabled;
        UpdatedAtUtc = now;
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
