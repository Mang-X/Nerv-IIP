using Nerv.IIP.Business.MasterData.Domain.DomainEvents;

namespace Nerv.IIP.Business.MasterData.Domain.AggregatesModel.PersonnelSkillAggregate;

public partial record PersonnelSkillId : IGuidStronglyTypedId;

public class PersonnelSkill : Entity<PersonnelSkillId>, IAggregateRoot
{
    protected PersonnelSkill()
    {
    }

    private PersonnelSkill(
        string organizationId,
        string environmentId,
        string userId,
        string skillCode,
        string level,
        DateOnly effectiveFrom,
        DateOnly effectiveTo)
    {
        if (effectiveTo < effectiveFrom)
        {
            throw new ArgumentOutOfRangeException(nameof(effectiveTo), "Effective end date cannot be before the start date.");
        }

        OrganizationId = Required(organizationId);
        EnvironmentId = Required(environmentId);
        UserId = Required(userId);
        SkillCode = Required(skillCode);
        Level = Required(level);
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
        this.AddDomainEvent(new MasterDataAggregateCreatedDomainEvent(nameof(PersonnelSkill), OrganizationId, EnvironmentId, SkillCode));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string UserId { get; private set; } = string.Empty;
    public string SkillCode { get; private set; } = string.Empty;
    public string Level { get; private set; } = string.Empty;
    public DateOnly EffectiveFrom { get; private set; }
    public DateOnly EffectiveTo { get; private set; }
    public bool Disabled { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static PersonnelSkill Assign(
        string organizationId,
        string environmentId,
        string userId,
        string skillCode,
        string level,
        DateOnly effectiveFrom,
        DateOnly effectiveTo)
    {
        return new PersonnelSkill(organizationId, environmentId, userId, skillCode, level, effectiveFrom, effectiveTo);
    }

    public bool IsValidOn(DateOnly date)
    {
        return !Disabled && date >= EffectiveFrom && date <= EffectiveTo;
    }

    public void Disable(string reason)
    {
        var validReason = Required(reason);
        EnsureEnabled();
        Disabled = true;
        UpdatedAtUtc = DateTime.UtcNow;
        this.AddDomainEvent(new MasterDataAggregateDisabledDomainEvent(nameof(PersonnelSkill), OrganizationId, EnvironmentId, SkillCode, validReason));
    }

    private void EnsureEnabled()
    {
        if (Disabled)
        {
            throw new InvalidOperationException("Disabled personnel skill cannot be changed.");
        }
    }

    private static string Required(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value cannot be blank.", nameof(value)) : value.Trim();
    }
}
