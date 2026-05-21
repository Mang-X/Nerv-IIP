using Nerv.IIP.Business.MasterData.Domain.DomainEvents;

namespace Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SkuAggregate;

public partial record SkuId : IGuidStronglyTypedId;

public class Sku : Entity<SkuId>, IAggregateRoot
{
    protected Sku()
    {
    }

    private Sku(string organizationId, string environmentId, string code, string name, string unit, string category)
    {
        OrganizationId = Required(organizationId);
        EnvironmentId = Required(environmentId);
        Code = Required(code);
        Name = Required(name);
        Unit = Required(unit);
        Category = Required(category);
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
        this.AddDomainEvent(new MasterDataAggregateCreatedDomainEvent(nameof(Sku), OrganizationId, EnvironmentId, Code));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Unit { get; private set; } = string.Empty;
    public string Category { get; private set; } = string.Empty;
    public bool Disabled { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static Sku Create(string organizationId, string environmentId, string code, string name, string unit, string category)
    {
        return new Sku(organizationId, environmentId, code, name, unit, category);
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
        this.AddDomainEvent(new MasterDataAggregateDisabledDomainEvent(nameof(Sku), OrganizationId, EnvironmentId, Code, validReason));
    }

    private void Touch()
    {
        UpdatedAtUtc = DateTime.UtcNow;
        this.AddDomainEvent(new MasterDataAggregateUpdatedDomainEvent(nameof(Sku), OrganizationId, EnvironmentId, Code));
    }

    private void EnsureEnabled()
    {
        if (Disabled)
        {
            throw new InvalidOperationException("Disabled SKU cannot be changed.");
        }
    }

    private static string Required(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value cannot be blank.", nameof(value)) : value.Trim();
    }
}
