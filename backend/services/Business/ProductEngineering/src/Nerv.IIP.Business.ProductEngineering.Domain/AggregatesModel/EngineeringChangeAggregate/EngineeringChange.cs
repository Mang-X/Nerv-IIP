using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ProductionVersionAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.DomainEvents;
using static Nerv.IIP.Business.ProductEngineering.Domain.ProductEngineeringGuards;

namespace Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringChangeAggregate;

public partial record EngineeringChangeId : IGuidStronglyTypedId;

public sealed class EngineeringChange : Entity<EngineeringChangeId>, IAggregateRoot
{
    private readonly List<EngineeringChangeAffectedVersion> affectedVersions = [];

    private EngineeringChange()
    {
    }

    private EngineeringChange(string organizationId, string environmentId, string changeNumber, string reason)
    {
        OrganizationId = Required(organizationId);
        EnvironmentId = Required(environmentId);
        ChangeNumber = Required(changeNumber);
        Reason = Required(reason);
        Status = EngineeringVersionStatus.Draft;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string ChangeNumber { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public string ApprovalReferenceId { get; private set; } = string.Empty;
    public EngineeringVersionStatus Status { get; private set; }
    public DateOnly? EffectiveDate { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public IReadOnlyCollection<EngineeringChangeAffectedVersion> AffectedVersions => affectedVersions.AsReadOnly();

    public static EngineeringChange Open(string organizationId, string environmentId, string changeNumber, string reason)
    {
        return new EngineeringChange(organizationId, environmentId, changeNumber, reason);
    }

    public EngineeringChange Affect(string versionKind, string versionId, string? supersededByVersionId = null)
    {
        EnsureDraft();
        versionKind = Required(versionKind);
        versionId = Required(versionId);
        supersededByVersionId = Optional(supersededByVersionId);
        var existing = affectedVersions.SingleOrDefault(x => SameAffectedVersion(x, versionKind, versionId));
        if (existing is not null)
        {
            if (!SameOptionalVersionId(existing.SupersededByVersionId, supersededByVersionId))
            {
                throw new InvalidOperationException($"Affected {versionKind} version '{versionId}' can only declare one successor in the same engineering change.");
            }

            return this;
        }

        affectedVersions.Add(new EngineeringChangeAffectedVersion(versionKind, versionId, supersededByVersionId));
        Touch();
        return this;
    }

    private static bool SameAffectedVersion(EngineeringChangeAffectedVersion affectedVersion, string versionKind, string versionId)
    {
        return string.Equals(affectedVersion.VersionKind, versionKind, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(affectedVersion.VersionId, versionId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool SameOptionalVersionId(string? left, string? right)
    {
        return string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    public EngineeringChange Approve(string approvalReferenceId)
    {
        EnsureDraft();
        ApprovalReferenceId = Required(approvalReferenceId);
        Touch();
        return this;
    }

    public void Release(DateOnly effectiveDate)
    {
        EnsureDraft();
        if (string.IsNullOrWhiteSpace(ApprovalReferenceId))
        {
            throw new InvalidOperationException("Engineering change requires an approval reference before release.");
        }

        if (affectedVersions.Count == 0)
        {
            throw new InvalidOperationException("Engineering change requires at least one affected version before release.");
        }

        Status = EngineeringVersionStatus.Published;
        EffectiveDate = effectiveDate;
        Touch();
        AddDomainEvent(new EngineeringChangeReleasedDomainEvent(this));
    }

    private void EnsureDraft()
    {
        if (Status != EngineeringVersionStatus.Draft)
        {
            throw new InvalidOperationException("Released engineering change cannot be changed directly.");
        }
    }

    private void Touch()
    {
        UpdatedAtUtc = DateTime.UtcNow;
    }
}

public sealed class EngineeringChangeAffectedVersion
{
    private EngineeringChangeAffectedVersion()
    {
    }

    internal EngineeringChangeAffectedVersion(string versionKind, string versionId, string? supersededByVersionId)
    {
        VersionKind = versionKind;
        VersionId = versionId;
        SupersededByVersionId = supersededByVersionId;
    }

    public string VersionKind { get; private set; } = string.Empty;
    public string VersionId { get; private set; } = string.Empty;
    public string? SupersededByVersionId { get; private set; }
}
