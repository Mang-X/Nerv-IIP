namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public sealed record BusinessConsoleConnectorCollectionHealthRequest(string ConnectorId, string OrganizationId, string EnvironmentId);

public sealed record BusinessConsoleConnectorConnectionState(
    string Status,
    DateTimeOffset ObservedAtUtc,
    DateTimeOffset? ConnectedSinceUtc = null,
    DateTimeOffset? DisconnectedSinceUtc = null,
    string? ReasonCategory = null,
    string? DiagnosticCode = null);

public sealed record BusinessConsoleConnectorCollectionHealthResponse(
    string ConnectorId,
    string Status,
    DateTimeOffset? LastHeartbeatAtUtc,
    DateTimeOffset? MetricsReportedAtUtc,
    DateTimeOffset? LastSampleAtUtc,
    long? ReceivedCount,
    long? DroppedCount,
    long? ErrorCount,
    string? SourceSystem,
    BusinessConsoleConnectorConnectionState? Connection = null,
    string? StaleReason = null,
    string? OfflineReason = null);

public sealed record BusinessConsoleConnectorCollectionHealthListRequest(
    string OrganizationId,
    string EnvironmentId);

public sealed record BusinessConsoleConnectorCollectionHealthListResponse(
    IReadOnlyList<BusinessConsoleConnectorCollectionHealthListItem> Items,
    int Total = 0);

public sealed record BusinessConsoleConnectorCollectionHealthListItem(
    string ConnectorId,
    string ConnectorName,
    string Status,
    string? StaleReason,
    DateTimeOffset? LastHeartbeatAtUtc,
    DateTimeOffset? MetricsReportedAtUtc,
    DateTimeOffset? LastSampleAtUtc,
    long? ReceivedCount,
    long? DroppedCount,
    long? ErrorCount,
    Guid? CounterEpoch,
    string? SourceSystem,
    BusinessConsoleConnectorConnectionState? Connection = null,
    string? OfflineReason = null);
