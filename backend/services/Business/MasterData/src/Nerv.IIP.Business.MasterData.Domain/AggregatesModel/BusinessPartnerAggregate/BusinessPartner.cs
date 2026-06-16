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
        string? taxId,
        string? taxRegionCode = null,
        string? defaultCurrencyCode = null,
        string? paymentTermsCode = null,
        string? primaryAddress = null,
        string? primaryContactName = null,
        string? primaryContactEmail = null,
        string? primaryContactPhone = null)
    {
        OrganizationId = Required(organizationId);
        EnvironmentId = Required(environmentId);
        Code = Required(code);
        PartnerRoles = NormalizeRoles(partnerType, partnerRoles);
        PartnerType = PartnerRoles[0];
        Name = Required(name);
        TaxId = Optional(taxId);
        TaxRegionCode = Optional(taxRegionCode);
        DefaultCurrencyCode = Optional(defaultCurrencyCode);
        PaymentTermsCode = Optional(paymentTermsCode);
        PrimaryAddress = Optional(primaryAddress);
        PrimaryContactName = Optional(primaryContactName);
        PrimaryContactEmail = Optional(primaryContactEmail);
        PrimaryContactPhone = Optional(primaryContactPhone);
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
    public string? TaxRegionCode { get; private set; }
    public string? DefaultCurrencyCode { get; private set; }
    public string? PaymentTermsCode { get; private set; }
    public string? PrimaryAddress { get; private set; }
    public string? PrimaryContactName { get; private set; }
    public string? PrimaryContactEmail { get; private set; }
    public string? PrimaryContactPhone { get; private set; }
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
        string? taxId,
        string? taxRegionCode = null,
        string? defaultCurrencyCode = null,
        string? paymentTermsCode = null,
        string? primaryAddress = null,
        string? primaryContactName = null,
        string? primaryContactEmail = null,
        string? primaryContactPhone = null)
    {
        return new BusinessPartner(
            organizationId,
            environmentId,
            code,
            partnerType,
            name,
            partnerRoles,
            taxId,
            taxRegionCode,
            defaultCurrencyCode,
            paymentTermsCode,
            primaryAddress,
            primaryContactName,
            primaryContactEmail,
            primaryContactPhone);
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
        Update(name, partnerRoles, taxId, TaxRegionCode, DefaultCurrencyCode, PaymentTermsCode, PrimaryAddress, PrimaryContactName, PrimaryContactEmail, PrimaryContactPhone);
    }

    public void Update(
        string name,
        IReadOnlyCollection<string>? partnerRoles,
        string? taxId,
        string? taxRegionCode,
        string? defaultCurrencyCode,
        string? paymentTermsCode,
        string? primaryAddress,
        string? primaryContactName,
        string? primaryContactEmail,
        string? primaryContactPhone)
    {
        EnsureEnabled();
        Name = Required(name);
        PartnerRoles = NormalizeRoles(partnerRoles?.FirstOrDefault() ?? PartnerType, partnerRoles);
        PartnerType = PartnerRoles[0];
        TaxId = Optional(taxId);
        TaxRegionCode = Optional(taxRegionCode);
        DefaultCurrencyCode = Optional(defaultCurrencyCode);
        PaymentTermsCode = Optional(paymentTermsCode);
        PrimaryAddress = Optional(primaryAddress);
        PrimaryContactName = Optional(primaryContactName);
        PrimaryContactEmail = Optional(primaryContactEmail);
        PrimaryContactPhone = Optional(primaryContactPhone);
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
