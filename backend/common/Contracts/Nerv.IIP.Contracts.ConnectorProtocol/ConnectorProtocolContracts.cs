namespace Nerv.IIP.Contracts.ConnectorProtocol;

public sealed record ConnectorRequestContext(
    string ProtocolVersion,
    string SdkVersion,
    string CorrelationId,
    DateTimeOffset OccurredAtUtc,
    string OrganizationId,
    string EnvironmentId,
    string ConnectorHostId);

public sealed record ApplicationRegistration(
    ConnectorRequestContext Context,
    string IdempotencyKey,
    string NodeKey,
    string NodeName,
    string DeploymentKind,
    string ApplicationKey,
    string ApplicationName,
    string Version,
    string InstanceKey,
    string InstanceName,
    IReadOnlyList<CapabilityDescriptor> Capabilities,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record CapabilityDescriptor(
    string CapabilityCode,
    string CapabilityVersion,
    string Category,
    IReadOnlyList<string> SupportedOperations,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record ApplicationHeartbeat(
    ConnectorRequestContext Context,
    string InstanceKey,
    DateTimeOffset HeartbeatAtUtc,
    bool Reachable,
    DateTimeOffset ConnectorHostStartedAtUtc,
    int LatencyMs,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record InstanceStateSnapshot(
    ConnectorRequestContext Context,
    string InstanceKey,
    DateTimeOffset ObservedAtUtc,
    string ReportedStatus,
    string HealthStatus,
    string Summary,
    IReadOnlyDictionary<string, string> Detail,
    IReadOnlyDictionary<string, decimal> Metrics,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record OperationResult(
    ConnectorRequestContext Context,
    string OperationTaskId,
    string AttemptId,
    string InstanceKey,
    string OperationCode,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset FinishedAtUtc,
    string ExecutionStatus,
    FailureReason? Failure,
    IReadOnlyDictionary<string, string> Output);

public sealed record FailureReason(
    string Code,
    string Message,
    string Category,
    bool Retryable,
    IReadOnlyDictionary<string, string> Detail);
