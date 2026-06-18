namespace Nerv.IIP.Business.ProductEngineering.Domain;

internal static class ProductEngineeringGuards
{
    public static string Required(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value cannot be blank.", nameof(value)) : value.Trim();
    }

    public static decimal Positive(decimal value, string parameterName, string message = "Quantity must be positive.")
    {
        return value > 0 ? value : throw new ArgumentOutOfRangeException(parameterName, message);
    }

    public static decimal NonNegative(decimal value, string parameterName, string message = "Value cannot be negative.")
    {
        return value >= 0 ? value : throw new ArgumentOutOfRangeException(parameterName, message);
    }

    public static decimal Yield(decimal value, string parameterName)
    {
        return value is > 0 and <= 1
            ? value
            : throw new ArgumentOutOfRangeException(parameterName, "Yield rate must be greater than 0 and less than or equal to 1.");
    }

    public static string? Optional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
