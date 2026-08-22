using NetCorePal.Extensions.Domain;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Notification.Infrastructure;

public partial record NotificationSubscriptionId : IGuidStronglyTypedId;

public sealed class NotificationSubscription : Entity<NotificationSubscriptionId>
{
    private NotificationSubscription()
    {
    }

    private NotificationSubscription(
        string organizationId,
        string environmentId,
        string recipientRef,
        string notificationType,
        string channel,
        DateTimeOffset now)
    {
        OrganizationId = Required(organizationId, "组织");
        EnvironmentId = Required(environmentId, "环境");
        RecipientRef = Required(recipientRef, "收件人");
        NotificationType = Required(notificationType, "通知类型");
        Channel = Required(channel, "渠道");
        Enabled = true;
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

    public static NotificationSubscription Create(
        string organizationId,
        string environmentId,
        string recipientRef,
        string notificationType,
        string channel,
        DateTimeOffset now)
    {
        return new NotificationSubscription(organizationId, environmentId, recipientRef, notificationType, channel, now);
    }

    public void Update(bool enabled, DateTimeOffset now)
    {
        Enabled = enabled;
        UpdatedAtUtc = now;
    }

    private static string Required(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new KnownException($"通知订阅{fieldName}不能为空。");
        }

        return value;
    }
}
