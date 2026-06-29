namespace Nerv.IIP.Business.Erp.Domain.AggregatesModel;

internal static class ErpText
{
    public static string Required(string value, string parameterName)
    {
        return string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value cannot be blank.", parameterName)
            : value.Trim();
    }

    public static string? Optional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public static decimal Positive(decimal value, string parameterName)
    {
        return value <= 0 ? throw new ArgumentOutOfRangeException(parameterName, value, "Value must be positive.") : value;
    }

    public static decimal NonNegative(decimal value, string parameterName)
    {
        return value < 0 ? throw new ArgumentOutOfRangeException(parameterName, value, "Value cannot be negative.") : value;
    }
}
