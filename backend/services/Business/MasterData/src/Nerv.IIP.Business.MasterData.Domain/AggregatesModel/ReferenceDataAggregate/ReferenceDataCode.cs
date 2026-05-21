using Nerv.IIP.Business.MasterData.Domain.DomainEvents;

namespace Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ReferenceDataAggregate;

public partial record ReferenceDataCodeId : IGuidStronglyTypedId;

public class ReferenceDataCode : Entity<ReferenceDataCodeId>, IAggregateRoot
{
    protected ReferenceDataCode()
    {
    }

    private ReferenceDataCode(string organizationId, string environmentId, string codeSet, string code, string name)
    {
        OrganizationId = Required(organizationId);
        EnvironmentId = Required(environmentId);
        CodeSet = Required(codeSet);
        Code = Required(code);
        Name = Required(name);
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
        this.AddDomainEvent(new MasterDataAggregateCreatedDomainEvent(nameof(ReferenceDataCode), OrganizationId, EnvironmentId, Code));
        this.AddDomainEvent(new ReferenceDataCodeChangedDomainEvent(OrganizationId, EnvironmentId, CodeSet, Code));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string CodeSet { get; private set; } = string.Empty;
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public bool Disabled { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static ReferenceDataCode Create(string organizationId, string environmentId, string codeSet, string code, string name)
    {
        return new ReferenceDataCode(organizationId, environmentId, codeSet, code, name);
    }

    public void Disable(string reason)
    {
        var validReason = Required(reason);
        if (Disabled)
        {
            throw new InvalidOperationException("Disabled reference data code cannot be changed.");
        }

        Disabled = true;
        UpdatedAtUtc = DateTime.UtcNow;
        this.AddDomainEvent(new MasterDataAggregateDisabledDomainEvent(nameof(ReferenceDataCode), OrganizationId, EnvironmentId, Code, validReason));
        this.AddDomainEvent(new ReferenceDataCodeChangedDomainEvent(OrganizationId, EnvironmentId, CodeSet, Code));
    }

    private static string Required(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value cannot be blank.", nameof(value)) : value.Trim();
    }
}
