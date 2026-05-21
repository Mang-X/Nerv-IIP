using Nerv.IIP.Business.MasterData.Domain.DomainEvents;

namespace Nerv.IIP.Business.MasterData.Domain.AggregatesModel.UomConversionAggregate;

public partial record UomConversionId : IGuidStronglyTypedId;

public class UomConversion : Entity<UomConversionId>, IAggregateRoot
{
    protected UomConversion()
    {
    }

    private UomConversion(
        string organizationId,
        string environmentId,
        string fromUomCode,
        string toUomCode,
        decimal factor,
        decimal offset,
        int precision,
        string roundingMode,
        DateOnly effectiveFrom)
    {
        var validFrom = Required(fromUomCode);
        var validTo = Required(toUomCode);
        if (string.Equals(validFrom, validTo, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("UOM conversion requires different units.", nameof(toUomCode));
        }

        if (factor <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(factor), "Conversion factor must be positive.");
        }

        if (precision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(precision), "Precision cannot be negative.");
        }

        OrganizationId = Required(organizationId);
        EnvironmentId = Required(environmentId);
        FromUomCode = validFrom;
        ToUomCode = validTo;
        Factor = factor;
        Offset = offset;
        Precision = precision;
        RoundingMode = Required(roundingMode);
        EffectiveFrom = effectiveFrom;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
        this.AddDomainEvent(new UnitOfMeasureChangedDomainEvent(OrganizationId, EnvironmentId, FromUomCode));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string FromUomCode { get; private set; } = string.Empty;
    public string ToUomCode { get; private set; } = string.Empty;
    public decimal Factor { get; private set; }
    public decimal Offset { get; private set; }
    public int Precision { get; private set; }
    public string RoundingMode { get; private set; } = string.Empty;
    public DateOnly EffectiveFrom { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static UomConversion Create(
        string organizationId,
        string environmentId,
        string fromUomCode,
        string toUomCode,
        decimal factor,
        decimal offset,
        int precision,
        string roundingMode,
        DateOnly effectiveFrom)
    {
        return new UomConversion(organizationId, environmentId, fromUomCode, toUomCode, factor, offset, precision, roundingMode, effectiveFrom);
    }

    private static string Required(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value cannot be blank.", nameof(value)) : value.Trim();
    }
}
