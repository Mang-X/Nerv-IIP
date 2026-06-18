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
        DateOnly effectiveFrom,
        DateOnly? effectiveTo)
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

        ValidateEffectiveRange(effectiveFrom, effectiveTo);
        OrganizationId = Required(organizationId);
        EnvironmentId = Required(environmentId);
        FromUomCode = validFrom;
        ToUomCode = validTo;
        Factor = factor;
        Offset = offset;
        Precision = precision;
        RoundingMode = Required(roundingMode);
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
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
    public DateOnly? EffectiveTo { get; private set; }
    public bool Disabled { get; private set; }
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
        DateOnly effectiveFrom,
        DateOnly? effectiveTo = null)
    {
        return new UomConversion(organizationId, environmentId, fromUomCode, toUomCode, factor, offset, precision, roundingMode, effectiveFrom, effectiveTo);
    }

    public void Update(decimal factor, decimal offset, int precision, string roundingMode, DateOnly? effectiveTo = null)
    {
        EnsureEnabled();
        ValidateFactor(factor);
        ValidatePrecision(precision);
        ValidateEffectiveRange(EffectiveFrom, effectiveTo);
        Factor = factor;
        Offset = offset;
        Precision = precision;
        RoundingMode = Required(roundingMode);
        EffectiveTo = effectiveTo;
        TouchUpdated();
    }

    public void Disable(string reason)
    {
        _ = Required(reason);
        if (Disabled)
        {
            return;
        }

        Disabled = true;
        TouchUpdated();
    }

    public void Enable(string reason)
    {
        _ = Required(reason);
        if (!Disabled)
        {
            return;
        }

        Disabled = false;
        TouchUpdated();
    }

    private void EnsureEnabled()
    {
        if (Disabled)
        {
            throw new InvalidOperationException("Disabled UOM conversion cannot be changed.");
        }
    }

    private void TouchUpdated()
    {
        UpdatedAtUtc = DateTime.UtcNow;
        this.AddDomainEvent(new UnitOfMeasureChangedDomainEvent(OrganizationId, EnvironmentId, FromUomCode));
    }

    private static void ValidateFactor(decimal factor)
    {
        if (factor <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(factor), "Conversion factor must be positive.");
        }
    }

    private static void ValidatePrecision(int precision)
    {
        if (precision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(precision), "Precision cannot be negative.");
        }
    }

    private static string Required(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value cannot be blank.", nameof(value)) : value.Trim();
    }

    private static void ValidateEffectiveRange(DateOnly effectiveFrom, DateOnly? effectiveTo)
    {
        if (effectiveTo.HasValue && effectiveTo.Value < effectiveFrom)
        {
            throw new ArgumentOutOfRangeException(nameof(effectiveTo), "Effective end date cannot be before effective start date.");
        }
    }
}
