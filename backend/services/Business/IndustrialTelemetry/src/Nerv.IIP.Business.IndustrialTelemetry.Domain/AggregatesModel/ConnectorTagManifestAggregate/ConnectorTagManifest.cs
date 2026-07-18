namespace Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.ConnectorTagManifestAggregate;

public partial record ConnectorTagManifestId : IGuidStronglyTypedId;

public enum ManifestApplyDisposition
{
    Accepted,
    Idempotent,
    Stale,
    Conflict,
}

public sealed record ManifestApplyResult(
    ManifestApplyDisposition Disposition,
    string AcceptedManifestRevision,
    DateTimeOffset AcceptedManifestObservedAtUtc);

public sealed record ConnectorTagManifestEntry(
    string DeviceAssetId,
    string TagKey,
    bool Enabled,
    string? ProtocolAddress,
    string ActivationStatus,
    DateTimeOffset ActivationObservedAtUtc,
    string? ActivationErrorCode = null,
    string? ActivationErrorMessage = null);

public sealed class ConnectorTagManifest : Entity<ConnectorTagManifestId>, IAggregateRoot
{
    private readonly List<ConnectorTagBinding> bindings = [];

    private ConnectorTagManifest()
    {
    }

    private ConnectorTagManifest(
        string organizationId,
        string environmentId,
        string collectionConnectorId,
        string sourceSystem,
        string manifestRevision,
        DateTimeOffset manifestObservedAtUtc,
        IReadOnlyCollection<ConnectorTagManifestEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var normalizedEntries = NormalizeEntries(entries);
        Id = new ConnectorTagManifestId(Guid.CreateVersion7());
        OrganizationId = IndustrialTelemetryText.Required(organizationId, nameof(organizationId));
        EnvironmentId = IndustrialTelemetryText.Required(environmentId, nameof(environmentId));
        CollectionConnectorId = IndustrialTelemetryText.Required(collectionConnectorId, nameof(collectionConnectorId));
        SourceSystem = IndustrialTelemetryText.RequiredLower(sourceSystem, nameof(sourceSystem));
        ManifestRevision = NormalizeRevision(manifestRevision);
        ManifestObservedAtUtc = manifestObservedAtUtc.ToUniversalTime();
        ManifestObservedAtUtcTicks = manifestObservedAtUtc.UtcTicks;
        ConcurrencyVersion = 1;
        ApplyEntries(normalizedEntries, manifestObservedAtUtc);
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string CollectionConnectorId { get; private set; } = string.Empty;
    public string SourceSystem { get; private set; } = string.Empty;
    public string ManifestRevision { get; private set; } = string.Empty;
    public DateTimeOffset ManifestObservedAtUtc { get; private set; }
    public long ManifestObservedAtUtcTicks { get; private set; }
    public long ConcurrencyVersion { get; private set; }
    public IReadOnlyCollection<ConnectorTagBinding> Bindings => bindings.AsReadOnly();

    public static ConnectorTagManifest Create(
        string organizationId,
        string environmentId,
        string collectionConnectorId,
        string sourceSystem,
        string manifestRevision,
        DateTimeOffset manifestObservedAtUtc,
        IReadOnlyCollection<ConnectorTagManifestEntry> entries)
    {
        return new ConnectorTagManifest(
            organizationId,
            environmentId,
            collectionConnectorId,
            sourceSystem,
            manifestRevision,
            manifestObservedAtUtc,
            entries);
    }

    public ManifestApplyResult Apply(
        string sourceSystem,
        string manifestRevision,
        DateTimeOffset manifestObservedAtUtc,
        IReadOnlyCollection<ConnectorTagManifestEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var normalizedRevision = NormalizeRevision(manifestRevision);
        var manifestObservedAtUtcTicks = manifestObservedAtUtc.UtcTicks;
        if (manifestObservedAtUtcTicks < ManifestObservedAtUtcTicks)
        {
            return CurrentResult(ManifestApplyDisposition.Stale);
        }

        if (manifestObservedAtUtcTicks == ManifestObservedAtUtcTicks)
        {
            if (normalizedRevision != ManifestRevision)
            {
                return CurrentResult(ManifestApplyDisposition.Conflict);
            }

            var replayEntries = NormalizeEntries(entries);
            var replaySourceSystem = IndustrialTelemetryText.RequiredLower(sourceSystem, nameof(sourceSystem));
            if (!HasSameRevisionShape(replaySourceSystem, replayEntries))
            {
                return CurrentResult(ManifestApplyDisposition.Conflict);
            }

            ApplyEntries(replayEntries, manifestObservedAtUtc);
            return CurrentResult(ManifestApplyDisposition.Idempotent);
        }

        var normalizedEntries = NormalizeEntries(entries);
        var normalizedSourceSystem = IndustrialTelemetryText.RequiredLower(sourceSystem, nameof(sourceSystem));
        if (normalizedRevision == ManifestRevision && !HasSameRevisionShape(normalizedSourceSystem, normalizedEntries))
        {
            return CurrentResult(ManifestApplyDisposition.Conflict);
        }

        SourceSystem = normalizedSourceSystem;
        ManifestRevision = normalizedRevision;
        ManifestObservedAtUtc = manifestObservedAtUtc.ToUniversalTime();
        ManifestObservedAtUtcTicks = manifestObservedAtUtcTicks;
        ApplyEntries(normalizedEntries, manifestObservedAtUtc);
        ConcurrencyVersion = checked(ConcurrencyVersion + 1);
        return CurrentResult(ManifestApplyDisposition.Accepted);
    }

    public bool HasSameBusinessKey(string organizationId, string environmentId, string collectionConnectorId)
    {
        return OrganizationId == IndustrialTelemetryText.Required(organizationId, nameof(organizationId))
            && EnvironmentId == IndustrialTelemetryText.Required(environmentId, nameof(environmentId))
            && CollectionConnectorId == IndustrialTelemetryText.Required(collectionConnectorId, nameof(collectionConnectorId));
    }

    private void ApplyEntries(IReadOnlyCollection<ConnectorTagManifestEntry> entries, DateTimeOffset retiredAtUtc)
    {
        var currentKeys = entries
            .Select(entry => (entry.DeviceAssetId, entry.TagKey))
            .ToHashSet();

        foreach (var binding in bindings.Where(binding => binding.IsCurrent && !currentKeys.Contains((binding.DeviceAssetId, binding.TagKey))))
        {
            binding.Retire(retiredAtUtc);
        }

        foreach (var entry in entries)
        {
            var binding = bindings.SingleOrDefault(existing =>
                existing.DeviceAssetId == entry.DeviceAssetId && existing.TagKey == entry.TagKey);
            if (binding is null)
            {
                bindings.Add(ConnectorTagBinding.Create(
                    Id,
                    OrganizationId,
                    EnvironmentId,
                    CollectionConnectorId,
                    entry));
            }
            else
            {
                binding.Apply(entry);
            }
        }
    }

    private static IReadOnlyCollection<ConnectorTagManifestEntry> NormalizeEntries(IReadOnlyCollection<ConnectorTagManifestEntry> entries)
    {
        var keys = new HashSet<(string DeviceAssetId, string TagKey)>();
        var normalized = new List<ConnectorTagManifestEntry>(entries.Count);
        foreach (var entry in entries)
        {
            ArgumentNullException.ThrowIfNull(entry);
            var normalizedEntry = entry with
            {
                DeviceAssetId = IndustrialTelemetryText.Required(entry.DeviceAssetId, nameof(entry.DeviceAssetId)),
                TagKey = IndustrialTelemetryText.RequiredLower(entry.TagKey, nameof(entry.TagKey)),
                ProtocolAddress = IndustrialTelemetryText.OptionalSanitized(entry.ProtocolAddress, 500),
                ActivationStatus = ConnectorTagBinding.NormalizeActivationStatus(entry.ActivationStatus),
            };
            if (!keys.Add((normalizedEntry.DeviceAssetId, normalizedEntry.TagKey)))
            {
                throw new ArgumentException(
                    $"Connector manifest contains duplicate device/tag binding '{normalizedEntry.DeviceAssetId}/{normalizedEntry.TagKey}'.",
                    nameof(entries));
            }

            normalized.Add(normalizedEntry);
        }

        return normalized;
    }

    private bool HasSameRevisionShape(
        string sourceSystem,
        IReadOnlyCollection<ConnectorTagManifestEntry> entries)
    {
        if (SourceSystem != sourceSystem)
        {
            return false;
        }

        var currentBindings = bindings
            .Where(binding => binding.IsCurrent)
            .ToDictionary(binding => (binding.DeviceAssetId, binding.TagKey));
        return currentBindings.Count == entries.Count
            && entries.All(entry =>
                currentBindings.TryGetValue((entry.DeviceAssetId, entry.TagKey), out var binding)
                && binding.Enabled == entry.Enabled
                && binding.ProtocolAddress == entry.ProtocolAddress);
    }

    private ManifestApplyResult CurrentResult(ManifestApplyDisposition disposition)
    {
        return new ManifestApplyResult(
            disposition,
            ManifestRevision,
            new DateTimeOffset(ManifestObservedAtUtcTicks, TimeSpan.Zero));
    }

    private static string NormalizeRevision(string manifestRevision)
    {
        var normalized = IndustrialTelemetryText.Required(manifestRevision, nameof(manifestRevision));
        if (normalized.Length != 64 || normalized.Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
        {
            throw new ArgumentException("Manifest revision must be a 64-character lowercase SHA-256 hexadecimal value.", nameof(manifestRevision));
        }

        return normalized;
    }
}
