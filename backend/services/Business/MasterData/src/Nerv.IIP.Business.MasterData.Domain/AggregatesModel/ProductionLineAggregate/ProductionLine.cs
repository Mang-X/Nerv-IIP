using Nerv.IIP.Business.MasterData.Domain.DomainEvents;

namespace Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ProductionLineAggregate;

public partial record ProductionLineId : IGuidStronglyTypedId;

public class ProductionLine : Entity<ProductionLineId>, IAggregateRoot
{
    protected ProductionLine()
    {
    }

    private ProductionLine(string organizationId, string environmentId, string code, string name, string siteCode, string? workshopCode)
    {
        OrganizationId = Required(organizationId);
        EnvironmentId = Required(environmentId);
        Code = Required(code);
        Name = Required(name);
        SiteCode = Required(siteCode);
        WorkshopCode = Optional(workshopCode);
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
        this.AddDomainEvent(new MasterDataAggregateCreatedDomainEvent(nameof(ProductionLine), OrganizationId, EnvironmentId, Code));
        this.AddDomainEvent(new ResourceChangedDomainEvent(nameof(ProductionLine), OrganizationId, EnvironmentId, Code));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string SiteCode { get; private set; } = string.Empty;
    public string? WorkshopCode { get; private set; }
    public bool Disabled { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static ProductionLine Create(string organizationId, string environmentId, string code, string name, string siteCode, string? workshopCode = null)
    {
        return new ProductionLine(organizationId, environmentId, code, name, siteCode, workshopCode);
    }

    public void Update(string name, string siteCode, string? workshopCode = null)
    {
        EnsureEnabled();
        Name = Required(name);
        SiteCode = Required(siteCode);
        WorkshopCode = Optional(workshopCode);
        Touch();
    }

    public void Disable(string reason)
    {
        var validReason = Required(reason);
        EnsureEnabled();
        Disabled = true;
        UpdatedAtUtc = DateTime.UtcNow;
        this.AddDomainEvent(new MasterDataAggregateDisabledDomainEvent(nameof(ProductionLine), OrganizationId, EnvironmentId, Code, validReason));
        this.AddDomainEvent(new ResourceChangedDomainEvent(nameof(ProductionLine), OrganizationId, EnvironmentId, Code));
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
        this.AddDomainEvent(new MasterDataAggregateUpdatedDomainEvent(nameof(ProductionLine), OrganizationId, EnvironmentId, Code));
        this.AddDomainEvent(new ResourceChangedDomainEvent(nameof(ProductionLine), OrganizationId, EnvironmentId, Code));
    }

    private void EnsureEnabled()
    {
        if (Disabled)
        {
            throw new InvalidOperationException("Disabled production line cannot be changed.");
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
}
