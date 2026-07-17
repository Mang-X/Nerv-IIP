namespace Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.ConnectorTagManifestAggregate;

public partial record ConnectorTagBindingId : IGuidStronglyTypedId;

public sealed class ConnectorTagBinding : Entity<ConnectorTagBindingId>
{
    private static readonly string[] ActivationStatuses = ["pending", "active", "error", "disabled"];

    private ConnectorTagBinding()
    {
    }

    private ConnectorTagBinding(
        ConnectorTagManifestId connectorTagManifestId,
        string organizationId,
        string environmentId,
        string collectionConnectorId,
        ConnectorTagManifestEntry entry)
    {
        Id = new ConnectorTagBindingId(Guid.CreateVersion7());
        ConnectorTagManifestId = connectorTagManifestId;
        OrganizationId = organizationId;
        EnvironmentId = environmentId;
        CollectionConnectorId = collectionConnectorId;
        DeviceAssetId = entry.DeviceAssetId;
        TagKey = entry.TagKey;
        Apply(entry);
    }

    public ConnectorTagManifestId ConnectorTagManifestId { get; private set; } = default!;
    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string CollectionConnectorId { get; private set; } = string.Empty;
    public string DeviceAssetId { get; private set; } = string.Empty;
    public string TagKey { get; private set; } = string.Empty;
    public bool Enabled { get; private set; }
    public string? ProtocolAddress { get; private set; }
    public bool IsCurrent { get; private set; }
    public DateTimeOffset? RetiredAtUtc { get; private set; }
    public string ActivationStatus { get; private set; } = "pending";
    public DateTimeOffset ActivationObservedAtUtc { get; private set; }
    public string? ActivationErrorCode { get; private set; }
    public string? ActivationErrorMessage { get; private set; }

    internal static ConnectorTagBinding Create(
        ConnectorTagManifestId connectorTagManifestId,
        string organizationId,
        string environmentId,
        string collectionConnectorId,
        ConnectorTagManifestEntry entry)
    {
        return new ConnectorTagBinding(
            connectorTagManifestId,
            organizationId,
            environmentId,
            collectionConnectorId,
            entry);
    }

    internal void Apply(ConnectorTagManifestEntry entry)
    {
        Enabled = entry.Enabled;
        ProtocolAddress = IndustrialTelemetryText.OptionalSanitized(entry.ProtocolAddress, 500);
        IsCurrent = true;
        RetiredAtUtc = null;
        ApplyActivation(entry);
    }

    internal void Retire(DateTimeOffset retiredAtUtc)
    {
        IsCurrent = false;
        RetiredAtUtc = retiredAtUtc;
    }

    public bool HasSameBusinessKey(
        string organizationId,
        string environmentId,
        string collectionConnectorId,
        string deviceAssetId,
        string tagKey)
    {
        return OrganizationId == IndustrialTelemetryText.Required(organizationId, nameof(organizationId))
            && EnvironmentId == IndustrialTelemetryText.Required(environmentId, nameof(environmentId))
            && CollectionConnectorId == IndustrialTelemetryText.Required(collectionConnectorId, nameof(collectionConnectorId))
            && DeviceAssetId == IndustrialTelemetryText.Required(deviceAssetId, nameof(deviceAssetId))
            && TagKey == IndustrialTelemetryText.RequiredLower(tagKey, nameof(tagKey));
    }

    private void ApplyActivation(ConnectorTagManifestEntry entry)
    {
        var status = NormalizeActivationStatus(entry.ActivationStatus);
        if (ActivationObservedAtUtc != default && entry.ActivationObservedAtUtc <= ActivationObservedAtUtc)
        {
            return;
        }

        ActivationStatus = status;
        ActivationObservedAtUtc = entry.ActivationObservedAtUtc;
        ActivationErrorCode = status == "error"
            ? IndustrialTelemetryText.OptionalSanitized(entry.ActivationErrorCode, 128)
            : null;
        ActivationErrorMessage = status == "error"
            ? IndustrialTelemetryText.OptionalSanitized(entry.ActivationErrorMessage, 500)
            : null;
    }

    internal static string NormalizeActivationStatus(string activationStatus)
    {
        var status = IndustrialTelemetryText.RequiredLower(activationStatus, nameof(activationStatus));
        if (!ActivationStatuses.Contains(status, StringComparer.Ordinal))
        {
            throw new ArgumentException(
                $"Activation status must be one of: {string.Join(", ", ActivationStatuses)}.",
                nameof(activationStatus));
        }

        return status;
    }
}
