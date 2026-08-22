using NetCorePal.Extensions.Domain;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Notification.Infrastructure;

public partial record NotificationRecipientChannelBindingId : IGuidStronglyTypedId;

public sealed class NotificationRecipientChannelBinding : Entity<NotificationRecipientChannelBindingId>
{
    private NotificationRecipientChannelBinding()
    {
    }

    private NotificationRecipientChannelBinding(
        string organizationId,
        string environmentId,
        string recipientRef,
        string channel,
        string recipientAddress,
        DateTimeOffset now)
    {
        OrganizationId = Required(organizationId, "组织");
        EnvironmentId = Required(environmentId, "环境");
        RecipientRef = Required(recipientRef, "收件人");
        Channel = Required(channel, "渠道");
        RecipientAddress = Required(recipientAddress, "收件地址");
        Enabled = true;
        CreatedAtUtc = now;
        UpdatedAtUtc = now;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string RecipientRef { get; private set; } = string.Empty;
    public string Channel { get; private set; } = string.Empty;
    public string RecipientAddress { get; private set; } = string.Empty;
    public bool Enabled { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public Deleted Deleted { get; private set; } = new();
    public RowVersion RowVersion { get; private set; } = new(0);

    public static NotificationRecipientChannelBinding Create(
        string organizationId,
        string environmentId,
        string recipientRef,
        string channel,
        string recipientAddress,
        DateTimeOffset now)
    {
        return new NotificationRecipientChannelBinding(organizationId, environmentId, recipientRef, channel, recipientAddress, now);
    }

    public void Update(string recipientAddress, bool enabled, DateTimeOffset now)
    {
        RecipientAddress = Required(recipientAddress, "收件地址");
        Enabled = enabled;
        UpdatedAtUtc = now;
    }

    private static string Required(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new KnownException($"通知渠道绑定{fieldName}不能为空。");
        }

        return value;
    }
}
