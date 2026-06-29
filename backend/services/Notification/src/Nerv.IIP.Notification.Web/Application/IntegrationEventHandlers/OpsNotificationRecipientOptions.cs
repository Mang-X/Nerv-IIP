namespace Nerv.IIP.Notification.Web.Application.IntegrationEventHandlers;

public sealed class OpsNotificationRecipientOptions
{
    private static readonly string[] FallbackRecipientRefs = ["role:ops-admin"];

    public const string SectionName = "Notification:OpsRecipients";

    public string[] DefaultRecipientRefs { get; set; } = FallbackRecipientRefs;

    public IReadOnlyCollection<string> ResolveDefaultRecipientRefs()
    {
        var recipientRefs = DefaultRecipientRefs
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return recipientRefs.Length == 0
            ? FallbackRecipientRefs
            : recipientRefs;
    }
}
