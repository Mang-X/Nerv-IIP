namespace Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ScopeContextAuditAggregate;

public partial record MasterDataScopeContextAuditEntryId : IGuidStronglyTypedId;

public sealed class MasterDataScopeContextAuditEntry : Entity<MasterDataScopeContextAuditEntryId>
{
    private MasterDataScopeContextAuditEntry() { }

    public MasterDataScopeContextAuditEntry(
        string organizationId,
        string environmentId,
        string operationKind,
        string resourceType,
        string resourceId,
        string resourceCode,
        string resourceIdentity,
        string actorId,
        string correlationId,
        string causationId,
        string operationId,
        string beforeJson,
        string afterJson,
        string reason,
        DateTimeOffset occurredAtUtc)
    {
        OrganizationId = Required(organizationId);
        EnvironmentId = Required(environmentId);
        OperationKind = Required(operationKind);
        ResourceType = Required(resourceType);
        ResourceId = Required(resourceId);
        ResourceCode = Required(resourceCode);
        ResourceIdentity = Required(resourceIdentity);
        ActorId = Required(actorId);
        CorrelationId = Required(correlationId);
        CausationId = Required(causationId);
        OperationId = Required(operationId);
        BeforeJson = Required(beforeJson);
        AfterJson = Required(afterJson);
        Reason = Required(reason);
        OccurredAtUtc = occurredAtUtc;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string OperationKind { get; private set; } = string.Empty;
    public string ResourceType { get; private set; } = string.Empty;
    public string ResourceId { get; private set; } = string.Empty;
    public string ResourceCode { get; private set; } = string.Empty;
    public string ResourceIdentity { get; private set; } = string.Empty;
    public string ActorId { get; private set; } = string.Empty;
    public string CorrelationId { get; private set; } = string.Empty;
    public string CausationId { get; private set; } = string.Empty;
    public string OperationId { get; private set; } = string.Empty;
    public string BeforeJson { get; private set; } = string.Empty;
    public string AfterJson { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; private set; }

    private static string Required(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value cannot be blank.", nameof(value))
            : value.Trim();
}
