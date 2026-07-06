namespace Nerv.IIP.Business.DemandPlanning.Web.Application.Planning;

internal sealed class PlanningUomConverter
{
    private readonly IReadOnlyDictionary<(string FromUomCode, string ToUomCode), UomConversionSnapshot> conversions;

    private PlanningUomConverter(IReadOnlyDictionary<(string FromUomCode, string ToUomCode), UomConversionSnapshot> conversions)
    {
        this.conversions = conversions;
    }

    public static PlanningUomConverter Create(IReadOnlyCollection<UomConversionSnapshot> conversions)
    {
        return new PlanningUomConverter(conversions
            .GroupBy(x => (Normalize(x.FromUomCode), Normalize(x.ToUomCode)))
            .ToDictionary(x => x.Key, x => x.First()));
    }

    public decimal Convert(
        string triggerSkuCode,
        string fromUomCode,
        string toUomCode,
        decimal quantity,
        string targetDescription)
    {
        if (TryConvert(triggerSkuCode, fromUomCode, toUomCode, quantity, targetDescription, out var converted))
        {
            return converted;
        }

        throw new InvalidOperationException($"Missing global UOM conversion from '{fromUomCode}' to {targetDescription} '{toUomCode}' while normalizing SKU '{triggerSkuCode}'.");
    }

    public bool TryConvert(
        string triggerSkuCode,
        string fromUomCode,
        string toUomCode,
        decimal quantity,
        string targetDescription,
        out decimal converted)
    {
        if (string.Equals(Normalize(fromUomCode), Normalize(toUomCode), StringComparison.Ordinal))
        {
            converted = quantity;
            return true;
        }

        if (!conversions.TryGetValue((Normalize(fromUomCode), Normalize(toUomCode)), out var conversion))
        {
            converted = 0m;
            return false;
        }

        if (conversion.Factor <= 0m)
        {
            throw new InvalidOperationException($"Invalid global UOM conversion from '{fromUomCode}' to {targetDescription} '{toUomCode}' while normalizing SKU '{triggerSkuCode}': factor must be positive.");
        }

        converted = Round(quantity * conversion.Factor + conversion.Offset, conversion.Precision, conversion.RoundingMode);
        if (converted < 0m)
        {
            throw new InvalidOperationException($"Invalid global UOM conversion from '{fromUomCode}' to {targetDescription} '{toUomCode}' while normalizing SKU '{triggerSkuCode}': negative quantity after conversion is not allowed.");
        }

        return true;
    }

    private static decimal Round(decimal value, int precision, string roundingMode)
    {
        var digits = Math.Clamp(precision, 0, 12);
        return Normalize(roundingMode) switch
        {
            "BANKERS" or "TO-EVEN" or "TOEVEN" => Math.Round(value, digits, MidpointRounding.ToEven),
            "CEILING" or "UP" => RoundToward(value, digits, ceiling: true),
            "FLOOR" or "DOWN" => RoundToward(value, digits, ceiling: false),
            _ => Math.Round(value, digits, MidpointRounding.AwayFromZero),
        };
    }

    private static decimal RoundToward(decimal value, int digits, bool ceiling)
    {
        var scale = (decimal)Math.Pow(10, digits);
        return (ceiling ? Math.Ceiling(value * scale) : Math.Floor(value * scale)) / scale;
    }

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();
}
