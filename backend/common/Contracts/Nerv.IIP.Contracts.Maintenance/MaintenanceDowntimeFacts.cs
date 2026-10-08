namespace Nerv.IIP.Contracts.Maintenance;

/// <summary>Queries actual maintenance downtime overlapping a half-open window; returned timestamps are never clipped.</summary>
public sealed record MaintenanceDowntimeFactsRequest(
    string OrganizationId,
    string EnvironmentId,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    IReadOnlyCollection<string> DeviceAssetIds);

public sealed record MaintenanceDowntimeFactsResponse(IReadOnlyCollection<MaintenanceDowntimeFact> Items);

/// <summary>Completion or cancellation releases the asset; alarm clearance and predictions do not.</summary>
public sealed record MaintenanceDowntimeFact(
    string DeviceAssetId,
    string WorkOrderId,
    string Source,
    string? SourceType,
    string? SourceReferenceId,
    DateTimeOffset UnavailableFromUtc,
    DateTimeOffset? ReleasedAtUtc,
    DateTimeOffset? ExpectedRestoreAtUtc,
    DateTimeOffset? PredictedRestoreAtUtc,
    string? RestorePredictionSource,
    string? RestorePredictionSourceVersion);
