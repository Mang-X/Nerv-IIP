using Nerv.IIP.Business.MasterData.Domain.DomainEvents;

namespace Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SkillAggregate;

public partial record SkillId : IGuidStronglyTypedId;

public sealed class Skill : Entity<SkillId>, IAggregateRoot
{
    private Skill()
    {
    }

    private Skill(
        string organizationId,
        string environmentId,
        string skillCode,
        string skillName,
        string groupName,
        bool requiresCertification,
        int? validityMonths,
        string? description)
    {
        OrganizationId = Required(organizationId);
        EnvironmentId = Required(environmentId);
        SkillCode = Required(skillCode);
        SkillName = Required(skillName);
        GroupName = Required(groupName);
        RequiresCertification = requiresCertification;
        ValidityMonths = NormalizeValidity(requiresCertification, validityMonths);
        Description = Optional(description);
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
        this.AddDomainEvent(new MasterDataAggregateCreatedDomainEvent(nameof(Skill), OrganizationId, EnvironmentId, SkillCode));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string SkillCode { get; private set; } = string.Empty;
    public string SkillName { get; private set; } = string.Empty;
    public string GroupName { get; private set; } = string.Empty;
    public bool RequiresCertification { get; private set; }
    public int? ValidityMonths { get; private set; }
    public string? Description { get; private set; }
    public bool Disabled { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static Skill Create(
        string organizationId,
        string environmentId,
        string skillCode,
        string skillName,
        string groupName,
        bool requiresCertification,
        int? validityMonths,
        string? description)
    {
        return new Skill(organizationId, environmentId, skillCode, skillName, groupName, requiresCertification, validityMonths, description);
    }

    public void Update(
        string skillName,
        string groupName,
        bool requiresCertification,
        int? validityMonths,
        string? description)
    {
        EnsureEnabled();
        SkillName = Required(skillName);
        GroupName = Required(groupName);
        RequiresCertification = requiresCertification;
        ValidityMonths = NormalizeValidity(requiresCertification, validityMonths);
        Description = Optional(description);
        UpdatedAtUtc = DateTime.UtcNow;
        this.AddDomainEvent(new MasterDataAggregateUpdatedDomainEvent(nameof(Skill), OrganizationId, EnvironmentId, SkillCode));
    }

    public void Disable(string reason)
    {
        var validReason = Required(reason);
        EnsureEnabled();
        Disabled = true;
        UpdatedAtUtc = DateTime.UtcNow;
        this.AddDomainEvent(new MasterDataAggregateDisabledDomainEvent(nameof(Skill), OrganizationId, EnvironmentId, SkillCode, validReason));
    }

    public void Enable(string reason)
    {
        _ = Required(reason);
        if (!Disabled)
        {
            return;
        }

        Disabled = false;
        UpdatedAtUtc = DateTime.UtcNow;
        this.AddDomainEvent(new MasterDataAggregateUpdatedDomainEvent(nameof(Skill), OrganizationId, EnvironmentId, SkillCode));
    }

    private void EnsureEnabled()
    {
        if (Disabled)
        {
            throw new InvalidOperationException("Disabled skill cannot be changed.");
        }
    }

    private static int? NormalizeValidity(bool requiresCertification, int? validityMonths)
    {
        if (!requiresCertification)
        {
            return validityMonths.HasValue && validityMonths.Value <= 0
                ? throw new ArgumentOutOfRangeException(nameof(validityMonths), "Validity months must be positive.")
                : validityMonths;
        }

        if (!validityMonths.HasValue)
        {
            throw new ArgumentException("Validity months are required when certification is required.", nameof(validityMonths));
        }

        if (validityMonths.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(validityMonths), "Validity months must be positive.");
        }

        return validityMonths.Value;
    }

    private static string Required(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value cannot be blank.", nameof(value)) : value.Trim();
    }

    private static string? Optional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
