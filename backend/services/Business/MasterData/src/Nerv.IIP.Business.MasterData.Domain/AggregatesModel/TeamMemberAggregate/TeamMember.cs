using Nerv.IIP.Business.MasterData.Domain.DomainEvents;

namespace Nerv.IIP.Business.MasterData.Domain.AggregatesModel.TeamMemberAggregate;

public partial record TeamMemberId : IGuidStronglyTypedId;

public class TeamMember : Entity<TeamMemberId>, IAggregateRoot
{
    protected TeamMember()
    {
    }

    private TeamMember(
        string organizationId,
        string environmentId,
        string teamCode,
        string userId,
        bool isLeader,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo)
    {
        if (effectiveTo.HasValue && effectiveTo.Value < effectiveFrom)
        {
            throw new ArgumentOutOfRangeException(nameof(effectiveTo), "Effective end date cannot be before the start date.");
        }

        OrganizationId = Required(organizationId);
        EnvironmentId = Required(environmentId);
        TeamCode = Required(teamCode);
        UserId = Required(userId);
        IsLeader = isLeader;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
        this.AddDomainEvent(new MasterDataAggregateCreatedDomainEvent(nameof(TeamMember), OrganizationId, EnvironmentId, Code));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string TeamCode { get; private set; } = string.Empty;
    public string UserId { get; private set; } = string.Empty;
    public bool IsLeader { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public DateOnly? EffectiveTo { get; private set; }
    public bool Disabled { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public string Code => $"{TeamCode}:{UserId}";

    public static TeamMember Assign(
        string organizationId,
        string environmentId,
        string teamCode,
        string userId,
        bool isLeader,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo)
    {
        return new TeamMember(organizationId, environmentId, teamCode, userId, isLeader, effectiveFrom, effectiveTo);
    }

    public bool IsEffectiveOn(DateOnly date)
    {
        return !Disabled && date >= EffectiveFrom && (!EffectiveTo.HasValue || date <= EffectiveTo.Value);
    }

    public void Remove(string reason)
    {
        var validReason = Required(reason);
        EnsureEnabled();
        Disabled = true;
        UpdatedAtUtc = DateTime.UtcNow;
        this.AddDomainEvent(new MasterDataAggregateDisabledDomainEvent(nameof(TeamMember), OrganizationId, EnvironmentId, Code, validReason));
    }

    private void EnsureEnabled()
    {
        if (Disabled)
        {
            throw new InvalidOperationException("Disabled team member cannot be changed.");
        }
    }

    private static string Required(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value cannot be blank.", nameof(value)) : value.Trim();
    }
}
