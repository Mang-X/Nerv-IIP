namespace Nerv.IIP.Business.IndustrialTelemetry.Domain;

public static class IndustrialTelemetryText
{
    public static string Required(string value, string parameterName)
    {
        return string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value is required.", parameterName)
            : value.Trim();
    }

    public static string RequiredLower(string value, string parameterName)
    {
        return Required(value, parameterName).ToLowerInvariant();
    }

    public static string? Optional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public static string? OptionalSanitized(string? value, int maximumLength)
    {
        if (maximumLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumLength));
        }

        var optional = Optional(value);
        if (optional is null)
        {
            return null;
        }

        var sanitized = new string(optional.Select(character => char.IsControl(character) ? ' ' : character).ToArray()).Trim();
        if (sanitized.Length == 0)
        {
            return null;
        }

        return sanitized.Length <= maximumLength ? sanitized : sanitized[..maximumLength];
    }
}
