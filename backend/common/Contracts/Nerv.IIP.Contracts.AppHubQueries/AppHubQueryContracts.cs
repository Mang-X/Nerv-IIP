namespace Nerv.IIP.Contracts.AppHubQueries;

public sealed record InstanceListQuery(
    string OrganizationId,
    string EnvironmentId,
    int PageNumber,
    int PageSize,
    string? Search);

public sealed record InstanceListResponse(
    int PageNumber,
    int PageSize,
    int TotalCount,
    IReadOnlyList<InstanceListItem> Items);

public sealed record InstanceListItem(
    string ApplicationKey,
    string ApplicationName,
    string Version,
    string NodeKey,
    string NodeName,
    string InstanceKey,
    string InstanceName,
    string ReportedStatus,
    string HealthStatus,
    DateTimeOffset? LastHeartbeatAtUtc,
    DateTimeOffset? LastStateObservedAtUtc);

public sealed record InstanceDetailResponse(
    string ApplicationKey,
    string ApplicationName,
    string Version,
    string NodeKey,
    string NodeName,
    string InstanceKey,
    string InstanceName,
    string ReportedStatus,
    string HealthStatus,
    DateTimeOffset? LastHeartbeatAtUtc,
    DateTimeOffset? LastStateObservedAtUtc,
    IReadOnlyList<CapabilitySummary> Capabilities,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record CapabilitySummary(
    string CapabilityCode,
    string CapabilityVersion,
    string Category,
    IReadOnlyList<string> SupportedOperations);
