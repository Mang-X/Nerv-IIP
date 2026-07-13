namespace Nerv.IIP.Business.MasterData.Domain.AggregatesModel.LifecycleAuditAggregate;

public partial record MasterDataLifecycleAuditEntryId : IGuidStronglyTypedId;

public sealed class MasterDataLifecycleAuditEntry : Entity<MasterDataLifecycleAuditEntryId>
{
    private MasterDataLifecycleAuditEntry() { }

    public MasterDataLifecycleAuditEntry(
        string organizationId,
        string environmentId,
        string resourceType,
        string resourceId,
        string resourceCode,
        string resourceIdentity,
        bool targetEnabled,
        string actorId,
        string reason,
        string operationId,
        DateTimeOffset occurredAtUtc)
    {
        OrganizationId = organizationId;
        EnvironmentId = environmentId;
        ResourceType = resourceType;
        ResourceId = resourceId;
        ResourceCode = resourceCode;
        ResourceIdentity = resourceIdentity;
        TargetEnabled = targetEnabled;
        ActorId = actorId;
        Reason = reason;
        OperationId = operationId;
        OccurredAtUtc = occurredAtUtc;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string ResourceType { get; private set; } = string.Empty;
    public string ResourceId { get; private set; } = string.Empty;
    public string ResourceCode { get; private set; } = string.Empty;
    public string ResourceIdentity { get; private set; } = string.Empty;
    public bool TargetEnabled { get; private set; }
    public string ActorId { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public string OperationId { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; private set; }
}
