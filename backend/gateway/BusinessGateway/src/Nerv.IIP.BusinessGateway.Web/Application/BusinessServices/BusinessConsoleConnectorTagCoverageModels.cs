namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public sealed record BusinessConsoleConnectorTagCoverageRequest(
    string ConnectorId,
    string OrganizationId,
    string EnvironmentId);

public sealed record BusinessConsoleConnectorTagCoverageResponse(
    string CollectionConnectorId,
    string ManifestStatus,
    string? ManifestRevision,
    DateTimeOffset? ManifestObservedAtUtc,
    int ConfiguredCount,
    int EnabledCount,
    int ActiveCount,
    int EverSampledCount,
    int ErrorCount,
    IReadOnlyCollection<BusinessConsoleConnectorTagCoverageItem> Items);

public sealed record BusinessConsoleConnectorTagCoverageItem(
    string DeviceAssetId,
    string TagKey,
    bool Enabled,
    string ActivationStatus,
    DateTimeOffset ActivationObservedAtUtc,
    string? ActivationErrorCode,
    string? ActivationErrorMessage,
    DateTimeOffset? FirstSampleAtUtc,
    DateTimeOffset? LastSampleAtUtc);
