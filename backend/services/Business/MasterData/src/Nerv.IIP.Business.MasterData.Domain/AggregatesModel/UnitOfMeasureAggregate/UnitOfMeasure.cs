using Nerv.IIP.Business.MasterData.Domain.DomainEvents;

namespace Nerv.IIP.Business.MasterData.Domain.AggregatesModel.UnitOfMeasureAggregate;

public partial record UnitOfMeasureId : IGuidStronglyTypedId;

public class UnitOfMeasure : Entity<UnitOfMeasureId>, IAggregateRoot
{
    protected UnitOfMeasure()
    {
    }

    private UnitOfMeasure(string organizationId, string environmentId, string code, string name, string dimensionType, int precision, string roundingMode)
    {
        if (precision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(precision), "Precision cannot be negative.");
        }

        OrganizationId = Required(organizationId);
        EnvironmentId = Required(environmentId);
        Code = Required(code);
        Name = Required(name);
        DimensionType = Required(dimensionType);
        Precision = precision;
        RoundingMode = Required(roundingMode);
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
        this.AddDomainEvent(new MasterDataAggregateCreatedDomainEvent(nameof(UnitOfMeasure), OrganizationId, EnvironmentId, Code));
        this.AddDomainEvent(new UnitOfMeasureChangedDomainEvent(OrganizationId, EnvironmentId, Code));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string DimensionType { get; private set; } = string.Empty;
    public int Precision { get; private set; }
    public string RoundingMode { get; private set; } = string.Empty;
    public bool Disabled { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static UnitOfMeasure Create(string organizationId, string environmentId, string code, string name, string dimensionType, int precision, string roundingMode)
    {
        return new UnitOfMeasure(organizationId, environmentId, code, name, dimensionType, precision, roundingMode);
    }

    public void Disable(string reason)
    {
        var validReason = Required(reason);
        EnsureEnabled();
        Disabled = true;
        UpdatedAtUtc = DateTime.UtcNow;
        this.AddDomainEvent(new MasterDataAggregateDisabledDomainEvent(nameof(UnitOfMeasure), OrganizationId, EnvironmentId, Code, validReason));
        this.AddDomainEvent(new UnitOfMeasureChangedDomainEvent(OrganizationId, EnvironmentId, Code));
    }

    private void EnsureEnabled()
    {
        if (Disabled)
        {
            throw new InvalidOperationException("Disabled unit of measure cannot be changed.");
        }
    }

    private static string Required(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value cannot be blank.", nameof(value)) : value.Trim();
    }
}
