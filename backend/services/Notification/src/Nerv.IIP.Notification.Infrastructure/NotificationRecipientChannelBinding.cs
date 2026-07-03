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
        OrganizationId = Required(organizationId, "Organization is required.");
        EnvironmentId = Required(environmentId, "Environment is required.");
        RecipientRef = Required(recipientRef, "Recipient ref is required.");
        Channel = Required(channel, "Delivery channel is required.");
        RecipientAddress = Required(recipientAddress, "Recipient channel address is required.");
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

    private static string Required(string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new KnownException(message);
        }

        return value;
    }
}
