using Nerv.IIP.Business.MasterData.Domain.DomainEvents;

namespace Nerv.IIP.Business.MasterData.Domain.AggregatesModel.BusinessPartnerAggregate;

public partial record BusinessPartnerId : IGuidStronglyTypedId;

public class BusinessPartner : Entity<BusinessPartnerId>, IAggregateRoot
{
    protected BusinessPartner()
    {
    }

    private BusinessPartner(
        string organizationId,
        string environmentId,
        string code,
        string partnerType,
        string name,
        IReadOnlyCollection<string>? partnerRoles,
        string? taxId)
    {
        OrganizationId = Required(organizationId);
        EnvironmentId = Required(environmentId);
        Code = Required(code);
        PartnerRoles = NormalizeRoles(partnerType, partnerRoles);
        PartnerType = PartnerRoles[0];
        Name = Required(name);
        TaxId = Optional(taxId);
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
        this.AddDomainEvent(new MasterDataAggregateCreatedDomainEvent(nameof(BusinessPartner), OrganizationId, EnvironmentId, Code));
        this.AddDomainEvent(new BusinessPartnerChangedDomainEvent(OrganizationId, EnvironmentId, Code));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string Code { get; private set; } = string.Empty;
    public string PartnerType { get; private set; } = string.Empty;
    public string[] PartnerRoles { get; private set; } = [];
    public string Name { get; private set; } = string.Empty;
    public string? TaxId { get; private set; }
    public bool Disabled { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static BusinessPartner Create(string organizationId, string environmentId, string code, string partnerType, string name)
    {
        return Create(organizationId, environmentId, code, partnerType, name, [partnerType], null);
    }

    public static BusinessPartner Create(
        string organizationId,
        string environmentId,
        string code,
        string partnerType,
        string name,
        IReadOnlyCollection<string>? partnerRoles,
        string? taxId)
    {
        return new BusinessPartner(organizationId, environmentId, code, partnerType, name, partnerRoles, taxId);
    }

    public void Rename(string name)
    {
        var validName = Required(name);
        EnsureEnabled();
        Name = validName;
        Touch();
    }

    public void Update(string name, string partnerType)
    {
        Update(name, [partnerType], TaxId);
    }

    public void Update(string name, IReadOnlyCollection<string>? partnerRoles, string? taxId)
    {
        EnsureEnabled();
        Name = Required(name);
        PartnerRoles = NormalizeRoles(PartnerType, partnerRoles);
        PartnerType = PartnerRoles[0];
        TaxId = Optional(taxId);
        Touch();
    }

    public void Disable(string reason)
    {
        var validReason = Required(reason);
        EnsureEnabled();
        Disabled = true;
        UpdatedAtUtc = DateTime.UtcNow;
        this.AddDomainEvent(new MasterDataAggregateDisabledDomainEvent(nameof(BusinessPartner), OrganizationId, EnvironmentId, Code, validReason));
        this.AddDomainEvent(new BusinessPartnerChangedDomainEvent(OrganizationId, EnvironmentId, Code));
    }

    public void Enable(string reason)
    {
        _ = Required(reason);
        if (!Disabled)
        {
            return;
        }

        Disabled = false;
        Touch();
    }

    private void Touch()
    {
        UpdatedAtUtc = DateTime.UtcNow;
        this.AddDomainEvent(new MasterDataAggregateUpdatedDomainEvent(nameof(BusinessPartner), OrganizationId, EnvironmentId, Code));
        this.AddDomainEvent(new BusinessPartnerChangedDomainEvent(OrganizationId, EnvironmentId, Code));
    }

    private void EnsureEnabled()
    {
        if (Disabled)
        {
            throw new InvalidOperationException("Disabled business partner cannot be changed.");
        }
    }

    private static string Required(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value cannot be blank.", nameof(value)) : value.Trim();
    }

    private static string? Optional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string[] NormalizeRoles(string partnerType, IReadOnlyCollection<string>? partnerRoles)
    {
        var roles = (partnerRoles is { Count: > 0 } ? partnerRoles : [partnerType])
            .Select(Required)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return roles.Length == 0 ? [Required(partnerType)] : roles;
    }
}
