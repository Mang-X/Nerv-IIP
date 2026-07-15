namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public sealed record BusinessConsoleConnectorCollectionHealthRequest(string ConnectorId, string OrganizationId, string EnvironmentId);

public sealed record BusinessConsoleConnectorCollectionHealthResponse(
    string ConnectorId,
    string Status,
    DateTimeOffset? LastHeartbeatAtUtc,
    DateTimeOffset? MetricsReportedAtUtc,
    DateTimeOffset? LastSampleAtUtc,
    long? ReceivedCount,
    long? DroppedCount,
    long? ErrorCount,
    string? SourceSystem);

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
    DateTimeOffset? LastHeartbeatAtUtc,
    DateTimeOffset? MetricsReportedAtUtc,
    DateTimeOffset? LastSampleAtUtc,
    long? ReceivedCount,
    long? DroppedCount,
    long? ErrorCount,
    string? SourceSystem);
