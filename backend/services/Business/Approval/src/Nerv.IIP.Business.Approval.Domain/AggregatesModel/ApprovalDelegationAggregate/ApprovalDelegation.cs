using Nerv.IIP.Business.Approval.Domain.AggregatesModel;

namespace Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalDelegationAggregate;

public partial record ApprovalDelegationId : IGuidStronglyTypedId;

public sealed class ApprovalDelegation : Entity<ApprovalDelegationId>, IAggregateRoot
{
    private ApprovalDelegation()
    {
    }

    private ApprovalDelegation(
        string organizationId,
        string environmentId,
        string delegatorActorType,
        string delegatorActorRef,
        string delegateActorType,
        string delegateActorRef,
        string? documentType,
        DateTimeOffset effectiveFromUtc,
        DateTimeOffset effectiveToUtc,
        string? reason,
        string createdBy)
    {
        if (effectiveToUtc <= effectiveFromUtc)
        {
            throw new ArgumentException("Delegation effective end must be after start.", nameof(effectiveToUtc));
        }

        Id = new ApprovalDelegationId(Guid.CreateVersion7());
        OrganizationId = ApprovalText.Required(organizationId);
        EnvironmentId = ApprovalText.Required(environmentId);
        DelegatorActorType = ApprovalText.RequiredLower(delegatorActorType);
        DelegatorActorRef = ApprovalText.Required(delegatorActorRef);
        DelegateActorType = ApprovalText.RequiredLower(delegateActorType);
        DelegateActorRef = ApprovalText.Required(delegateActorRef);
        DocumentType = ApprovalText.Optional(documentType);
        EffectiveFromUtc = effectiveFromUtc;
        EffectiveToUtc = effectiveToUtc;
        Reason = ApprovalText.Optional(reason);
        CreatedBy = ApprovalText.Required(createdBy);
        CreatedAtUtc = DateTimeOffset.UtcNow;
        Status = ApprovalDelegationStatuses.Active;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string DelegatorActorType { get; private set; } = string.Empty;
    public string DelegatorActorRef { get; private set; } = string.Empty;
    public string DelegateActorType { get; private set; } = string.Empty;
    public string DelegateActorRef { get; private set; } = string.Empty;
    public string? DocumentType { get; private set; }
    public DateTimeOffset EffectiveFromUtc { get; private set; }
    public DateTimeOffset EffectiveToUtc { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public string? Reason { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public string? RevokedBy { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public static ApprovalDelegation Create(
        string organizationId,
        string environmentId,
        string delegatorActorType,
        string delegatorActorRef,
        string delegateActorType,
        string delegateActorRef,
        string? documentType,
        DateTimeOffset effectiveFromUtc,
        DateTimeOffset effectiveToUtc,
        string? reason,
        string createdBy) =>
        new(
            organizationId,
            environmentId,
            delegatorActorType,
            delegatorActorRef,
            delegateActorType,
            delegateActorRef,
            documentType,
            effectiveFromUtc,
            effectiveToUtc,
            reason,
            createdBy);

    public void Revoke(string revokedBy)
    {
        if (Status == ApprovalDelegationStatuses.Revoked)
        {
            return;
        }

        Status = ApprovalDelegationStatuses.Revoked;
        RevokedBy = ApprovalText.Required(revokedBy);
        RevokedAtUtc = DateTimeOffset.UtcNow;
    }
}

public static class ApprovalDelegationStatuses
{
    public const string Active = "active";
    public const string Revoked = "revoked";
}
