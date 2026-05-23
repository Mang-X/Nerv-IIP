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
}
