using Nerv.IIP.Business.MasterData.Domain.DomainEvents;

namespace Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SiteAggregate;

public partial record SiteId : IGuidStronglyTypedId;

public class Site : Entity<SiteId>, IAggregateRoot
{
    protected Site()
    {
    }

    private Site(string organizationId, string environmentId, string code, string name, string timezone)
    {
        OrganizationId = Required(organizationId);
        EnvironmentId = Required(environmentId);
        Code = Required(code);
        Name = Required(name);
        Timezone = Required(timezone);
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
        this.AddDomainEvent(new MasterDataAggregateCreatedDomainEvent(nameof(Site), OrganizationId, EnvironmentId, Code));
        this.AddDomainEvent(new ResourceChangedDomainEvent(nameof(Site), OrganizationId, EnvironmentId, Code));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Timezone { get; private set; } = string.Empty;
    public bool Disabled { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static Site Create(string organizationId, string environmentId, string code, string name, string timezone)
    {
        return new Site(organizationId, environmentId, code, name, timezone);
    }

    public void Update(string name, string timezone)
    {
        EnsureEnabled();
        Name = Required(name);
        Timezone = Required(timezone);
        Touch();
    }

    public void Disable(string reason)
    {
        var validReason = Required(reason);
        EnsureEnabled();
        Disabled = true;
        UpdatedAtUtc = DateTime.UtcNow;
        this.AddDomainEvent(new MasterDataAggregateDisabledDomainEvent(nameof(Site), OrganizationId, EnvironmentId, Code, validReason));
        this.AddDomainEvent(new ResourceChangedDomainEvent(nameof(Site), OrganizationId, EnvironmentId, Code));
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
        this.AddDomainEvent(new MasterDataAggregateUpdatedDomainEvent(nameof(Site), OrganizationId, EnvironmentId, Code));
        this.AddDomainEvent(new ResourceChangedDomainEvent(nameof(Site), OrganizationId, EnvironmentId, Code));
    }

    private void EnsureEnabled()
    {
        if (Disabled)
        {
            throw new InvalidOperationException("Disabled site cannot be changed.");
        }
    }

    private static string Required(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value cannot be blank.", nameof(value)) : value.Trim();
    }
}
