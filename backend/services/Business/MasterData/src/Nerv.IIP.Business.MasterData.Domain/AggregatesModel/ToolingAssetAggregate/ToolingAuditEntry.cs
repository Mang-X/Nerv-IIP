namespace Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ToolingAssetAggregate;

public partial record ToolingAuditEntryId : IGuidStronglyTypedId;

public sealed class ToolingAuditEntry : Entity<ToolingAuditEntryId>
{
    public const string RegisterOperation = "tooling-register";
    public const string StatusOperation = "tooling-status";
    public const string UsageOperation = "tooling-usage";

    private ToolingAuditEntry() { }

    private ToolingAuditEntry(
        string organizationId,
        string environmentId,
        string operationKind,
        string toolingAssetId,
        string toolingCode,
        string actorId,
        string correlationId,
        string causationId,
        string operationId,
        string requestFingerprint,
        ToolingAssetStatus? beforeStatus,
        ToolingAssetStatus? afterStatus,
        long? beforeUsageCount,
        long? afterUsageCount,
        long? usageDelta,
        string? reason,
        DateTimeOffset occurredAtUtc)
    {
        OrganizationId = Required(organizationId);
        EnvironmentId = Required(environmentId);
        OperationKind = Required(operationKind);
        ToolingAssetId = Required(toolingAssetId);
        ToolingCode = Required(toolingCode);
        ActorId = RequiredActor(actorId);
        CorrelationId = RequiredOpaqueIdentity(correlationId, nameof(correlationId));
        CausationId = RequiredOpaqueIdentity(causationId, nameof(causationId));
        OperationId = RequiredOpaqueIdentity(operationId, nameof(operationId));
        RequestFingerprint = Required(requestFingerprint);
        BeforeStatus = beforeStatus;
        AfterStatus = afterStatus;
        BeforeUsageCount = beforeUsageCount;
        AfterUsageCount = afterUsageCount;
        UsageDelta = usageDelta;
        Reason = OptionalAuditText(reason);
        OccurredAtUtc = occurredAtUtc.ToUniversalTime();
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string OperationKind { get; private set; } = string.Empty;
    public string ToolingAssetId { get; private set; } = string.Empty;
    public string ToolingCode { get; private set; } = string.Empty;
    public string ActorId { get; private set; } = string.Empty;
    public string CorrelationId { get; private set; } = string.Empty;
    public string CausationId { get; private set; } = string.Empty;
    public string OperationId { get; private set; } = string.Empty;
    public string RequestFingerprint { get; private set; } = string.Empty;
    public ToolingAssetStatus? BeforeStatus { get; private set; }
    public ToolingAssetStatus? AfterStatus { get; private set; }
    public long? BeforeUsageCount { get; private set; }
    public long? AfterUsageCount { get; private set; }
    public long? UsageDelta { get; private set; }
    public string? Reason { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }

    public static ToolingAuditEntry Register(
        string organizationId,
        string environmentId,
        string toolingAssetId,
        string toolingCode,
        string actorId,
        string correlationId,
        string causationId,
        string operationId,
        string requestFingerprint,
        DateTimeOffset occurredAtUtc) => new(
            organizationId,
            environmentId,
            RegisterOperation,
            toolingAssetId,
            toolingCode,
            actorId,
            correlationId,
            causationId,
            operationId,
            requestFingerprint,
            beforeStatus: null,
            afterStatus: ToolingAssetStatus.Available,
            beforeUsageCount: null,
            afterUsageCount: 0,
            usageDelta: null,
            reason: null,
            occurredAtUtc);

    public static ToolingAuditEntry Status(
        string organizationId,
        string environmentId,
        string toolingAssetId,
        string toolingCode,
        string actorId,
        string correlationId,
        string causationId,
        string operationId,
        string requestFingerprint,
        ToolingAssetStatus beforeStatus,
        ToolingAssetStatus afterStatus,
        string reason,
        DateTimeOffset occurredAtUtc) => new(
            organizationId,
            environmentId,
            StatusOperation,
            toolingAssetId,
            toolingCode,
            actorId,
            correlationId,
            causationId,
            operationId,
            requestFingerprint,
            beforeStatus,
            afterStatus,
            beforeUsageCount: null,
            afterUsageCount: null,
            usageDelta: null,
            reason,
            occurredAtUtc);

    public static ToolingAuditEntry Usage(
        string organizationId,
        string environmentId,
        string toolingAssetId,
        string toolingCode,
        string actorId,
        string correlationId,
        string causationId,
        string operationId,
        string requestFingerprint,
        long beforeUsageCount,
        long afterUsageCount,
        long usageDelta,
        DateTimeOffset occurredAtUtc) => new(
            organizationId,
            environmentId,
            UsageOperation,
            toolingAssetId,
            toolingCode,
            actorId,
            correlationId,
            causationId,
            operationId,
            requestFingerprint,
            beforeStatus: null,
            afterStatus: null,
            beforeUsageCount,
            afterUsageCount,
            usageDelta,
            reason: null,
            occurredAtUtc);

    public bool Matches(string actorId, string requestFingerprint) =>
        string.Equals(ActorId, actorId, StringComparison.Ordinal) &&
        string.Equals(RequestFingerprint, requestFingerprint, StringComparison.Ordinal);

    private static string Required(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value cannot be blank.", nameof(value))
            : value.Trim();

    private static string RequiredActor(string value) =>
        ToolingAuditIdentityPolicy.IsValidActor(value)
            ? value
            : throw new ArgumentException("Actor must be a canonical, non-sensitive user identity.", nameof(value));

    private static string RequiredOpaqueIdentity(string value, string parameterName) =>
        ToolingAuditIdentityPolicy.IsValidOpaqueIdentity(value)
            ? value
            : throw new ArgumentException("Audit identity must be canonical and non-sensitive.", parameterName);

    private static string? Optional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? OptionalAuditText(string? value)
    {
        var normalized = Optional(value);
        return normalized is null || ToolingAuditIdentityPolicy.IsValidAuditText(normalized)
            ? normalized
            : throw new ArgumentException("Audit text cannot contain credentials or sensitive markers.", nameof(value));
    }
}
