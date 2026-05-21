using Nerv.IIP.Business.MasterData.Domain.DomainEvents;

namespace Nerv.IIP.Business.MasterData.Domain.AggregatesModel.BusinessPartnerAggregate;

public partial record BusinessPartnerId : IGuidStronglyTypedId;

public class BusinessPartner : Entity<BusinessPartnerId>, IAggregateRoot
{
    protected BusinessPartner()
    {
    }

    private BusinessPartner(string organizationId, string environmentId, string code, string partnerType, string name)
    {
        OrganizationId = Required(organizationId);
        EnvironmentId = Required(environmentId);
        Code = Required(code);
        PartnerType = Required(partnerType);
        Name = Required(name);
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
        this.AddDomainEvent(new MasterDataAggregateCreatedDomainEvent(nameof(BusinessPartner), OrganizationId, EnvironmentId, Code));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string Code { get; private set; } = string.Empty;
    public string PartnerType { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public bool Disabled { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static BusinessPartner Create(string organizationId, string environmentId, string code, string partnerType, string name)
    {
        return new BusinessPartner(organizationId, environmentId, code, partnerType, name);
    }

    public void Rename(string name)
    {
        var validName = Required(name);
        EnsureEnabled();
        Name = validName;
        Touch();
    }

    public void Disable(string reason)
    {
        var validReason = Required(reason);
        EnsureEnabled();
        Disabled = true;
        UpdatedAtUtc = DateTime.UtcNow;
        this.AddDomainEvent(new MasterDataAggregateDisabledDomainEvent(nameof(BusinessPartner), OrganizationId, EnvironmentId, Code, validReason));
    }

    private void Touch()
    {
        UpdatedAtUtc = DateTime.UtcNow;
        this.AddDomainEvent(new MasterDataAggregateUpdatedDomainEvent(nameof(BusinessPartner), OrganizationId, EnvironmentId, Code));
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
}
