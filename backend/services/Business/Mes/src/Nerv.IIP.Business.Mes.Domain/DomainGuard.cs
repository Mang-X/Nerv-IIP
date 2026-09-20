namespace Nerv.IIP.Business.Mes.Domain;

internal static class DomainGuard
{
    public static string Required(string value, string parameterName)
    {
        return string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value cannot be blank.", parameterName)
            : value.Trim();
    }

    public static string RequiredBounded(string value, string parameterName, int maxLength)
    {
        var normalized = Required(value, parameterName);
        return normalized.Length <= maxLength
            ? normalized
            : throw new ArgumentOutOfRangeException(parameterName, $"Value cannot exceed {maxLength} characters.");
    }

    public static decimal Positive(decimal value, string parameterName)
    {
        return value > 0
            ? value
            : throw new ArgumentOutOfRangeException(parameterName, "Quantity must be positive.");
    }

    public static decimal NonNegative(decimal value, string parameterName)
    {
        return value >= 0
            ? value
            : throw new ArgumentOutOfRangeException(parameterName, "Quantity cannot be negative.");
    }
}
