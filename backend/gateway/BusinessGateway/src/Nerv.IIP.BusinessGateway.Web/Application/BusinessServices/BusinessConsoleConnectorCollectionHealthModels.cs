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
